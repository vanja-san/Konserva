using Konserva.Models;
using Konserva.Utilities;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Konserva.Services;

/// <summary>
/// Сервис управления серверами Minecraft
/// </summary>
public class McServerManager(IServerStorageService storage, IConfigService configService, IPortForwardingService? portForwarding = null) : IServerManager
{
    // Создаём отдельную копию списка, чтобы _servers не был привязан к
    // внутреннему кэшу FileBasedStore (_cached). Это предотвращает возможные
    // расхождения порядка при перезагрузках кэша.
    private readonly List<Server> _servers = [.. storage.LoadServers()];
    private readonly ConcurrentDictionary<string, McServerProcess> _processes = new();
    private readonly ConcurrentDictionary<string, Action<ServerStatus>> _statusHandlers = new();
    private readonly ReaderWriterLockSlim _serversLock = new();
    private readonly IPortForwardingService? _portForwarding = portForwarding;

    // CPU tracking
    private readonly ConcurrentDictionary<int, (DateTime time, TimeSpan cpu)> _cpuSamples = new();

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
        // fire-and-forget для синхронного вызова
        _ = StartServerCoreAsync(server, CancellationToken.None);
    }

    public async Task StartServerAsync(string id, CancellationToken ct = default)
    {
        var server = GetServer(id);
        if (server == null)
            return;

        if (!CanStartServer(id, out var existingProcess))
            return;

        RemoveStoppedOrErroredProcess(id, existingProcess);
        await StartServerCoreAsync(server, ct);
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
            CleanupProcess(id);
        }
    }

    /// <summary>
    /// Очищает процесс: отписывает обработчики событий, удаляет из словаря
    /// </summary>
    private void CleanupProcess(string id)
    {
        if (_processes.TryRemove(id, out var process))
        {
            if (_statusHandlers.TryRemove(id, out var handler))
            {
                process.OnStatusChanged -= handler;
            }
        }
        else
        {
            // Если процесса уже нет в словаре, всё равно чистим обработчик
            _statusHandlers.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Очищает процесс и полностью утилизирует его
    /// </summary>
    private void CleanupAndDisposeProcess(string id)
    {
        if (_processes.TryRemove(id, out var process))
        {
            if (_statusHandlers.TryRemove(id, out var handler))
            {
                process.OnStatusChanged -= handler;
            }
            process.Dispose();
        }
        else
        {
            _statusHandlers.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Запускает сервер в фоне. Возвращает Task, который завершается после попытки запуска.
    /// </summary>
    private async Task StartServerCoreAsync(Server server, CancellationToken ct)
    {
        // Очищаем предыдущий процесс (если был), чтобы избежать утечки событий
        CleanupProcess(server.Id);

        // Убиваем зависшие Java процессы из папки этого сервера (могут держать session.lock)
        KillZombieProcesses(server.Path);

        var process = new McServerProcess(server, configService);
        _processes[server.Id] = process;

        server.Status = ServerStatus.Starting;
        server.LastPlayed = SystemTime.Now;

        Logger.Info($"[StartServerInternal] Starting server {server.Id} ({server.Name})", "McServerManager");

        // Используем Interlocked для thread-safe флага — race condition между OnStatusChanged и Task.Run
        int errorNotified = 0;

        Action<ServerStatus> onStatusChanged = status =>
        {
            server.Status = status;
            if (status is ServerStatus.Running or ServerStatus.Stopped or ServerStatus.Error)
            {
                storage.UpdateServer(server);
            }

            // Если статус стал Error и мы ещё не уведовляли об ошибке — уведомляем
            if (status == ServerStatus.Error && Interlocked.CompareExchange(ref errorNotified, 1, 0) == 0 && !string.IsNullOrEmpty(process.LastError))
            {
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

        _statusHandlers[server.Id] = onStatusChanged;
        process.OnStatusChanged += onStatusChanged;

        try
        {
            Logger.Info($"[StartServerInternal] Calling process.StartAsync() for {server.Name}", "McServerManager");
            await process.StartAsync(ct);

            // Ждём немного для проверки статуса (ошибка может произойти асинхронно)
            await Task.Delay(500);

            // Проверяем, не произошла ли ошибка при запуске (thread-safe через Interlocked)
            if (Interlocked.CompareExchange(ref errorNotified, 1, 0) == 0 && process.Status == ServerStatus.Error && !string.IsNullOrEmpty(process.LastError))
            {
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

                // UPnP: автоматический проброс порта, если включено
                if (_portForwarding != null && server.Settings.EnableUpnp)
                {
                    var port = server.Port;
                    _ = TryCreateUpnpMappingAsync(port, server.Name, server.Id);
                }
            }
        }
        catch (Exception ex)
        {
            if (Interlocked.CompareExchange(ref errorNotified, 1, 0) == 0)
            {
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

        storage.UpdateServer(server);

        // Обновляем UI в потоке UI
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            OnServersChanged?.Invoke();
        });
    }

    public void StopServer(string id)
    {
        // UPnP: удаляем проброс порта при остановке (если включено)
        TryDeleteUpnpMappingForServer(id);

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
                    // Отписываем обработчик ТОЛЬКО после полной остановки
                    if (_statusHandlers.TryRemove(id, out var handler))
                    {
                        process.OnStatusChanged -= handler;
                    }
                    process.Dispose();
                }
            });
        }
        else
        {
            _statusHandlers.TryRemove(id, out _);
        }
    }

    public async Task StopServerAsync(string id, CancellationToken ct = default)
    {
        // UPnP: удаляем проброс порта при остановке (если включено)
        await TryDeleteUpnpMappingForServerAsync(id);

        if (_processes.TryRemove(id, out var process))
        {
            try
            {
                await process.StopAsync();
            }
            finally
            {
                if (_statusHandlers.TryRemove(id, out var handler))
                {
                    process.OnStatusChanged -= handler;
                }
                process.Dispose();
            }
        }
        else
        {
            _statusHandlers.TryRemove(id, out _);
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
            if (_statusHandlers.TryRemove(id, out var handler))
            {
                process.OnStatusChanged -= handler;
            }

            await process.StopAsync();
            process.Dispose();
        }
        else
        {
            _statusHandlers.TryRemove(id, out _);
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
        if (!string.IsNullOrWhiteSpace(server.Path) &&
            !PathValidator.ContainsTraversalSequences(server.Path) &&
            PathValidator.IsPathSafe(server.Path, storage.ServersPath) &&
            Directory.Exists(server.Path))
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

    public double GetTotalCpuUsage()
    {
        var now = DateTime.UtcNow;
        double totalCpuPercent = 0;
        var processorCount = Environment.ProcessorCount;
        var activeIds = new HashSet<int>();

        foreach (var (_, process) in _processes)
        {
            if (process.Status != ServerStatus.Running)
                continue;

            var proc = process.Process;
            if (proc == null || proc.HasExited)
                continue;

            try
            {
                _ = proc.Id; // throws if process died
                var currentCpu = proc.TotalProcessorTime;
                var processId = proc.Id;
                activeIds.Add(processId);

                if (_cpuSamples.TryGetValue(processId, out var prev))
                {
                    var cpuDelta = (currentCpu - prev.cpu).TotalSeconds;
                    var timeDelta = (now - prev.time).TotalSeconds;

                    if (timeDelta > 0.5)
                    {
                        var percent = (cpuDelta / (processorCount * timeDelta)) * 100.0;
                        totalCpuPercent += Math.Max(0, percent);
                    }
                }

                _cpuSamples[processId] = (now, currentCpu);
            }
            catch
            {
                // Process exited between checks
            }
        }

        // Clean up stale entries
        foreach (var key in _cpuSamples.Keys)
        {
            if (!activeIds.Contains(key))
                _cpuSamples.TryRemove(key, out _);
        }

        return Math.Round(Math.Min(totalCpuPercent, 100.0), 1);
    }

    /// <summary>
    /// Убивает zombie Java процессы, которые могут держать блокировки файлов сервера
    /// </summary>
    internal static void KillZombieProcesses(string serverPath)
    {
        try
        {
            var normalizedPath = serverPath.Replace('/', '\\').TrimEnd('\\');

            foreach (var proc in Process.GetProcessesByName("java").Concat(Process.GetProcessesByName("javaw")))
            {
                try
                {
                    if (proc.Id == Environment.ProcessId) continue;

                    // MainModule работает корректно с кириллицей в путях (в отличие от wmic)
                    // Используем StartsWith вместо Contains, чтобы избежать ложных срабатываний
                    // когда имя папки сервера является подстрокой другой папки
                    var fileName = proc.MainModule?.FileName;
                    if (fileName != null &&
                        (fileName.StartsWith(normalizedPath + "\\", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(fileName, normalizedPath, StringComparison.OrdinalIgnoreCase)))
                    {
                        Logger.Info($"[KillZombieProcesses] Killing zombie PID={proc.Id} for {serverPath}", "McServerManager");
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(5000);
                    }
                }
                catch
                {
                    // Нет доступа к процессу — пропускаем
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"[KillZombieProcesses] Failed: {ex.Message}", "McServerManager");
        }
    }

    // ===== UPnP helpers =====

    /// <summary>
    /// Fire-and-forget: создаёт UPnP проброс порта для сервера.
    /// </summary>
    private async Task TryCreateUpnpMappingAsync(int port, string serverName, string serverId)
    {
        try
        {
            if (_portForwarding == null)
                return;

            var description = $"Konserva - {serverName}";
            var result = await _portForwarding.CreateMappingAsync(port, description);

            if (result)
            {
                Logger.Info($"UPnP: Port {port} forwarded for server {serverName} ({serverId})", "McServerManager");
            }
            else
            {
                Logger.Warning($"UPnP: Failed to forward port {port} for server {serverName}", "McServerManager");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"UPnP create mapping error for {serverName}: {ex.Message}", ex, "McServerManager");
        }
    }

    /// <summary>
    /// Удаляет UPnP проброс порта для сервера (синхронный вызов).
    /// </summary>
    private void TryDeleteUpnpMappingForServer(string id)
    {
        if (_portForwarding == null)
            return;

        try
        {
            var server = GetServer(id);
            if (server == null || !server.Settings.EnableUpnp)
                return;

            _ = TryDeleteUpnpMappingAsync(server.Port);
        }
        catch (Exception ex)
        {
            Logger.Warning($"UPnP delete mapping error (sync): {ex.Message}", "McServerManager");
        }
    }

    /// <summary>
    /// Удаляет UPnP проброс порта для сервера (асинхронный вызов).
    /// </summary>
    private async Task TryDeleteUpnpMappingForServerAsync(string id)
    {
        if (_portForwarding == null)
            return;

        try
        {
            var server = GetServer(id);
            if (server == null || !server.Settings.EnableUpnp)
                return;

            await TryDeleteUpnpMappingAsync(server.Port);
        }
        catch (Exception ex)
        {
            Logger.Warning($"UPnP delete mapping error (async): {ex.Message}", "McServerManager");
        }
    }

    /// <summary>
    /// Удаляет проброс порта по номеру порта.
    /// </summary>
    private async Task TryDeleteUpnpMappingAsync(int port)
    {
        try
        {
            if (_portForwarding == null)
                return;

            var result = await _portForwarding.DeleteMappingAsync(port);
            if (result)
            {
                Logger.Info($"UPnP: Port {port} unforwarded", "McServerManager");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"UPnP delete mapping error for port {port}: {ex.Message}", "McServerManager");
        }
    }
}