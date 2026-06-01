using System;

namespace Konserva.Utilities;

/// <summary>
/// Обёртка над TimeProvider для тестируемости.
/// По умолчанию использует TimeProvider.System.
/// Может быть переопределён в тестах через SetProvider().
/// </summary>
public static class SystemTime
{
    private static TimeProvider _provider = TimeProvider.System;

    /// <summary>
    /// Текущее локальное время.
    /// </summary>
    public static DateTime Now => _provider.GetLocalNow().DateTime;

    /// <summary>
    /// Текущее UTC время.
    /// </summary>
    public static DateTime UtcNow => _provider.GetUtcNow().DateTime;

    /// <summary>
    /// Возвращает TimeProvider (для передачи в API, которые его принимают).
    /// </summary>
    public static TimeProvider Provider => _provider;

    /// <summary>
    /// Устанавливает провайдер времени (из DI или для тестов).
    /// </summary>
    public static void SetProvider(TimeProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Сбрасывает на системное время (для тестов).
    /// </summary>
    public static void Reset()
    {
        _provider = TimeProvider.System;
    }
}
