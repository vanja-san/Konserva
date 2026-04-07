using Konserva.Models;
using Konserva.Utilities;
using Newtonsoft.Json;
using System.IO;

namespace Konserva.Services;

public class ServerStorageService : IServerStorageService, IDisposable
{
    private readonly string _serversIndexPath;
    private readonly Lock _lock = new();
    private List<Server>? _cachedServers;
    private bool _disposed;

    public ServerStorageService()
    {
        var exeDir = AppContext.BaseDirectory;
        var serversDir = Path.Combine(exeDir, "Servers");
        Directory.CreateDirectory(serversDir);
        _serversIndexPath = Path.Combine(serversDir, "servers.json");
    }

    public List<Server> LoadServers()
    {
        lock (_lock)
        {
            if (_cachedServers != null)
                return [.. _cachedServers];

            _cachedServers = LoadServersFromFile();
            return [.. _cachedServers];
        }
    }

    public async Task<List<Server>> LoadServersAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_cachedServers != null)
                return [.. _cachedServers];
        }

        var servers = await LoadServersFromFileAsync(ct);

        lock (_lock)
        {
            _cachedServers = servers;
            return [.. _cachedServers];
        }
    }

    public void SaveServers(List<Server> servers)
    {
        lock (_lock)
        {
            _cachedServers = servers;
            SaveServersToFile(servers);
        }
    }

    public async Task SaveServersAsync(List<Server> servers, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _cachedServers = servers;
        }

        await SaveServersToFileAsyncWithRetry(servers, ct);
    }

    public void AddServer(Server server)
    {
        var servers = LoadServers();
        servers.Add(server);
        SaveServers(servers);
    }

    public void UpdateServer(Server server)
    {
        lock (_lock)
        {
            var servers = _cachedServers ?? LoadServersFromFile();
            var existing = servers.FirstOrDefault(s => s.Id == server.Id);
            if (existing == null)
                return;

            existing.Name = server.Name;
            existing.Path = server.Path;
            existing.McVersion = server.McVersion;
            existing.ModLoader = server.ModLoader;
            existing.Settings = server.Settings;
            existing.Port = server.Port;
            existing.AutoStart = server.AutoStart;
            existing.LastPlayed = server.LastPlayed;

            SaveServersToFile(servers);
            _cachedServers = servers;
        }
    }

    public void DeleteServer(string serverId)
    {
        Server? serverToDelete;

        lock (_lock)
        {
            var servers = _cachedServers ?? LoadServersFromFile();
            serverToDelete = servers.FirstOrDefault(s => s.Id == serverId);
            if (serverToDelete == null)
                return;

            servers.Remove(serverToDelete);
            SaveServersToFile(servers);
            _cachedServers = servers;
        }

        TryDeleteServerFolder(serverToDelete);
    }

    private List<Server> LoadServersFromFile()
    {
        if (!File.Exists(_serversIndexPath))
            return [];

        try
        {
            using var fileStream = new FileStream(_serversIndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);
            var json = reader.ReadToEnd();

            var servers = JsonConvert.DeserializeObject<List<Server>>(json) ?? [];

            foreach (var server in servers)
            {
                server.Status = ServerStatus.Stopped;
                server.InstallStatus = string.Empty;
            }

            // Инициализируем счётчик ID, чтобы новые серверы получали уникальные ID
            Server.InitializeIdCounter(servers);

            return servers;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load servers: {ex.Message}", "ServerStorageService");
            return [];
        }
    }

    private async Task<List<Server>> LoadServersFromFileAsync(CancellationToken ct)
    {
        if (!File.Exists(_serversIndexPath))
            return [];

        try
        {
            await using var fileStream = new FileStream(_serversIndexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            using var reader = new StreamReader(fileStream);
            var json = await reader.ReadToEndAsync(ct);

            var servers = JsonConvert.DeserializeObject<List<Server>>(json) ?? [];

            foreach (var server in servers)
            {
                server.Status = ServerStatus.Stopped;
                server.InstallStatus = string.Empty;
            }

            // Инициализируем счётчик ID, чтобы новые серверы получали уникальные ID
            Server.InitializeIdCounter(servers);

            return servers;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load servers: {ex.Message}", "ServerStorageService");
            return [];
        }
    }

    private void SaveServersToFile(List<Server> servers)
    {
        try
        {
            var json = JsonConvert.SerializeObject(servers, Formatting.Indented);

            // Попытка записи с retry логикой (на случай блокировки файла антивирусом)
            const int maxRetries = 3;
            const int delayMs = 500;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var fileStream = new FileStream(
                        _serversIndexPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        4096,
                        FileOptions.WriteThrough);
                    using var writer = new StreamWriter(fileStream);
                    writer.Write(json);
                    return; // Успешно сохранено
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    // Файл заблокирован, пробуем снова с задержкой
                    Logger.Warning($"Save attempt {attempt}/{maxRetries} failed (file locked): {ex.Message}", "ServerStorageService");
                    Thread.Sleep(delayMs * attempt);
                }
            }

            // Если все попытки не удались
            Logger.Error($"Failed to save servers after {maxRetries} attempts - file may be locked", null, "ServerStorageService");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save servers: {ex.Message}", ex, "ServerStorageService");
        }
    }

    private async Task SaveServersToFileAsync(List<Server> servers, CancellationToken ct)
    {
        try
        {
            var json = JsonConvert.SerializeObject(servers, Formatting.Indented);
            await using var fileStream = new FileStream(_serversIndexPath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            using var writer = new StreamWriter(fileStream);
            await writer.WriteAsync(json.AsMemory(), ct);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save servers: {ex.Message}", ex, "ServerStorageService");
        }
    }

    private async Task SaveServersToFileAsyncWithRetry(List<Server> servers, CancellationToken ct)
    {
        try
        {
            var json = JsonConvert.SerializeObject(servers, Formatting.Indented);

            const int maxRetries = 3;
            const int delayMs = 500;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    await using var fileStream = new FileStream(
                        _serversIndexPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        4096,
                        FileOptions.WriteThrough);
                    using var writer = new StreamWriter(fileStream);
                    await writer.WriteAsync(json.AsMemory(), ct);
                    return;
                }
                catch (IOException ex) when (attempt < maxRetries && !ct.IsCancellationRequested)
                {
                    Logger.Warning($"Save attempt {attempt}/{maxRetries} failed (file locked): {ex.Message}", "ServerStorageService");
                    await Task.Delay(delayMs * attempt, ct);
                }
            }

            Logger.Error($"Failed to save servers after {maxRetries} attempts - file may be locked", null, "ServerStorageService");
        }
        catch (OperationCanceledException)
        {
            // Cancelled - expected
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save servers: {ex.Message}", ex, "ServerStorageService");
        }
    }

    private static void TryDeleteServerFolder(Server? server)
    {
        if (server == null) return;
        _ = TryDeleteServerFolderAsync(server);
    }

    private static async Task TryDeleteServerFolderAsync(Server server)
    {
        if (!Directory.Exists(server.Path))
        {
            Logger.Info($"Server folder does not exist: {server.Path}", "ServerStorageService");
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            try
            {
                Directory.Delete(server.Path, true);
                Logger.Info($"Deleted server folder: {server.Path}", "ServerStorageService");
                return;
            }
            catch (IOException) when (i < 2)
            {
                await Task.Delay(500);
            }
        }

        Logger.Warning($"Could not delete server folder (may be in use): {server.Path}", "ServerStorageService");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
    }
}
