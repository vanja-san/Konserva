using Konserva.Utilities;
using SharpOpenNat;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace Konserva.Services;

public sealed class PortForwardingService : IPortForwardingService, IDisposable
{
    internal const string DescriptionPrefix = "Konserva";

    // Таймауты
    private static readonly TimeSpan LocalCheckTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan SoapCallTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan TcpCheckTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ShodanTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExternalIpTimeout = TimeSpan.FromSeconds(4);

    private readonly HttpClient _shodan = new() { Timeout = ShodanTimeout };
    private INatDevice? _device;
    private string? _lastExternalIp;
    private bool _disposed;
    private readonly SemaphoreSlim _discoverLock = new(1, 1);

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

    public async Task<bool> IsAvailableAsync()
    {
        var device = await GetDeviceAsync();
        return device != null;
    }

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
        catch (Exception ex)
        {
            Logger.Warning($"UPnP create mapping for port {port} failed ({ex.GetType().Name}: {ex.Message})", "UPnP");
            return false;
        }
    }

    public async Task<bool> DeleteMappingAsync(int port)
    {
        var device = await GetDeviceAsync();
        if (device == null)
            return false;

        try
        {
            Logger.Info($"Deleting UPnP mapping for port {port}", "UPnP");

            Mapping? existing;
            try
            {
                existing = await device.GetSpecificMappingAsync(Protocol.Tcp, port);
            }
            catch (NotSupportedException)
            {
                Logger.Warning("UPnP device cannot list mappings — refusing to delete by port only", "UPnP");
                return false;
            }
            catch (MappingException ex)
            {
                Logger.Info($"UPnP mapping for port {port} does not exist ({ex.Message})", "UPnP");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"UPnP cannot read mapping for port {port} ({ex.GetType().Name}) — refusing to delete", "UPnP");
                return false;
            }

            if (existing != null && !IsOwnedByKonserva(existing.Description))
            {
                Logger.Warning($"UPnP rule for port {port} was not created by Konserva — leaving it", "UPnP");
                return false;
            }

            var mapping = new Mapping(Protocol.Tcp, port, port);
            await device.DeletePortMapAsync(mapping);
            Logger.Info($"UPnP mapping deleted for port {port}", "UPnP");
            return true;
        }
        catch (MappingException ex)
        {
            Logger.Info($"UPnP mapping for port {port} already removed ({ex.Message})", "UPnP");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warning($"UPnP delete mapping for port {port} failed ({ex.GetType().Name}: {ex.Message})", "UPnP");
            return false;
        }
    }

    internal static bool IsOwnedByKonserva(string? description) =>
        !string.IsNullOrWhiteSpace(description) &&
        description.StartsWith(DescriptionPrefix, StringComparison.OrdinalIgnoreCase);

    // ─────────────────────────────────────────────────────────────
    //  Проверка порта
    // ─────────────────────────────────────────────────────────────

    public async Task<PortCheckResult> CheckMappingAsync(int port)
    {
        using var overallCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // 1. Локально: сервер вообще слушает?
        if (!await IsPortListeningLocallyAsync(port))
        {
            Logger.Info($"Port {port}: not listening on localhost — server is not running", "UPnP");
            return new PortCheckResult(MappingCheckResult.ServerNotRunning, "localhost");
        }
        Logger.Info($"Port {port}: server is listening locally", "UPnP");

        // 2. UPnP — самый быстрый и авторитетный, если работает
        var upnp = await CheckUpnpMappingAsync(port, overallCts.Token);
        Logger.Info($"Port {port}: UPnP result = {upnp}", "UPnP");

        if (upnp == MappingCheckResult.Open)
        {
            // Правило есть — это уже надёжное подтверждение от роутера
            return new PortCheckResult(MappingCheckResult.Open, "UPnP");
        }

        // 3. TCP hairpin — реальный тест «порт доступен снаружи?»
        var externalIp = await GetExternalIpAsyncSafe(overallCts.Token);
        if (!string.IsNullOrEmpty(externalIp))
        {
            Logger.Info($"Port {port}: external IP = {externalIp}", "UPnP");
            var tcp = await CheckViaTcpAsync(externalIp, port, overallCts.Token);
            Logger.Info($"Port {port}: TCP hairpin result = {tcp}", "UPnP");

            if (tcp == MappingCheckResult.Open)
                return new PortCheckResult(MappingCheckResult.Open, "TCP");
        }
        else
        {
            Logger.Warning($"Port {port}: external IP unavailable — cannot run TCP/Shodan checks", "UPnP");
        }

        // 4. Shodan — как третий голос (кэш, может быть устаревшим)
        if (!string.IsNullOrEmpty(externalIp))
        {
            var shodan = await CheckViaShodanAsync(externalIp, port, overallCts.Token);
            Logger.Info($"Port {port}: Shodan result = {shodan}", "UPnP");

            if (shodan == MappingCheckResult.Open)
                return new PortCheckResult(MappingCheckResult.Open, "Shodan");

            // Оба источника уверенно говорят Closed
            if (shodan == MappingCheckResult.Closed && upnp == MappingCheckResult.Closed)
                return new PortCheckResult(MappingCheckResult.Closed, "UPnP+Shodan");
        }

        // 5. Если UPnP явно сказал Closed (router ответил, правила нет)
        if (upnp == MappingCheckResult.Closed)
            return new PortCheckResult(MappingCheckResult.Closed, "UPnP");

        // 6. Ничего определённого
        return PortCheckResult.Unknown();
    }

    private async Task<MappingCheckResult> CheckUpnpMappingAsync(int port, CancellationToken ct)
    {
        var device = await GetDeviceAsync(ct);
        if (device == null)
            return MappingCheckResult.Unknown;

        try
        {
            Logger.Info($"Checking UPnP mapping for port {port}", "UPnP");
            var mapping = await WithTimeout(
                device.GetSpecificMappingAsync(Protocol.Tcp, port),
                SoapCallTimeout, ct);
            return mapping != null ? MappingCheckResult.Open : MappingCheckResult.Closed;
        }
        catch (MappingException ex)
        {
            Logger.Info($"UPnP check mapping for port {port}: {ex.Message}", "UPnP");
            return MappingCheckResult.Closed;
        }
        catch (NotSupportedException)
        {
            Logger.Warning("UPnP device does not support GetSpecificMappingAsync", "UPnP");
            return MappingCheckResult.Unknown;
        }
        catch (TimeoutException)
        {
            Logger.Warning($"UPnP GetSpecificMappingAsync timed out after {SoapCallTimeout.TotalSeconds}s", "UPnP");
            return MappingCheckResult.Unknown;
        }
        catch (Exception ex)
        {
            Logger.Warning($"UPnP check mapping for port {port} failed ({ex.GetType().Name}: {ex.Message})", "UPnP");
            return MappingCheckResult.Unknown;
        }
    }

    private static async Task<bool> IsPortListeningLocallyAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(LocalCheckTimeout));
            if (completed != connectTask)
                return false;

            await connectTask;
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetExternalIpAsyncSafe(CancellationToken ct)
    {
        var device = await GetDeviceAsync(ct);
        if (device == null)
            return null;

        try
        {
            var ip = await WithTimeout(device.GetExternalIPAsync(), ExternalIpTimeout, ct);
            _lastExternalIp = ip?.ToString();
            return _lastExternalIp;
        }
        catch (TimeoutException)
        {
            Logger.Warning($"GetExternalIPAsync timed out after {ExternalIpTimeout.TotalSeconds}s", "UPnP");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning($"GetExternalIPAsync failed ({ex.GetType().Name}: {ex.Message})", "UPnP");
            return null;
        }
    }

    private static async Task<MappingCheckResult> CheckViaShodanAsync(string externalIp, int port, CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = ShodanTimeout };
            using var response = await client.GetAsync(
                $"https://internetdb.shodan.io/{externalIp}", ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                Logger.Info($"Shodan: no data for {externalIp} → treating as closed", "UPnP");
                return MappingCheckResult.Closed;
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning($"Shodan returned {(int)response.StatusCode} for {externalIp}", "UPnP");
                return MappingCheckResult.Unknown;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("ports", out var ports) &&
                ports.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in ports.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.Number && p.GetInt32() == port)
                        return MappingCheckResult.Open;
                }
            }

            return MappingCheckResult.Closed;
        }
        catch (Exception ex)
        {
            Logger.Info($"Shodan check failed ({ex.GetType().Name}: {ex.Message})", "UPnP");
            return MappingCheckResult.Unknown;
        }
    }

    private static async Task<MappingCheckResult> CheckViaTcpAsync(string externalIp, int port, CancellationToken ct)
    {
        try
        {
            if (!IPAddress.TryParse(externalIp, out var ip))
                return MappingCheckResult.Unknown;

            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(TcpCheckTimeout, ct));
            if (completed != connectTask)
                return MappingCheckResult.Unknown;   // таймаут — hairpin не поддерживается или порт закрыт

            await connectTask;
            return client.Connected ? MappingCheckResult.Open : MappingCheckResult.Unknown;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            // Refused — реальный закрытый порт (не таймаут)
            return MappingCheckResult.Closed;
        }
        catch
        {
            return MappingCheckResult.Unknown;
        }
    }

    /// <summary>
    /// Обёртка для вызова SharpOpenNat с таймаутом.
    /// </summary>
    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken ct)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout, ct));
        if (completed != task)
            throw new TimeoutException();

        return await task;
    }

    public async Task<string?> GetExternalIpAsync()
    {
        return await GetExternalIpAsyncSafe(CancellationToken.None);
    }

    public string? TryGetCachedExternalIp() => _lastExternalIp;

    public void Dispose()
    {
        if (!_disposed)
        {
            _device = null;
            _shodan.Dispose();
            _discoverLock.Dispose();
            _disposed = true;
        }
    }
}
