using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Wpf.Ui.Controls;
using System.Text;
using System.Text.RegularExpressions;

namespace Konserva.Services;

/// <summary>
/// Management of a Minecraft server process
/// </summary>
public partial class McServerProcess(Server server, IConfigService? configService = null, IServerInstaller? installer = null) : IDisposable
{
    // Keep the installer as nullable — if it was not passed in, an exception will be thrown on first access
    private readonly IServerInstaller? _installerField = installer;

    private IServerInstaller GetInstaller() => _installerField ?? throw new InvalidOperationException(
        "IServerInstaller is not available. Ensure McServerProcess is created with an installer (e.g., via DI).");
    private Process? _process;
    private readonly StringBuilder _logs = new();
    private readonly Queue<string> _logLines = new();
    private readonly Lock _lock = new();
    private readonly Lock _startStopLock = new();
    private CancellationTokenSource? _startCts;
    private CancellationTokenSource? _stopCts;
    private CancellationTokenSource? _readyMsgCts;
    private int _playersOnline;
    private string? _pendingErrorOutput;
    private string? _lastJavaDisplayName;
    private int _lastJavaMajorVersion;
    private volatile bool _isStarting;
    private volatile bool _serverReady;
    private volatile bool _intentionalStop;
    private bool _disposed;

    public Server Server { get; } = server;
    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;
    public int PlayersOnline => _playersOnline;
    public string? LastError { get; private set; }
    public Process? Process => _process;

    public event Action<string>? OnLog;
    public event Action<ServerStatus>? OnStatusChanged;
    public event Action<int>? OnPlayersChanged;

    /// <summary>
    /// Gets the server log lines
    /// </summary>
    public IReadOnlyList<string> GetLogs()
    {
        lock (_lock)
        {
            // Exactly a copy, not an AsReadOnly() wrapper: the wrapper reflects changes
            // to the queue, while enumeration happens outside the lock — a concurrent
            // AppendLog would manage to change the collection version and throw
            // InvalidOperationException right in the middle of rendering the console.
            return [.. _logLines];
        }
    }

    /// <summary>
    /// Gets the full log
    /// </summary>
    public string GetFullLog()
    {
        lock (_lock)
        {
            return _logs.ToString();
        }
    }

    /// <summary>
    /// Starts the server (synchronous version)
    /// </summary>
    public void Start()
    {
        Task.Run(() => StartAsync(CancellationToken.None))
            .ContinueWith(t =>
            {
                if (t.Exception?.InnerException is { } ex)
                    Logger.Error($"[Start] Unhandled exception: {ex.Message}", ex, "McServerProcess");
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Starts the server
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        // A disposed instance can still be reached through a pending AutoRestart task:
        // starting here would launch a Java process that is no longer registered
        // in the manager, so nothing would be able to stop it or track it.
        if (_disposed)
        {
            Logger.Warning($"[StartAsync] Ignored: the process wrapper for {Server.Name} is already disposed", "McServerProcess");
            return;
        }

        Logger.Info($"[StartAsync] Starting for server {Server.Name}", "McServerProcess");

        CancellationToken linkedCt;
        lock (_startStopLock)
        {
            // If it is already starting or running — exit
            if (_isStarting || _process is { HasExited: false })
            {
                Logger.Warning($"[StartAsync] Already starting or running: {Server.Name}", "McServerProcess");
                return;
            }

            _isStarting = true;
            _startCts?.Cancel();
            _startCts?.Dispose();
            _startCts = new CancellationTokenSource();

            // Combine the external token with the internal one
            linkedCt = ct == CancellationToken.None
                ? _startCts.Token
                : CancellationTokenSource.CreateLinkedTokenSource(ct, _startCts.Token).Token;
        }

        try
        {
            Logger.Info($"[StartAsync] Calling StartInternalAsync for {Server.Name}", "McServerProcess");
            await StartInternalAsync(linkedCt);
        }
        catch (Exception ex)
        {
            Logger.Error($"[StartAsync] Exception for {Server.Name}: {ex.Message}", ex, "McServerProcess");
            throw;
        }
        finally
        {
            lock (_startStopLock)
            {
                _isStarting = false;
            }
        }
    }

    /// <summary>
    /// Internal start logic
    /// </summary>
    private async Task StartInternalAsync(CancellationToken ct)
    {
        Logger.Info($"[StartInternalAsync] Beginning for {Server.Name}", "McServerProcess");

        _intentionalStop = false;  // Reset the intentional stop flag
        _stopCts = new CancellationTokenSource();
        _pendingErrorOutput = null;
        _serverReady = false;  // Reset the ready flag
        Status = ServerStatus.Starting;
        OnStatusChanged?.Invoke(Status);

        try
        {
            LogHeader(string.Format(LocalizationManager.Get("Log_ServerStarting"), Server.Name));
            AppendLog($" {string.Format(LocalizationManager.Get("Log_ServerInfo"), Server.Id, Server.ModLoader.Type, Server.ModLoader.LoaderVersion ?? "", Server.McVersion)}");
            AppendLog("========================================");

            Logger.Info($"[StartInternalAsync] Checking directory for {Server.Path}", "McServerProcess");
            ct.ThrowIfCancellationRequested();

            if (!Directory.Exists(Server.Path))
                throw new DirectoryNotFoundException($"Папка сервера не найдена: {Server.Path}");

            ValidateEula();

            ct.ThrowIfCancellationRequested();

            var launchType = GetInstaller().GetServerLaunchType(Server.Path);

            var jarFile = FindServerJar(Server.Path);

            // Modern Forge (47.x+) and NeoForge are launched via @args files, without -jar
            if (string.IsNullOrEmpty(jarFile) && launchType is not ServerLaunchType.Forge and not ServerLaunchType.NeoForge)
            {
                var fileList = Directory.Exists(Server.Path)
                    ? string.Join(", ", Directory.GetFiles(Server.Path, "*.jar").Select(Path.GetFileName))
                    : "папка пуста";
                throw new FileNotFoundException($"Не найден jar файл сервера. Файлы в папке: {fileList}");
            }
            if (!string.IsNullOrEmpty(jarFile))
                AppendLog($" {string.Format(LocalizationManager.Get("Log_JarFile"), Path.GetFileName(jarFile), Math.Max(1, new FileInfo(jarFile).Length / (1024 * 1024)))}");

            ct.ThrowIfCancellationRequested();

            Logger.Info($"[StartInternalAsync] Getting Java path for {Server.Name}", "McServerProcess");
            var javaPath = GetJavaPathForServer();
            Logger.Info($"[StartInternalAsync] Java path: {javaPath}", "McServerProcess");

            Logger.Info($"[StartInternalAsync] Checking Java at {javaPath}", "McServerProcess");
            var javaCheckResult = CheckJava(javaPath);
            if (!javaCheckResult.Success)
            {
                Logger.Error($"Java check failed: {javaCheckResult.Error}", null, "McServerProcess");
                LogJavaNotFoundError(javaPath, javaCheckResult.Error);
                throw new FileNotFoundException($"Java не найдена или не работает: {javaPath}. {javaCheckResult.Error}");
            }

            _lastJavaMajorVersion = javaCheckResult.MajorVersion;

            var requiredJavaVersion = GetRequiredJavaVersion(Server.McVersion, launchType);
            if (_lastJavaDisplayName != null)
            {
                AppendLog($" {LocalizationManager.Get("Log_JavaInfo", _lastJavaDisplayName, requiredJavaVersion)}");
            }
            else
                AppendLog($" {LocalizationManager.Get("Log_JavaVersionFallback", javaCheckResult.Version)}");

            ct.ThrowIfCancellationRequested();

            ValidateJavaVersion(javaCheckResult);

            var javaArgs = BuildJavaArgs(jarFile, launchType, javaCheckResult.MajorVersion);
            AppendLog($" {string.Format(LocalizationManager.Get("Log_JavaArgs"), javaArgs)}");
            AppendLog($" {LocalizationManager.Get("Log_LaunchingProcess")}");

            ct.ThrowIfCancellationRequested();

            // Create an empty server.properties if it is missing — otherwise the Minecraft server writes ERROR to the log
            var serverPropsPath = Path.Combine(Server.Path, "server.properties");
            if (!File.Exists(serverPropsPath))
                File.Create(serverPropsPath).Dispose();

            // Process launch
            StartProcess(javaCheckResult.JavaPath, javaArgs);

            // After the process is created — reset the starting flag
            lock (_startStopLock)
            {
                _isStarting = false;
            }

            _ = MonitorProcessExitAsync();

            // The status is set in StartProcess after a successful launch
            AppendLog($" {LocalizationManager.Get("Log_WaitingForStartup")}");
            AppendLog("========================================");
        }
        catch (OperationCanceledException)
        {
            AppendLog($" {LocalizationManager.Get("Log_LaunchCancelled")}");
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
            throw; // Re-throw the cancellation further up
        }
        catch (Exception ex)
        {
            Logger.Error($"Start failed for {Server.Name}: {ex.Message}", ex, "McServerProcess");
            HandleStartError(ex);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default) => await StopInternalAsync(ct);

    /// <summary>
    /// Internal stop logic (universal)
    /// </summary>
    private async Task StopInternalAsync(CancellationToken ct = default)
    {
        bool isStarting;
        bool isReady;

        lock (_startStopLock)
        {
            isStarting = _isStarting;
            isReady = _serverReady;

            // If the start is still in progress — force cancellation
            if (isStarting)
            {
                _startCts?.Cancel();
                _isStarting = false;

                // The Java process may already have been created by StartProcess but the
                // starting flag not yet cleared. Dropping the reference without killing it
                // would leave an orphan holding the game port and world/session.lock.
                KillOrphanProcess();

                Status = ServerStatus.Stopped;
                OnStatusChanged?.Invoke(Status);
                _intentionalStop = true;
                AppendLog($" {LocalizationManager.Get("Log_LaunchForceCancelled")}");
                return;
            }
        }

        // If the server has not been started yet — exit
        if (_process == null || _process.HasExited)
        {
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
            _intentionalStop = true;
            return;
        }

        // If the server has not finished loading yet — force stop
        if (!isReady)
        {
            _intentionalStop = true;
            await ForceKillProcessAsync();
            return;
        }

        // The server is running and ready — graceful stop
        _intentionalStop = true;
        await GracefulStopAsync(ct);
    }

    /// <summary>
    /// Kills the currently referenced OS process (if any) and drops the reference.
    /// <para>
    /// The field is captured and cleared atomically, so a concurrent
    /// <see cref="StartProcess"/> cannot slip a new process into the slot we are about
    /// to dispose — otherwise the freshly started server would be killed.
    /// </para>
    /// </summary>
    private void KillOrphanProcess()
    {
        var process = Interlocked.Exchange(ref _process, null);
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            // The process may have exited on its own between the check and the kill — not an error.
            Logger.Warning($"[KillOrphanProcess] Kill failed for {Server.Name}: {ex.Message}", "McServerProcess");
        }
        finally
        {
            try { process.Dispose(); }
            catch (Exception ex) { Logger.Warning($"[KillOrphanProcess] Dispose failed: {ex.Message}", "McServerProcess"); }
        }
    }

    /// <summary>
    /// Forced stop of the process (for Starting/not yet ready servers)
    /// </summary>
    private async Task ForceKillProcessAsync()
    {
        try
        {
            // Capture the reference before awaiting: _process must not be read inside
            // the Task.Run lambda, otherwise a concurrent restart could hand us a new
            // process and we would dispose the one just launched.
            var process = Interlocked.Exchange(ref _process, null);
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to kill process: {ex.Message}", "McServerProcess");
            }
            finally
            {
                await Task.Run(process.Dispose);
            }

            AppendLog("");
            AppendLog($" {LocalizationManager.Get("Log_ProcessForceKilled")}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"ForceKillProcessAsync failed: {ex.Message}", "McServerProcess");
            AppendLog($"[ERROR] {LocalizationManager.Get("Log_ExitError")} {ex.Message}");
        }
        finally
        {
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
        }
    }

    /// <summary>
    /// Proper stop via the stop command (for Running servers)
    /// </summary>
    private async Task GracefulStopAsync(CancellationToken ct = default)
    {
        Status = ServerStatus.Stopping;
        OnStatusChanged?.Invoke(Status);

        try
        {
            SendCommand("stop");
            var startTime = Environment.TickCount64;

            while (_process != null && !_process.HasExited && (Environment.TickCount64 - startTime) < Constants.ServerStopTimeoutMs)
            {
                await Task.Delay(100, ct);
            }

            if (_process is { HasExited: false })
            {
                AppendLog($"[WARN] {LocalizationManager.Get("Log_TimeoutForceKill")}");
                _process.Kill();
                AppendLog($" {LocalizationManager.Get("Log_ProcessForceKilledAfterTimeout")}");
            }
            else
            {
                // Wait a little to process the last output lines
                await Task.Delay(Constants.ServerStatusCheckDelayMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog($" {LocalizationManager.Get("Log_StopCancelled")}");
            // Force termination on cancellation
            try
            {
                _process?.Kill();
            }
            catch (Exception killEx)
            {
                Logger.Warning($"Failed to kill process on cancel: {killEx.Message}", "McServerProcess");
                AppendLog($"[ERROR] {string.Format(LocalizationManager.Get("Log_KillFailedOnCancel"), killEx.Message)}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Stop error for {Server.Name}: {ex.Message}", ex, "McServerProcess");
            AppendLog($"[ERROR] {LocalizationManager.Get("Log_StopError")} {ex.Message}");
            try
            {
                AppendLog($"[WARN] {LocalizationManager.Get("Log_ForceKillAttempt")}");
                _process?.Kill();
            }
            catch (Exception killEx)
            {
                Logger.Warning($"Failed to kill process during stop: {killEx.Message}", "McServerProcess");
                AppendLog($"[ERROR] {string.Format(LocalizationManager.Get("Log_KillFailed"), killEx.Message)}");
            }
        }
        finally
        {
            // Update the status right after completion
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
        }
    }

    /// <summary>
    /// Sends a command to the server
    /// </summary>
    public void SendCommand(string command)
    {
        Logger.Info($"[SendCommand] Отправка команды: {command}", "McServerProcess");

        if (_process == null || _process.HasExited)
        {
            Logger.Warning($"[SendCommand] Процесс не запущен: process={_process != null}, HasExited={_process?.HasExited.ToString() ?? "N/A"}", "McServerProcess");
            AppendLog($"[WARN] {LocalizationManager.Get("Log_ServerNotRunning", command)}");
            return;
        }

        try
        {
            var cmdLabel = command == "stop" ? "Остановка сервера" : "Console";
            AppendLog($"[{cmdLabel}] {string.Format(LocalizationManager.Get("Log_CommandSent"), command)}");
            Logger.Info($"[SendCommand] Запись в StandardInput: {command}", "McServerProcess");
            _process.StandardInput.WriteLine(command);
            _process.StandardInput.Flush();
            _process.StandardInput.AutoFlush = true;
            Logger.Info($"[SendCommand] Команда отправлена", "McServerProcess");
        }
        catch (Exception ex)
        {
            Logger.Error($"[SendCommand] Ошибка отправки команды: {ex.Message}", ex, "McServerProcess");
            AppendLog($"[ERROR] {string.Format(LocalizationManager.Get("Log_CommandSendError"), command, ex.Message)}");
        }
    }

    /// <summary>
    /// Finds the server jar file
    /// </summary>
    private string FindServerJar(string path) => GetInstaller().FindServerJar(path);

    /// <summary>
    /// Builds the Java arguments
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Unsubscribe from the process events
        if (_process != null)
        {
            _process.OutputDataReceived -= OnOutput;
            _process.ErrorDataReceived -= OnError;
            _process.Dispose();
            _process = null;
        }

        // Cancel all operations
        _startCts?.Cancel();
        _startCts?.Dispose();
        _startCts = null;

        _stopCts?.Cancel();
        _stopCts?.Dispose();
        _stopCts = null;

        _readyMsgCts?.Cancel();
        _readyMsgCts?.Dispose();
        _readyMsgCts = null;

        // Clear the logs
        lock (_lock)
        {
            _logs.Clear();
            _logLines.Clear();
        }

        // Reset all events (unsubscribe external subscribers)
        OnLog = null;
        OnStatusChanged = null;
        OnPlayersChanged = null;

        // Note: _installerField is not disposed — it comes from DI (singleton),
        // the factory/container manages its lifetime.

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
