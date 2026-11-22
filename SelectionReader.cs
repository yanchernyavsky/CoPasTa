using System;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace CoPasTa;

/// <summary>
/// Читает выделенный текст. Сначала пытается сделать это «правильно» через UI Automation
/// (не трогая буфер и не изменяя выделение). Если приложение не поддерживает UIA —
/// используется запасной способ с имитацией Ctrl+C и восстановлением буфера при неудаче.
/// </summary>
internal static class SelectionReader
{
    internal static string? ReadViaUIA()
    {
        AutomationElement? element;
        try
        {
            element = AutomationElement.FocusedElement;
        }
        catch
        {
            return null;
        }

        if (element is null)
            return null;

        try
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object? pattern) ||
                pattern is not TextPattern text)
                return null;

            TextPatternRange[] ranges = text.GetSelection();
            if (ranges is null || ranges.Length == 0)
                return null;

            var builder = new StringBuilder();
            foreach (TextPatternRange range in ranges)
            {
                string? chunk = range.GetText(-1);
                if (!string.IsNullOrEmpty(chunk))
                    builder.Append(chunk);
            }

            return builder.Length == 0 ? null : builder.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static bool ClipboardHas(string text)
    {
        try
        {
            return string.Equals(Clipboard.GetText(), text, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    internal static bool SetClipboard(string text)
    {
        try
        {
            // copy: true и повторы нужны, если буфер на короткое время занят другим процессом
            Clipboard.SetDataObject(text, true, 3, 30);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool CopyViaKeys(out string? text)
    {
        text = null;

        // Снимаем только текст: GetDataObject() перебирает все форматы буфера
        // (картинки, файлы и т.д.) и из-за этого может заметно подтормаживать.
        string? previous = null;
        try
        {
            previous = Clipboard.GetText();
        }
        catch
        {
            // буфер может быть занят
        }

        try
        {
            SendKeys.SendWait("^c");

            // Ждём изменения буфера короткими шагами вместо фиксированной паузы 70 мс.
            for (int i = 0; i < 15; i++)
            {
                string? current = null;
                try
                {
                    current = Clipboard.GetText();
                }
                catch
                {
                    // следующий шаг
                }

                if (!string.Equals(current, previous, StringComparison.Ordinal))
                {
                    text = current;
                    break;
                }

                Thread.Sleep(10);
            }

            if (text is null)
                text = previous; // скопировали тот же самый текст или Ctrl+C ничего не изменил
        }
        catch
        {
            // приложение могло не принять нажатие
        }

        if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(previous))
        {
            try
            {
                Clipboard.SetDataObject(previous, true, 3, 30);
            }
            catch
            {
                // оставляем буфер как есть
            }
        }

        return !string.IsNullOrEmpty(text);
    }
}
