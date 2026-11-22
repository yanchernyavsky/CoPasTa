using System;
using System.Threading;
using System.Windows.Forms;

namespace CoPasTa;

/// <summary>
/// Отдельный STA-поток с очередью сообщений для работы с UI Automation, буфером и SendKeys.
/// Не блокирует поток UI (и поэтому не тормозит отпускание кнопки мыши).
/// </summary>
internal sealed class ClipboardWorker : IDisposable
{
    private readonly Thread _thread;
    private readonly AutoResetEvent _started = new(false);
    private Control? _invoker;

    internal ClipboardWorker()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "CoPasTa clipboard worker"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _started.WaitOne();
        _started.Dispose();
    }

    private void Run()
    {
        // Control создаёт дескриптор и очередь сообщений в этом потоке.
        _invoker = new Control();
        _ = _invoker.Handle;
        _started.Set();
        Application.Run();
    }

    internal void Post(Action action)
    {
        Control? invoker = _invoker;
        if (invoker is null || invoker.IsDisposed)
            return;

        invoker.BeginInvoke(action);
    }

    public void Dispose()
    {
        Control? invoker = _invoker;
        if (invoker is not null && !invoker.IsDisposed)
            invoker.BeginInvoke(new Action(Application.ExitThread));

        if (_thread.IsAlive)
            _thread.Join(1000);

        invoker?.Dispose();
    }
}
