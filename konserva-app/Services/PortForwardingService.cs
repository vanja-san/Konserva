using System;
using System.Threading;
using System.Threading.Tasks;
using Konserva.Utilities;
using SharpOpenNat;

namespace Konserva.Services;

/// <summary>
/// Реализация проброса портов через UPnP / NAT-PMP с использованием SharpOpenNat.
/// </summary>
public sealed class PortForwardingService : IPortForwardingService, IDisposable
{
  private INatDevice? _device;
  private string? _lastExternalIp;
  private bool _disposed;
  private readonly SemaphoreSlim _discoverLock = new(1, 1);

  /// <summary>
  /// Ищет UPnP/NAT-PMP роутер в локальной сети. Кэширует результат.
  /// </summary>
  private async Task<INatDevice?> GetDeviceAsync(CancellationToken ct = default)
  {
    if (_device != null)
      return _device;

    await _discoverLock.WaitAsync(ct);
    try
    {
      if (_device != null)
        return _device;

      Logger.Info("Discovering UPnP/NAT-PMP device...", "UPnP");

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      cts.CancelAfter(TimeSpan.FromSeconds(5));

      _device = await OpenNat.Discoverer.DiscoverDeviceAsync(
          PortMapper.Upnp | PortMapper.Pmp, cts.Token);

      Logger.Info($"UPnP device found: {_device.LocalAddress}", "UPnP");
    }
    catch (NatDeviceNotFoundException)
    {
      Logger.Warning("No UPnP device found in network", "UPnP");
    }
    catch (OperationCanceledException)
    {
      Logger.Warning("UPnP discovery cancelled (timeout)", "UPnP");
    }
    catch (Exception ex)
    {
      Logger.Warning($"UPnP discovery error: {ex.Message}", "UPnP");
    }
    finally
    {
      _discoverLock.Release();
    }

    return _device;
  }

  /// <summary>
  /// Проверяет, доступен ли UPnP-роутер.
  /// </summary>
  public async Task<bool> IsAvailableAsync()
  {
    var device = await GetDeviceAsync();
    return device != null;
  }

  /// <summary>
  /// Создаёт проброс порта (TCP) на роутере.
  /// </summary>
  public async Task<bool> CreateMappingAsync(int port, string description)
  {
    var device = await GetDeviceAsync();
    if (device == null)
      return false;

    try
    {
      Logger.Info($"Creating UPnP mapping for port {port} ({description})", "UPnP");

      var mapping = new Mapping(Protocol.Tcp, port, port, description);
      await device.CreatePortMapAsync(mapping);

      Logger.Info($"UPnP mapping created for port {port}", "UPnP");
      return true;
    }
    catch (MappingException ex)
    {
      Logger.Error($"UPnP create mapping failed: {ex.Message}", ex, "UPnP");
      return false;
    }
  }

  /// <summary>
  /// Удаляет проброс порта (TCP) с роутера.
  /// </summary>
  public async Task<bool> DeleteMappingAsync(int port)
  {
    var device = await GetDeviceAsync();
    if (device == null)
      return false;

    try
    {
      Logger.Info($"Deleting UPnP mapping for port {port}", "UPnP");

      var mapping = new Mapping(Protocol.Tcp, port, port);
      await device.DeletePortMapAsync(mapping);

      Logger.Info($"UPnP mapping deleted for port {port}", "UPnP");
      return true;
    }
    catch (MappingException ex)
    {
      // 404/725 — правило уже удалено или не существует
      Logger.Info($"UPnP mapping for port {port} already removed ({ex.Message})", "UPnP");
      return true;
    }
  }

  /// <summary>
  /// Проверяет, существует ли проброс порта (TCP) на роутере.
  /// </summary>
  public async Task<bool> CheckMappingAsync(int port)
  {
    var device = await GetDeviceAsync();
    if (device == null)
      return false;

    try
    {
      Logger.Info($"Checking UPnP mapping for port {port}", "UPnP");

      var mapping = await device.GetSpecificMappingAsync(Protocol.Tcp, port);
      return mapping != null;
    }
    catch (MappingException ex)
    {
      Logger.Info($"UPnP check mapping for port {port} failed ({ex.Message})", "UPnP");
      return false;
    }
    catch (NotSupportedException)
    {
      Logger.Warning("UPnP device does not support GetSpecificMappingAsync", "UPnP");
      return false;
    }
  }

  /// <summary>
  /// Получает внешний (публичный) IP-адрес через UPnP-роутер. Результат кэшируется.
  /// </summary>
  public async Task<string?> GetExternalIpAsync()
  {
    var device = await GetDeviceAsync();
    if (device == null)
      return null;

    try
    {
      var ip = await device.GetExternalIPAsync();
      _lastExternalIp = ip?.ToString();
      return _lastExternalIp;
    }
    catch (MappingException ex)
    {
      Logger.Warning($"Failed to get external IP: {ex.Message}", "UPnP");
      return null;
    }
  }

  /// <summary>
  /// Возвращает последний известный внешний IP без перезапроса к роутеру.
  /// </summary>
  public string? TryGetCachedExternalIp() => _lastExternalIp;

  public void Dispose()
  {
    if (!_disposed)
    {
      _device = null;
      _discoverLock.Dispose();
      _disposed = true;
    }
  }
}
