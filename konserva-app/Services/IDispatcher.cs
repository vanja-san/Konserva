namespace Konserva.Services;

/// <summary>
/// Абстракция над WPF Dispatcher для тестирования и отвязки от UI-типов
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Планирует выполнение действия в UI-потоке (fire-and-forget).
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Выполняет действие асинхронно в UI-потоке.
    /// </summary>
    Task InvokeAsync(Action action);
}
