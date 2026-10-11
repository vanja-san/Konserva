namespace Konserva.Services;

/// <summary>
/// Abstraction over the WPF Dispatcher for testing and to decouple from UI types
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Schedules the action to run on the UI thread (fire-and-forget).
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Executes the action asynchronously on the UI thread.
    /// </summary>
    Task InvokeAsync(Action action);
}
