namespace Konserva.Utilities;

/// <summary>
/// Extension-методы для UI элементов
/// </summary>
public static class UiExtensions
{
    /// <summary>
    /// Выполняет действие в UI потоке (синхронно)
    /// </summary>
    public static void Invoke(this System.Windows.Threading.DispatcherObject dispatcher,
        Action action,
        System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Dispatcher.Invoke(action, priority);
    }

    /// <summary>
    /// Выполняет асинхронное действие в UI потоке
    /// </summary>
    public static async Task InvokeAsync(this System.Windows.Threading.DispatcherObject dispatcher,
        Action action,
        System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.Dispatcher.InvokeAsync(action, priority);
    }

    /// <summary>
    /// Выполняет функцию в UI потоке
    /// </summary>
    public static async Task<T> InvokeAsync<T>(this System.Windows.Threading.DispatcherObject dispatcher,
        Func<T> func,
        System.Windows.Threading.DispatcherPriority priority = System.Windows.Threading.DispatcherPriority.Normal)
    {
        return dispatcher.CheckAccess()
            ? func()
            : await dispatcher.Dispatcher.InvokeAsync(func, priority);
    }

    /// <summary>
    /// Проверка: находится ли поток в UI потоке
    /// </summary>
    public static bool IsOnUiThread(this System.Windows.Threading.DispatcherObject dispatcher) =>
        dispatcher.CheckAccess();

    /// <summary>
    /// Выполняет действие в UI потоке с обработкой ошибок
    /// </summary>
    public static async Task TryInvokeAsync(this System.Windows.Threading.DispatcherObject dispatcher,
        Action action,
        Action<Exception>? onError = null)
    {
        try
        {
            await dispatcher.InvokeAsync(action);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            Logger.Error("UI invocation error", ex, "UiExtensions");
        }
    }
}
