using System.Runtime.CompilerServices;

namespace Konserva.Utilities;

/// <summary>
/// Retry helper — выполняет action до maxAttempts раз с задержкой delayMs между попытками.
/// </summary>
public static class RetryHelper
{
    public static async Task<T?> RetryAsync<T>(Func<Task<T?>> action, int maxAttempts = 3, int delayMs = 2000,
        string? operationName = null, CancellationToken ct = default, bool retryOnCancel = false)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await action();
            }
            catch (OperationCanceledException) when (attempt < maxAttempts && retryOnCancel)
            {
                Logger.Info($"{(operationName ?? "Operation")} cancelled (attempt {attempt}), retrying in {delayMs}ms");
                await Task.Delay(delayMs, ct);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                Logger.Warning($"{(operationName ?? "Operation")} failed (attempt {attempt}/{maxAttempts}): {ex.Message}");
                await Task.Delay(delayMs, ct);
            }
        }

        return default;
    }
}

/// <summary>
/// Extension methods for fire-and-forget Task handling.
/// Вместо <c>_ = Task.Run(...)</c> используйте <c>task.SafeFireAndForget()</c>,
/// чтобы гарантированно логировать исключения.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Запускает Task в fire-and-forget режиме с логированием необработанных исключений.
    /// </summary>
    public static void SafeFireAndForget(
        this Task task,
        [CallerMemberName] string callerName = "",
        string? errorMessage = null)
    {
        if (task == null)
            return;

        if (task.IsCompleted)
        {
            if (task.IsFaulted)
                LogError(task.Exception, callerName, errorMessage);
            return;
        }

        _ = ForgetAsync(task, callerName, errorMessage);
    }

    private static async Task ForgetAsync(Task task, string callerName, string? errorMessage)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Отмена — ожидаемое поведение
        }
        catch (Exception ex)
        {
            LogError(ex, callerName, errorMessage);
        }
    }

    private static void LogError(Exception? exception, string callerName, string? errorMessage)
    {
        if (exception is AggregateException agg)
        {
            foreach (var inner in agg.InnerExceptions)
            {
                Logger.Error(
                    $"{errorMessage ?? "Fire-and-forget task failed"} [{callerName}]: {inner.Message}",
                    inner,
                    callerName);
            }
        }
        else
        {
            Logger.Error(
                $"{errorMessage ?? "Fire-and-forget task failed"} [{callerName}]: {exception?.Message}",
                exception,
                callerName);
        }
    }
}
