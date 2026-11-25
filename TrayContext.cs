using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CoPasTa;

internal sealed class TrayContext : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const int DragThresholdSquared = 16;   // 4 пикселя
    private const int DoubleClickMs = 500;
    private const int PasteThrottleMs = 300;

    private readonly NotifyIcon _tray;
    private readonly Timer _timer;
    private readonly ClipboardWorker _worker;
    private readonly Bitmap _iconBitmap;
    private readonly Icon _icon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _pasteItem;
    private readonly ToolStripMenuItem _fallbackItem;
    private readonly ToolStripMenuItem _logItem;
    private readonly ToolStripMenuItem _autorunItem;
    private readonly ToolStripMenuItem _pause1;
    private readonly ToolStripMenuItem _pause5;
    private readonly ToolStripMenuItem _pause15;
    private readonly ToolStripMenuItem _resumeItem;

    private bool _enabled = true;
    private DateTime? _pausedUntil;
    private ToolStripMenuItem? _activePauseItem;
    private string _lastCopied = string.Empty;

    private LowLevelMouseProc? _mouseProc;
    private IntPtr _mouseHook;
    private bool _mouseDown;
    private int _downX;
    private int _downY;
    private long _lastDownTick;
    private int _lastDownX;
    private int _lastDownY;
    private long _lastPasteTick;
    private bool _copyPending;
    private bool _pastePending;
    private int _copyQueued;
    private int _pasteQueued;

    public TrayContext()
    {
        (_iconBitmap, _icon) = IconFactory.Create();

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = Program.AppName,
            Visible = true
        };

        _enabledItem = new ToolStripMenuItem("Включено")
        {
            CheckOnClick = true,
            Checked = true
        };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            _enabled = _enabledItem.Checked;
            UpdateStatusText();
        };

        _pause1 = new ToolStripMenuItem("1 минута", null, (_, _) => PauseFor(1, _pause1!));
        _pause5 = new ToolStripMenuItem("5 минут", null, (_, _) => PauseFor(5, _pause5!));
        _pause15 = new ToolStripMenuItem("15 минут", null, (_, _) => PauseFor(15, _pause15!));
        _resumeItem = new ToolStripMenuItem("Снять паузу", null, (_, _) => Resume());

        var pauseMenu = new ToolStripMenuItem("Пауза");
        pauseMenu.DropDownItems.Add(_pause1);
        pauseMenu.DropDownItems.Add(_pause5);
        pauseMenu.DropDownItems.Add(_pause15);
        pauseMenu.DropDownItems.Add(new ToolStripSeparator());
        pauseMenu.DropDownItems.Add(_resumeItem);

        _pasteItem = new ToolStripMenuItem("Вставка по двойному щелчку")
        {
            CheckOnClick = true,
            Checked = true
        };

        _fallbackItem = new ToolStripMenuItem("Резервный способ (Ctrl+C)")
        {
            CheckOnClick = true,
            Checked = false
        };

        _logItem = new ToolStripMenuItem("Диагностический лог")
        {
            CheckOnClick = true,
            Checked = DebugLog.Enabled
        };
        _logItem.CheckedChanged += (_, _) => DebugLog.Enabled = _logItem.Checked;

        _autorunItem = new ToolStripMenuItem("Добавить в автозагрузку")
        {
            CheckOnClick = true,
            Checked = IsAutorun()
        };
        _autorunItem.CheckedChanged += (_, _) => SetAutorun(_autorunItem.Checked);

        var exitItem = new ToolStripMenuItem("Выход", null, (_, _) => ExitThread());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(pauseMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pasteItem);
        menu.Items.Add(_fallbackItem);
        menu.Items.Add(_logItem);
        menu.Items.Add(_autorunItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        _tray.ContextMenuStrip = menu;

        _tray.DoubleClick += (_, _) =>
        {
            _enabled = !_enabled;
            _enabledItem.Checked = _enabled;
        };

        // Тяжёлые вызовы уходят в отдельный STA-поток, чтобы не блокировать UI.
        _worker = new ClipboardWorker();

        _timer = new Timer { Interval = 100 };
        _timer.Tick += OnTick;
        _timer.Start();

        InstallMouseHook();
        Application.ApplicationExit += OnApplicationExit;
        UpdateStatusText();
        DebugLog.Write("CoPasTa started");
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_enabled || _pausedUntil is DateTime until && DateTime.Now < until)
        {
            _copyPending = false;
            _pastePending = false;
            UpdateStatusText();
            return;
        }

        if (_pausedUntil is not null)
        {
            _pausedUntil = null;
            _activePauseItem = null;
            UpdatePauseChecks();
            UpdateStatusText();
        }

        if (_copyPending)
        {
            _copyPending = false;
            if (System.Threading.Interlocked.Exchange(ref _copyQueued, 1) == 0)
            {
                _worker.Post(() =>
                {
                    try
                    {
                        CopySelectionOnce();
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Write($"copy: unexpected error {ex.GetType().Name}");
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _copyQueued, 0);
                    }
                });
            }
        }

        if (_pastePending)
        {
            _pastePending = false;
            long now = Environment.TickCount64;
            if (now - _lastPasteTick >= PasteThrottleMs)
            {
                _lastPasteTick = now;
                if (_pasteItem.Checked && System.Threading.Interlocked.Exchange(ref _pasteQueued, 1) == 0)
                {
                    _worker.Post(() =>
                    {
                        try
                        {
                            PasteOnce();
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Write($"paste: unexpected error {ex.GetType().Name}");
                        }
                        finally
                        {
                            System.Threading.Interlocked.Exchange(ref _pasteQueued, 0);
                        }
                    });
                }
            }
        }
    }

    /// <summary>Выделение закончилось (мышь отпущена) — копируем один раз.</summary>
    private void CopySelectionOnce()
    {
        DebugLog.Write("copy: triggered by mouse-up");

        string? text = SelectionReader.ReadViaUIA();
        DebugLog.Write(text is null ? "copy: UIA returned no text" : $"copy: UIA returned {text.Length} chars");

        if (string.IsNullOrWhiteSpace(text) && _fallbackItem.Checked &&
            SelectionReader.CopyViaKeys(out string? fromKeys))
        {
            text = fromKeys;
            DebugLog.Write("copy: used Ctrl+C fallback");
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            ApplyText(text);
            DebugLog.Write("copy: clipboard updated");
        }
    }

    private void ApplyText(string text)
    {
        if (SelectionReader.ClipboardHas(text))
        {
            _lastCopied = text;
            return;
        }

        if (SelectionReader.SetClipboard(text))
            _lastCopied = text;
    }

    /// <summary>Двойной щелчок в текстовом поле — вставляем текст из буфера.</summary>
    private void PasteOnce()
    {
        DebugLog.Write("paste: triggered by double click");

        string? text = null;
        try
        {
            text = Clipboard.GetText();
        }
        catch
        {
            DebugLog.Write("paste: clipboard read failed");
        }

        if (string.IsNullOrEmpty(text))
            text = _lastCopied;

        DebugLog.Write(text is null ? "paste: nothing to paste" : $"paste: text length {text.Length}");

        if (string.IsNullOrEmpty(text))
            return;

        if (!IsTextInputFocused())
        {
            DebugLog.Write("paste: skipped, focus is not a text field");
            return;
        }

        if (!SelectionReader.ClipboardHas(text))
            SelectionReader.SetClipboard(text);

        try
        {
            SendKeys.SendWait("^v");
            DebugLog.Write("paste: Ctrl+V sent");
        }
        catch
        {
            DebugLog.Write("paste: SendKeys failed");
        }
    }

    private static bool IsTextInputFocused()
    {
        try
        {
            AutomationElement? element = AutomationElement.FocusedElement;
            if (element is null)
            {
                DebugLog.Write("focus: UIA element is null");
            }
            else
            {
                var info = element.Current;
                DebugLog.Write($"focus: UIA {info.ControlType.ProgrammaticName}, class={info.ClassName}, password={info.IsPassword}");

                if (!info.IsPassword)
                {
                    if (info.ControlType == ControlType.Edit)
                        return true;

                    // contenteditable в браузерах часто выглядит как Document
                    if (info.ControlType == ControlType.Document)
                    {
                        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? value) &&
                            value is ValuePattern valuePattern && !valuePattern.Current.IsReadOnly)
                            return true;

                        if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? text) &&
                            text is TextPattern)
                            return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"focus: UIA error {ex.GetType().Name}");
        }

        // Запасная проверка на уровне Win32 (Edit / RichEdit и т.п.).
        try
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            var info = new GuiThreadInfo
            {
                cbSize = Marshal.SizeOf<GuiThreadInfo>()
            };

            if (!NativeMethods.GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == IntPtr.Zero)
            {
                DebugLog.Write("focus: Win32 has no focused window");
                return false;
            }

            var name = new StringBuilder(64);
            NativeMethods.GetClassName(info.hwndFocus, name, name.Capacity);
            string className = name.ToString();
            DebugLog.Write($"focus: Win32 class={className}");

            return className.StartsWith("Edit", StringComparison.OrdinalIgnoreCase) ||
                   className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase) ||
                   className.Equals("RICHEDIT50W", StringComparison.OrdinalIgnoreCase) ||
                   className.Equals("TextBox", StringComparison.OrdinalIgnoreCase) ||
                   className.Equals("SEdit", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DebugLog.Write($"focus: Win32 error {ex.GetType().Name}");
            return false;
        }
    }

    private void PauseFor(int minutes, ToolStripMenuItem item)
    {
        _pausedUntil = DateTime.Now.AddMinutes(minutes);
        _activePauseItem = item;
        UpdatePauseChecks();
        UpdateStatusText();
    }

    private void Resume()
    {
        _pausedUntil = null;
        _activePauseItem = null;
        UpdatePauseChecks();
        UpdateStatusText();
    }

    private void UpdatePauseChecks()
    {
        _pause1.Checked = ReferenceEquals(_activePauseItem, _pause1);
        _pause5.Checked = ReferenceEquals(_activePauseItem, _pause5);
        _pause15.Checked = ReferenceEquals(_activePauseItem, _pause15);
        _resumeItem.Checked = _pausedUntil is not null;
    }

    private void UpdateStatusText()
    {
        string state;
        if (!_enabled)
            state = "отключено";
        else if (_pausedUntil is DateTime until)
            state = "пауза " + Math.Max(0, (int)Math.Ceiling((until - DateTime.Now).TotalMinutes)) + " мин";
        else
            state = "включено";

        _tray.Text = $"{Program.AppName}: {state}";
    }

    private static bool IsAutorun()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Program.AppName) is string value && value.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SetAutorun(bool enable)
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(RunKey, true) ??
                Registry.CurrentUser.CreateSubKey(RunKey);

            if (key is null)
                return;

            if (enable)
                key.SetValue(Program.AppName, $"\"{Application.ExecutablePath}\"", RegistryValueKind.String);
            else
                key.DeleteValue(Program.AppName, false);
        }
        catch
        {
            // нет прав — оставляем как есть
        }
    }

    private void InstallMouseHook()
    {
        _mouseProc = OnMouseMessage;
        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseProc,
            NativeMethods.GetModuleHandle(null),
            0);
        DebugLog.Write(_mouseHook == IntPtr.Zero ? "hook: failed to install" : "hook: installed");
    }

    private IntPtr OnMouseMessage(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();

            if (message == NativeMethods.WmLButtonDown)
            {
                MsLlHookStruct info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                long now = Environment.TickCount64;
                int dx = info.Pt.X - _lastDownX;
                int dy = info.Pt.Y - _lastDownY;

                // Не все приложения посылают WM_LBUTTONDBLCLK в низкоуровневый хук,
                // поэтому определяем двойной щелчок сами по двум нажатиям.
                if (_lastDownTick != 0 &&
                    now - _lastDownTick <= DoubleClickMs &&
                    dx * dx + dy * dy <= DragThresholdSquared)
                {
                    _pastePending = true;
                    _lastDownTick = 0;
                    DebugLog.Write("mouse: double click detected");
                }
                else
                {
                    _lastDownTick = now;
                    _lastDownX = info.Pt.X;
                    _lastDownY = info.Pt.Y;
                }

                _mouseDown = true;
                _downX = info.Pt.X;
                _downY = info.Pt.Y;
            }
            else if (message == NativeMethods.WmLButtonUp && _mouseDown)
            {
                _mouseDown = false;
                MsLlHookStruct info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                int dx = info.Pt.X - _downX;
                int dy = info.Pt.Y - _downY;
                if (dx * dx + dy * dy > DragThresholdSquared)
                {
                    _copyPending = true;
                    DebugLog.Write("mouse: drag finished, copy pending");
                }
            }
            else if (message == NativeMethods.WmLButtonDblClk)
            {
                _pastePending = true;
                DebugLog.Write("mouse: WM_LBUTTONDBLCLK received");
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void OnApplicationExit(object? sender, EventArgs e)
    {
        _timer.Stop();
        _worker.Dispose();
        if (_mouseHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
        _tray.Visible = false;
        _tray.Dispose();
        _icon.Dispose();
        _iconBitmap.Dispose();
        DebugLog.Write("CoPasTa stopped");
    }
}
