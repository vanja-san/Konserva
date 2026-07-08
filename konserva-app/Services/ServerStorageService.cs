using Konserva.Models;
using Konserva.Utilities;
using System.IO;

namespace Konserva.Services;

public class ServerStorageService : IServerStorageService, IDisposable
{
    private readonly FileBasedStore<List<Server>> _store;
    private readonly string _serversPath;
    private readonly string _serversIndexPath;
    private bool _disposed;

    public string ServersPath => _serversPath;

    public ServerStorageService(IConfigService configService)
    {
        _serversPath = configService.GetConfig().ServersDirectory;
        Directory.CreateDirectory(_serversPath);
        _serversIndexPath = Path.Combine(_serversPath, "servers.json");
        _store = new FileBasedStore<List<Server>>(_serversIndexPath);
    }

    public List<Server> LoadServers()
        => PostProcessServers(_store.Load() ?? []);

    public async Task<List<Server>> LoadServersAsync(CancellationToken ct = default)
        => PostProcessServers(await _store.LoadAsync(ct) ?? []);

    public void SaveServers(List<Server> servers)
        => _store.Save(servers);

    public async Task SaveServersAsync(List<Server> servers, CancellationToken ct = default)
        => await _store.SaveAsync(servers, ct);

    public void AddServer(Server server)
    {
        var servers = LoadServers();
        servers.Add(server);
        SaveServers(servers);
    }

    public void UpdateServer(Server server)
    {
        // Берём кэш напрямую, чтобы не перезагружать весь файл
        var servers = _store.PeekCache() ?? LoadServers();
        var index = servers.FindIndex(s => s.Id == server.Id);
        if (index < 0)
            return;

        // Заменяем весь объект — не нужно вручную копировать каждое свойство
        servers[index] = server;

        _store.Save(servers);
    }

    public void DeleteServer(string serverId)
    {
        Server? serverToDelete;

        var servers = _store.PeekCache() ?? LoadServers();
        serverToDelete = servers.FirstOrDefault(s => s.Id == serverId);
        if (serverToDelete == null)
            return;

        servers.Remove(serverToDelete);
        _store.Save(servers);

        TryDeleteServerFolder(serverToDelete, _serversPath);
    }

    private static List<Server> PostProcessServers(List<Server> servers)
    {
        foreach (var server in servers)
        {
            server.Status = ServerStatus.Stopped;
            server.LastErrorMessage = string.Empty;
        }

        Server.InitializeIdCounter(servers);
        return servers;
    }

    private static void TryDeleteServerFolder(Server? server, string serversPath)
    {
        if (server == null) return;

        // Fire-and-forget с обработкой необработанных исключений
        _ = TryDeleteServerFolderAsync(server, serversPath).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                Logger.Error($"Error deleting server folder: {t.Exception.Flatten().Message}", t.Exception, "ServerStorageService");
            }
        }, TaskScheduler.Default);
    }

    private static async Task TryDeleteServerFolderAsync(Server server, string serversPath)
    {
        if (string.IsNullOrWhiteSpace(server.Path))
        {
            Logger.Warning($"Server path is empty: {server.Id}", "ServerStorageService");
            return;
        }

        // Проверяем что путь не содержит escape-последовательностей
        if (PathValidator.ContainsTraversalSequences(server.Path))
        {
            Logger.Warning($"Server path contains suspicious sequences: {server.Path}", "ServerStorageService");
            return;
        }

        // Проверяем что путь находится внутри директории Servers
        if (!PathValidator.IsPathSafe(server.Path, serversPath))
        {
            Logger.Warning($"Server path is outside allowed directory: {server.Path}", "ServerStorageService");
            return;
        }

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
