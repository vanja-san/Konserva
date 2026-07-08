namespace Konserva.Services;

/// <summary>
/// Сервис для автоматического проброса портов через UPnP/NAT-PMP.
/// </summary>
public interface IPortForwardingService
{
    /// <summary>
    /// Проверяет, доступен ли UPnP-роутер в сети.
    /// </summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Создаёт проброс порта (TCP) на роутере.
    /// </summary>
    /// <param name="port">Порт для проброса.</param>
    /// <param name="description">Описание правила (отображается в админке роутера).</param>
    Task<bool> CreateMappingAsync(int port, string description);

    /// <summary>
    /// Удаляет проброс порта (TCP) с роутера.
    /// </summary>
    Task<bool> DeleteMappingAsync(int port);

    /// <summary>
    /// Проверяет, существует ли проброс порта (TCP) на роутере.
    /// </summary>
    /// <param name="port">Порт для проверки.</param>
    /// <returns>true, если проброс существует.</returns>
    Task<bool> CheckMappingAsync(int port);

    /// <summary>
    /// Получает внешний IP-адрес (публичный) через UPnP-роутер.
    /// </summary>
    Task<string?> GetExternalIpAsync();

    /// <summary>
    /// Возвращает последний известный внешний IP без обращения к роутеру.
    /// </summary>
    string? TryGetCachedExternalIp();
}
