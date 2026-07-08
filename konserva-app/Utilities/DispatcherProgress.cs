using System.Windows.Threading;

namespace Konserva.Utilities;

/// <summary>
/// IProgress&lt;T&gt; implementation that always dispatches the callback to the specified Dispatcher (UI thread).
/// Unlike Progress&lt;T&gt;, this does NOT rely on SynchronizationContext.Current at construction time,
/// making it reliable in all async contexts.
/// </summary>
public sealed class DispatcherProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherPriority _priority;

    public DispatcherProgress(Action<T> handler, Dispatcher dispatcher, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _handler = handler;
        _dispatcher = dispatcher;
        _priority = priority;
    }

    public void Report(T value)
    {
        // Всегда диспатчим на UI-поток — даже если уже на нём,
        // Dispatcher.Invoke исполнит синхронно
        try
        {
            _dispatcher.Invoke(_handler, _priority, value);
        }
        catch (TaskCanceledException)
        {
            // Dispatcher завершает работу — окно закрывается, игнорируем
        }
    }
}
