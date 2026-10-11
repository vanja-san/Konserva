using Konserva.Models;
using Konserva.Utilities;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;

namespace Konserva.Services;

/// <summary>
/// Service for managing Minecraft servers
/// </summary>
public class McServerManager(IDispatcher dispatcher, IServerStorageService storage, IConfigService configService, IServerInstaller installer, IPortForwardingService? portForwarding = null) : IServerManager
{
    // Create a separate copy of the list so that _servers is not bound to the
    // FileBasedStore internal cache (_cached). This prevents possible ordering
    // discrepancies when the cache is reloaded.
    private readonly List<Server> _servers = [.. storage.LoadServers()];
    private readonly ConcurrentDictionary<string, McServerProcess> _processes = new();
    private readonly ConcurrentDictionary<string, Action<ServerStatus>> _statusHandlers = new();
    private readonly ReaderWriterLockSlim _serversLock = new();
    private readonly IDispatcher _dispatcher = dispatcher;
    private readonly IPortForwardingService? _portForwarding = portForwarding;

    // CPU tracking
    private readonly ConcurrentDictionary<int, (DateTime time, TimeSpan cpu)> _cpuSamples = new();

    // Deferred persistence.
    // Status changes arrive from the process stdout reader thread; serializing the whole
    // server index to disk right there stalls the child process once the OS pipe buffer
    // fills, which stalls the Minecraft server itself. Changes are therefore coalesced
    // here and flushed by a single background writer.
    private readonly ConcurrentDictionary<string, Server> _pendingPersistence = new();
    private readonly SemaphoreSlim _persistenceSignal = new(0, int.MaxValue);
    private CancellationTokenSource? _persistenceCts;
    private Task? _persistenceTask;

    public event Action? OnServersChanged;
    public event Action<Server, string>? OnServerStartError;  // Event raised on a start failure

    /// <summary>
    /// Raises <see cref="OnServersChanged"/> on the UI thread.
    /// <para>
    /// Subscribers mutate WPF state (ObservableCollection, TextBlock.Text), and several of
    /// them are reached from background threads — the process stdout reader and the
    /// status handler below. Raising the event directly from those threads produces
    /// cross-thread exceptions inside the WPF collections, so every raise goes through
    /// the dispatcher.
    /// </para>
    /// </summary>
    private void RaiseServersChanged() => _dispatcher.Post(() => OnServersChanged?.Invoke());

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
                RamMax = 4096,
                EnableUpnp = true
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

        RaiseServersChanged();
        return server;
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
    /// Check: whether the server can be started
    /// </summary>
    private bool CanStartServer(string id, out McServerProcess? existingProcess)
    {
        existingProcess = null;

        if (_processes.TryGetValue(id, out var process))
        {
            // check that the process is not already running (neither Stopped nor Error)
            if (process.Status is not (ServerStatus.Stopped or ServerStatus.Error))
            {
                return false;
            }

            existingProcess = process;
        }

        return true;
    }

    /// <summary>
    /// Removes a process in the Stopped/Error state
    /// </summary>
    private void RemoveStoppedOrErroredProcess(string id, McServerProcess? process)
    {
        if (process is { Status: ServerStatus.Stopped or ServerStatus.Error })
        {
            // Must dispose, not merely unsubscribe: the wrapper owns an OS Process handle
            // and an AutoRestart task that would otherwise start a second Java instance.
            CleanupAndDisposeProcess(id);
        }
    }

    /// <summary>
    /// Removes the process and its status handler without disposing the wrapper.
    /// Prefer <see cref="CleanupAndDisposeProcess"/> — leaving the wrapper alive keeps its
    /// AutoRestart task and OS process handle running outside the manager's control.
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
            // If the process is already gone from the dictionary, still clean up the handler
            _statusHandlers.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Cleans up the process and fully disposes of it
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
    /// Starts the server in the background. Returns a Task that completes after the start attempt.
    /// </summary>
    private async Task StartServerCoreAsync(Server server, CancellationToken ct)
    {
        // Clean up the previous process (if any): unsubscribing alone would leak the
        // Process handle and leave its AutoRestart task free to launch a second
        // server on top of the one we are about to start.
        CleanupAndDisposeProcess(server.Id);

        // Kill hung Java processes from this server folder (they may hold session.lock).
        // WMI enumeration is slow and can block for seconds — keep it off the UI thread.
        // No CancellationToken here: an already-cancelled token would throw here and skip
        // the graceful handling further down. StartAsync(ct) honours it a few lines later.
        await Task.Run(() => KillZombieProcesses(server.Path));

        var process = new McServerProcess(server, configService, installer);
        _processes[server.Id] = process;

        server.Status = ServerStatus.Starting;
        server.LastPlayed = SystemTime.Now;

        Logger.Info($"[StartServerInternal] Starting server {server.Id} ({server.Name})", "McServerManager");

        // Use Interlocked for a thread-safe flag — race condition between OnStatusChanged and Task.Run
        int errorNotified = 0;

        Action<ServerStatus> onStatusChanged = status =>
        {
            server.Status = status;
            if (status is ServerStatus.Running or ServerStatus.Stopped or ServerStatus.Error)
            {
                QueuePersistence(server);
            }

            // If the status became Error and we have not notified about it yet — notify
            if (status == ServerStatus.Error && Interlocked.CompareExchange(ref errorNotified, 1, 0) == 0 && !string.IsNullOrEmpty(process.LastError))
            {
                server.LastErrorMessage = process.LastError;
                QueuePersistence(server);
                OnServerStartError?.Invoke(server, process.LastError);
            }

            // Refresh the UI on any status change (non-blocking)
            RaiseServersChanged();
        };

        _statusHandlers[server.Id] = onStatusChanged;
        process.OnStatusChanged += onStatusChanged;

        try
        {
            Logger.Info($"[StartServerInternal] Calling process.StartAsync() for {server.Name}", "McServerManager");
            await process.StartAsync(ct);

            Logger.Info($"[StartServerInternal] process.Start() completed for {server.Name}", "McServerManager");

            // UPnP: automatic port forwarding if enabled
            if (_portForwarding != null && server.Settings.EnableUpnp)
            {
                var port = server.Port;
                _ = TryCreateUpnpMappingAsync(port, server.Name, server.Id);
            }
        }
        catch (Exception ex)
        {
            if (Interlocked.CompareExchange(ref errorNotified, 1, 0) == 0)
            {
                Logger.Error($"[StartServerInternal] Ошибка запуска сервера {server.Id} ({server.Name}): {ex.Message}", ex, "McServerManager");
                server.Status = ServerStatus.Error;
                server.LastErrorMessage = ex.Message;
                QueuePersistence(server);

                OnServerStartError?.Invoke(server, ex.Message);

                RaiseServersChanged();
            }
        }
    }

    /// <summary>
    /// Schedules a server for persistence on the background writer.
    /// Repeated calls for the same server collapse into a single write.
    /// </summary>
    private void QueuePersistence(Server server)
    {
        _pendingPersistence[server.Id] = server;
        EnsurePersistenceWriter();

        // The semaphore is only used as a wake-up signal; a full count is not an error.
        try { _persistenceSignal.Release(); }
        catch (SemaphoreFullException) { /* a wake-up is already pending */ }
    }

    private void EnsurePersistenceWriter()
    {
        if (_persistenceTask != null)
            return;

        _persistenceCts = new CancellationTokenSource();
        _persistenceTask = PersistenceLoopAsync(_persistenceCts.Token);
    }

    /// <summary>
    /// Drains the pending-persistence queue on the thread pool, coalescing bursts:
    /// a burst of status changes results in one disk write, not one per change.
    /// </summary>
    private async Task PersistenceLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _persistenceSignal.WaitAsync(ct);

                // Short debounce window — lets a burst of changes collapse into one write.
                await Task.Delay(250, ct);

                while (_persistenceSignal.Wait(0))
                {
                    // Drain: the single write below covers all queued servers.
                }

                if (_pendingPersistence.IsEmpty)
                    continue;

                var batch = _pendingPersistence.Values.ToArray();
                _pendingPersistence.Clear();

                try
                {
                    foreach (var server in batch)
                        storage.UpdateServer(server);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to persist server state: {ex.Message}", ex, "McServerManager");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected shutdown
        }
    }

    /// <summary>
    /// Flushes any queued persistence work synchronously (application shutdown).
    /// </summary>
    public void FlushPendingWrites()
    {
        _persistenceCts?.Cancel();
        _persistenceCts?.Dispose();
        _persistenceCts = null;
        _persistenceTask = null;

        FlushPersistence();
    }

    /// <summary>
    /// Writes every queued server out to disk (blocking).
    /// </summary>
    private void FlushPersistence()
    {
        if (_pendingPersistence.IsEmpty)
            return;

        var batch = _pendingPersistence.Values.ToArray();
        _pendingPersistence.Clear();

        try
        {
            foreach (var server in batch)
                storage.UpdateServer(server);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to flush server state: {ex.Message}", ex, "McServerManager");
        }
    }

    public async Task StopServerAsync(string id, CancellationToken ct = default)
    {
        if (_processes.TryRemove(id, out var process))
        {
            try
            {
                await process.StopAsync(ct);
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

        // UPnP: remove the port forwarding after the process is stopped.
        // Doing it first blocks shutdown on a 5-second SSDP discovery while the server
        // is still running and its port is still open.
        await TryDeleteUpnpMappingForServerAsync(id);

        // The final Stopped status was queued by the status handler; write it through
        // before returning so the caller never observes a stale "Running" on disk.
        FlushPersistence();
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
                // Update the existing server with the new values
                existing.Name = server.Name;
                existing.Port = server.Port;
                existing.Settings = server.Settings.Clone();  // Deep copy
                existing.AutoStart = server.AutoStart;
                existing.LastPlayed = server.LastPlayed;
                existing.McVersion = server.McVersion;
                existing.ModLoader = server.ModLoader;
                existing.Path = server.Path;

                // Update the storage while holding the lock
                storage.UpdateServer(existing);
            }
        }
        finally
        {
            _serversLock.ExitWriteLock();
        }

        // Notify the UI after releasing the lock
        OnServersChanged?.Invoke();
    }

    public bool RenameServerFolder(string serverId, string newName, out string? error)
    {
        error = null;

        _serversLock.EnterWriteLock();
        try
        {
            var server = _servers.FirstOrDefault(s => s.Id == serverId);
            if (server == null)
            {
                error = "Server not found";
                return false;
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                error = "Name is empty";
                return false;
            }

            var oldPath = server.Path;
            if (string.IsNullOrEmpty(oldPath))
            {
                error = "Server path is empty";
                return false;
            }

            var parentDir = Path.GetDirectoryName(oldPath);
            if (parentDir == null)
            {
                error = "Cannot determine parent directory";
                return false;
            }

            var newPath = Path.Combine(parentDir, newName);

            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                // The name is the same — only the letter case is updated
                if (!string.Equals(oldPath, newPath, StringComparison.Ordinal))
                {
                    // Only the case changes — has to go through a temporary name
                    var tempPath = Path.Combine(parentDir, newName + "_tmp_rename");
                    Directory.Move(oldPath, tempPath);
                    Directory.Move(tempPath, newPath);
                }
                server.Path = newPath;
                storage.UpdateServer(server);
                return true;
            }

            if (Directory.Exists(newPath))
            {
                error = $"Folder \"{newName}\" already exists";
                return false;
            }

            if (!Directory.Exists(oldPath))
            {
                // The folder does not exist — just update the path
                server.Path = newPath;
                server.Name = newName;
                storage.UpdateServer(server);
                return true;
            }

            Directory.Move(oldPath, newPath);
            server.Path = newPath;
            server.Name = newName;
            storage.UpdateServer(server);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Logger.Error($"RenameServerFolder failed: {ex.Message}", ex, "McServerManager");
            return false;
        }
        finally
        {
            _serversLock.ExitWriteLock();
        }
    }

    public async Task DeleteServerAsync(string id, CancellationToken ct = default)
    {
        Server? server;

        // stop the server process if it is running
        if (_processes.TryRemove(id, out var process))
        {
            if (_statusHandlers.TryRemove(id, out var handler))
            {
                process.OnStatusChanged -= handler;
            }

            try
            {
                await process.StopAsync(ct);
            }
            catch (Exception ex)
            {
                // Must not escape: the wrapper owns an OS handle and a still-registered
                // handler, so both have to be released regardless of the stop outcome.
                Logger.Warning($"Failed to stop server before delete: {ex.Message}", "McServerProcess");
            }
            finally
            {
                process.Dispose();
            }
        }
        else
        {
            _statusHandlers.TryRemove(id, out _);
        }

        // ReaderWriterLockSlim is thread-affine: awaiting inside the lock would release
        // it from a different thread and throw. Mutate under the lock, persist after it.
        List<Server> remaining;
        _serversLock.EnterWriteLock();
        try
        {
            server = _servers.FirstOrDefault(s => s.Id == id);
            if (server == null)
                return;

            _servers.RemoveAll(s => s.Id == id);
            remaining = [.. _servers];
        }
        finally
        {
            _serversLock.ExitWriteLock();
        }

        await storage.SaveServersAsync(remaining, ct);

        // delete the server folder
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

        RaiseServersChanged();
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
                catch (Exception ex)
                {
                    // Process died between the filter and the read, or access was denied.
                    // Logged so a permanent failure is distinguishable from a 0 reading.
                    Logger.Warning($"GetTotalMemoryUsage: {ex.Message}", "McServerManager");
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

            // The Process object is disposed and nulled by the exit monitor concurrently,
            // so the null/HasExited checks must live inside the try as well — outside it
            // an InvalidOperationException escapes into the UI dispatcher.
            try
            {
                var proc = process.Process;
                if (proc == null || proc.HasExited)
                    continue;

                var processId = proc.Id;
                var currentCpu = proc.TotalProcessorTime;
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
            catch (Exception ex)
            {
                Logger.Warning($"GetTotalCpuUsage: {ex.Message}", "McServerManager");
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
    /// Kills "zombie" Java processes left over from previous Konserva runs
    /// that keep the server file locks (for example <c>world/session.lock</c>).
    /// </summary>
    /// <remarks>
    /// The sign of a process is its command line: it contains the path to the server folder
    /// (the process working directory and/or an absolute path to server.jar).
    /// <para>
    /// Previously <c>Process.MainModule.FileName</c> was compared with the server folder, but that is
    /// the path to <c>java.exe</c>/<c>javaw.exe</c> itself (for example <c>C:\Program Files\Java\bin\javaw.exe</c>),
    /// which is never located inside the server folder — a match was impossible, and the method
    /// silently did nothing. Now WMI is used (<c>Win32_Process.CommandLine</c>):
    /// unlike <c>wmic</c> in the console, it returns correct Unicode for Cyrillic paths.
    /// </para>
    /// </remarks>
    internal static void KillZombieProcesses(string serverPath)
    {
        if (string.IsNullOrWhiteSpace(serverPath))
            return;

        try
        {
            var normalizedPath = serverPath.Replace('/', '\\').TrimEnd('\\');

            // Candidates: java/javaw processes whose command line references the server folder.
            foreach (var pid in FindJavaPidsForPath(normalizedPath))
            {
                if (pid == Environment.ProcessId)
                    continue;

                try
                {
                    using var proc = Process.GetProcessById(pid);
                    Logger.Info($"[KillZombieProcesses] Killing zombie PID={pid} for {serverPath}", "McServerManager");
                    proc.Kill(entireProcessTree: true);
                    // No WaitForExit: this runs before the server start and a blocking wait
                    // here freezes the window for seconds per matched process.
                }
                catch (Exception ex)
                {
                    // The process may have exited on its own between the lookup and the kill — that is not an error.
                    Logger.Info($"[KillZombieProcesses] PID={pid} could not be killed: {ex.Message}", "McServerManager");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"[KillZombieProcesses] Failed: {ex.Message}", "McServerManager");
        }
    }

    /// <summary>
    /// Returns the PIDs of java/javaw processes whose command line points to <paramref name="serverPath"/>.
    /// <para>
    /// The WQL query is intentionally fixed: the server path is a free-form user string
    /// (a folder name may contain apostrophes, <c>%</c> or <c>_</c>), and interpolating it
    /// into the query allowed breaking out of the <c>LIKE</c> literal — for example a folder
    /// named <c>x' OR CommandLine LIKE '%</c> turned the predicate into
    /// <c>(A AND B) OR C</c> and matched every java.exe on the machine, all of which were
    /// then killed. Matching is therefore done in managed code on the returned command lines.
    /// </para>
    /// </summary>
    private static List<int> FindJavaPidsForPath(string normalizedServerPath)
    {
        var result = new List<int>();

        var scope = new ManagementScope();
        scope.Connect();

        var query = new ObjectQuery(
            "SELECT ProcessId, CommandLine FROM Win32_Process " +
            "WHERE (Name = 'java.exe' OR Name = 'javaw.exe')");

        using var searcher = new ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();

        foreach (var item in results)
        {
            using var entry = item;

            if (entry["CommandLine"] is not string commandLine || string.IsNullOrEmpty(commandLine))
                continue;

            if (!CommandLineReferencesPath(commandLine, normalizedServerPath))
                continue;

            if (entry["ProcessId"] is uint pid)
                result.Add((int)pid);
        }

        return result;
    }

    /// <summary>
    /// Returns true when <paramref name="commandLine"/> contains <paramref name="path"/> as a
    /// plain substring. Paths are compared after normalizing separators and dropping the
    /// surrounding quotes the JVM launcher may add.
    /// </summary>
    private static bool CommandLineReferencesPath(string commandLine, string path)
    {
        var normalized = path.Replace('/', '\\').TrimEnd('\\');

        // The path may be quoted inside the command line — strip one layer of quotes
        // from each token before comparing.
        foreach (var token in commandLine.Split('"', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = token.Replace('/', '\\').TrimEnd('\\');
            if (candidate.Length > 0 && candidate.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return commandLine.Replace('/', '\\').Contains(normalized, StringComparison.OrdinalIgnoreCase);
    }

    // ===== UPnP helpers =====

    /// <summary>
    /// Fire-and-forget: creates a UPnP port forwarding rule for the server.
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
    /// Removes the UPnP port forwarding rule for the server (synchronous call).
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
    /// Removes the UPnP port forwarding rule for the server (asynchronous call).
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
    /// Removes the port forwarding rule by port number.
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