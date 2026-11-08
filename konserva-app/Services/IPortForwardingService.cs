namespace Konserva.Services;

/// <summary>
/// Статус проверки проброса порта.
/// </summary>
public enum MappingCheckResult
{
    /// <summary>Правило на роутере есть и/или порт доступен снаружи.</summary>
    Open,

    /// <summary>Правила нет и/или порт снаружи недоступен.</summary>
    Closed,

    /// <summary>Не удалось достоверно определить.</summary>
    Unknown,

    /// <summary>Сервер не слушает порт — проверять нечего.</summary>
    ServerNotRunning
}

/// <summary>
/// Результат проверки с указанием источника, подтвердившего статус.
/// <see cref="ConfirmedBy"/>: "UPnP", "TCP", "Shodan", "localhost".
/// </summary>
public sealed record PortCheckResult(MappingCheckResult Status, string? ConfirmedBy)
{
    public static PortCheckResult Unknown() => new(MappingCheckResult.Unknown, null);
}

public interface IPortForwardingService
{
    Task<bool> IsAvailableAsync();
    Task<bool> CreateMappingAsync(int port, string description);
    Task<bool> DeleteMappingAsync(int port);

    /// <summary>
    /// Каскадная проверка порта: localhost → UPnP → TCP hairpin → Shodan.
    /// </summary>
    Task<PortCheckResult> CheckMappingAsync(int port);

    Task<string?> GetExternalIpAsync();
    string? TryGetCachedExternalIp();
}
