namespace Konserva.Utilities;

/// <summary>
/// Wrapper around TimeProvider for testability.
/// Uses TimeProvider.System by default.
/// Can be overridden in tests via SetProvider().
/// </summary>
public static class SystemTime
{
    private static TimeProvider _provider = TimeProvider.System;

    /// <summary>
    /// Current local time.
    /// </summary>
    public static DateTime Now => _provider.GetLocalNow().DateTime;

    /// <summary>
    /// Current UTC time.
    /// </summary>
    public static DateTime UtcNow => _provider.GetUtcNow().DateTime;

    /// <summary>
    /// Returns the TimeProvider (for passing to APIs that accept one).
    /// </summary>
    public static TimeProvider Provider => _provider;

    /// <summary>
    /// Sets the time provider (from DI or for tests).
    /// </summary>
    public static void SetProvider(TimeProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Resets to system time (for tests).
    /// </summary>
    public static void Reset()
    {
        _provider = TimeProvider.System;
    }
}
