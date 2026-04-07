using Konserva.Models;
using Konserva.Utilities;
using System.Collections.Concurrent;
using System.IO;

namespace Konserva.Services;

/// <summary>
/// Сервис управления серверами Minecraft
/// </summary>
public class McServerManager(IServerStorageService storage, IConfigService configService) : IServerManager
{
    private readonly List<Server> _servers = storage.LoadServers();
    private readonly ConcurrentDictionary<string, McServerProcess> _processes = new();
    private readonly ReaderWriterLockSlim _serversLock = new();

    public event Action? OnServersChanged;
    public event Action<Server, string>? OnServerStartError;  // Событие об ошибке запуска

    public IReadOnlyList<Server> GetServers()
    {
        _serversLock.EnterReadLock();
        try
        {
            return [.. _servers];
        }
        finally
        {
            _serversLock.ExitReadLock();
        }
    }

    public Server? GetServer(string id)
    {
        _serversLock.EnterReadLock();
        try
        {
            return _servers.FirstOrDefault(s => s.Id == id);
        }
        finally
        {
            _serversLock.ExitReadLock();
        }
    }

    public McServerProcess? GetProcess(string id) => _processes.GetValueOrDefault(id);

    public IReadOnlyList<McServerProcess> GetProcesses() => [.. _processes.Values];

    public Server CreateServer(string name, string mcVersion, ModLoader modLoader, string path)
    {
        var server = new Server
        {
            Name = name,
            McVersion = mcVersion,
            ModLoader = modLoader,
            Path = path,
            Settings = new ServerSettings
            {
                RamMin = 1024,
                RamMax = 4096
            }
        };

        _serversLock.EnterWriteLock();
        try
        {
            _servers.Add(server);
            storage.SaveServers(_servers);
        }
        finally
        {
            _serversLock.ExitWriteLock();
        }

        OnServersChanged?.Invoke();
        return server;
    }

    public void StartServer(string id)
    {
        var server = GetServer(id);
        if (server == null)
            return;

        if (!CanStartServer(id, out var existingProcess))
            return;

        RemoveStoppedOrErroredProcess(id, existingProcess);
        StartServerInternal(server);
    }

    public async Task StartServerAsync(string id, CancellationToken ct = default)
    {
        var server = GetServer(id);
        if (server == null)
            return;

        if (!CanStartServer(id, out var existingProcess))
            return;

        RemoveStoppedOrErroredProcess(id, existingProcess);
        StartServerInternal(server);
        await Task.CompletedTask; // для асинхронного интерфейса
    }

    /// <summary>
    /// Проверка: можно ли запустить сервер
    /// </summary>
    private bool CanStartServer(string id, out McServerProcess? existingProcess)
    {
        existingProcess = null;

        if (_processes.TryGetValue(id, out var process))
        {
            // проверяем, что процесс уже не запущен (не Stopped и не Error)
            if (process.Status is not (ServerStatus.Stopped or ServerStatus.Error))
            {
                return false;
            }

            existingProcess = process;
        }

        return true;
    }

    /// <summary>
    /// Удаляем процесс с состоянием Stopped/Error
    /// </summary>
    private void RemoveStoppedOrErroredProcess(string id, McServerProcess? process)
    {
        if (process is { Status: ServerStatus.Stopped or ServerStatus.Error })
        {
            _processes.TryRemove(id, out _);
        }
    }

    private void StartServerInternal(Server server)
    {
        _processes.TryRemove(server.Id, out _);

        var process = new McServerProcess(server, configService);
        _processes[server.Id] = process;

        server.Status = ServerStatus.Starting;
        server.LastPlayed = DateTime.Now;

        Logger.Info($"[StartServerInternal] Starting server {server.Id} ({server.Name})", "McServerManager");

        bool errorNotified = false;

        process.OnStatusChanged += status =>
        {
            server.Status = status;
            if (status is ServerStatus.Running or ServerStatus.Stopped or ServerStatus.Error)
            {
                storage.UpdateServer(server);
            }

            // Если статус стал Error и мы ещё не уведовляли об ошибке — уведомляем
            if (status == ServerStatus.Error && !errorNotified && !string.IsNullOrEmpty(process.LastError))
            {
                errorNotified = true;
                server.InstallStatus = process.LastError;
                storage.UpdateServer(server);
                OnServerStartError?.Invoke(server, process.LastError);
            }

            // Обновляем UI при любом изменении статуса (неблокирующе)
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                OnServersChanged?.Invoke();
            });
        };

        // Запуск сервера в фоне с обработкой исключений
        _ = Task.Run(async () =>
        {
            try
            {
                Logger.Info($"[StartServerInternal] Calling process.Start() for {server.Name}", "McServerManager");
                await Task.Run(() => process.Start());
                
                // Ждём немного для проверки статуса (ошибка может произойти асинхронно)
                await Task.Delay(500);

                // Проверяем, не произошла ли ошибка при запуске
                if (!errorNotified && process.Status == ServerStatus.Error && !string.IsNullOrEmpty(process.LastError))
                {
                    errorNotified = true;
                    Logger.Error($"[StartServerInternal] Server {server.Id} ({server.Name}) failed to start: {process.LastError}", null, "McServerManager");
                    server.Status = ServerStatus.Error;
                    server.InstallStatus = process.LastError;
                    storage.UpdateServer(server);

                    // Уведомляем об ошибке запуска
                    OnServerStartError?.Invoke(server, process.LastError);

                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        OnServersChanged?.Invoke();
                    });
                }
                else
                {
                    Logger.Info($"[StartServerInternal] process.Start() completed for {server.Name}", "McServerManager");
                }
            }
            catch (Exception ex)
            {
                if (!errorNotified)
                {
                    errorNotified = true;
                    Logger.Error($"[StartServerInternal] Ошибка запуска сервера {server.Id} ({server.Name}): {ex.Message}", ex, "McServerManager");
                    server.Status = ServerStatus.Error;
                    server.InstallStatus = ex.Message;
                    storage.UpdateServer(server);

                    // Уведомляем об ошибке запуска
                    OnServerStartError?.Invoke(server, ex.Message);

                    System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        OnServersChanged?.Invoke();
                    });
                }
            }
        });

        storage.UpdateServer(server);

        // Обновляем UI в потоке UI
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            OnServersChanged?.Invoke();
        });
    }

    public void StopServer(string id)
    {
        if (_processes.TryRemove(id, out var process))
        {
            // останавливаем процесс в фоне, чтобы не блокировать UI
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.StopAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error($"[StopServer] Ошибка остановки: {ex.Message}", ex, "McServerManager");
                }
                finally
                {
                    process.Dispose();
                }
            });
        }
    }

    public async Task StopServerAsync(string id, CancellationToken ct = default)
    {
        if (_processes.TryRemove(id, out var process))
        {
            try
            {
                await process.StopAsync();
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    public void SendCommand(string id, string command)
    {
        if (_processes.TryGetValue(id, out var process))
        {
            process.SendCommand(command);
        }
    }

    public void UpdateServer(Server server)
    {
        _serversLock.EnterWriteLock();
        try
        {
            var existing = _servers.FirstOrDefault(s => s.Id == server.Id);
            if (existing != null)
            {
                // Обновляем существующий сервер новыми значениями
                existing.Name = server.Name;
                existing.Port = server.Port;
                existing.Settings = server.Settings.Clone();  // Глубокое копирование
                existing.AutoStart = server.AutoStart;
                existing.LastPlayed = server.LastPlayed;
                existing.McVersion = server.McVersion;
                existing.ModLoader = server.ModLoader;
                existing.Path = server.Path;

                // Обновляем хранилище внутри блокировки
                storage.UpdateServer(existing);
            }
        }
        finally
        {
            _serversLock.ExitWriteLock();
        }

        // Уведомляем UI после выхода из блокировки
        OnServersChanged?.Invoke();
    }

    public async Task DeleteServerAsync(string id, CancellationToken ct = default)
    {
        Server? server;

        // останавливаем процесс сервера, если он запущен
        if (_processes.TryRemove(id, out var process))
        {
            await process.StopAsync();
            process.Dispose();
        }

        _serversLock.EnterWriteLock();
        try
        {
            server = _servers.FirstOrDefault(s => s.Id == id);
            if (server == null)
                return;

            _servers.RemoveAll(s => s.Id == id);
            await storage.SaveServersAsync(_servers, ct);
        }
        finally
        {
            _serversLock.ExitWriteLock();
        }

        // удаляем папку сервера
        if (server?.Path != null && Directory.Exists(server.Path))
        {
            try
            {
                await Task.Run(() => Directory.Delete(server.Path, true), ct);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Не удалось удалить папку сервера: {server.Path}. Ошибка: {ex.Message}", "McServerManager");
            }
        }

        OnServersChanged?.Invoke();
    }

    public (int total, int running, int stopped) GetStats()
    {
        _serversLock.EnterReadLock();
        try
        {
            var running = _processes.Count(p => p.Value.Status == ServerStatus.Running);
            return (_servers.Count, running, _servers.Count - running);
        }
        finally
        {
            _serversLock.ExitReadLock();
        }
    }

    public long GetTotalMemoryUsage() =>
        _processes.Values
            .Where(p => p.Status == ServerStatus.Running)
            .Sum(p =>
            {
                try
                {
                    return p.Process?.WorkingSet64 ?? 0;
                }
                catch
                {
                    return 0;
                }
            });
}