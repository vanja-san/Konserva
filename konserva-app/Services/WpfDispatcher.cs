using System.Windows;

namespace Konserva.Services;

internal sealed class WpfDispatcher : IDispatcher
{
    public void Post(Action action)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(action);
    }

    public Task InvokeAsync(Action action)
    {
        return Application.Current.Dispatcher.InvokeAsync(action).Task;
    }
}
