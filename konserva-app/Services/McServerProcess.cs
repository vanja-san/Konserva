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
/// Управление процессом сервера Minecraft
/// </summary>
public partial class McServerProcess(Server server, IConfigService? configService = null, IServerInstaller? installer = null) : IDisposable
{
    // Храним installer как nullable — если не передан, будет выброшено исключение при первом обращении
    private readonly IServerInstaller? _installerField = installer;

    private IServerInstaller GetInstaller() => _installerField ?? throw new InvalidOperationException(
        "IServerInstaller is not available. Ensure McServerProcess is created with an installer (e.g., via DI).");
    private Process? _process;
    private readonly StringBuilder _logs = new();
    private readonly List<string> _logLines = [];
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
    /// Получить логи сервера
    /// </summary>
    public IReadOnlyList<string> GetLogs()
    {
        lock (_lock)
        {
            return _logLines.AsReadOnly();
        }
    }

    /// <summary>
    /// Получить полный лог
    /// </summary>
    public string GetFullLog()
    {
        lock (_lock)
        {
            return _logs.ToString();
        }
    }

    /// <summary>
    /// Запустить сервер (синхронная версия)
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
    /// Запустить сервер
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        Logger.Info($"[StartAsync] Starting for server {Server.Name}", "McServerProcess");

        CancellationToken linkedCt;
        lock (_startStopLock)
        {
            // Если уже запускается или запущен — выходим
            if (_isStarting || _process is { HasExited: false })
            {
                Logger.Warning($"[StartAsync] Already starting or running: {Server.Name}", "McServerProcess");
                return;
            }

            _isStarting = true;
            _startCts?.Cancel();
            _startCts = new CancellationTokenSource();

            // Объединяем внешний токен с внутренним
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
    /// Внутренняя логика запуска
    /// </summary>
    private async Task StartInternalAsync(CancellationToken ct)
    {
        Logger.Info($"[StartInternalAsync] Beginning for {Server.Name}", "McServerProcess");

        _intentionalStop = false;  // Сбрасываем флаг намеренной остановки
        _stopCts = new CancellationTokenSource();
        _pendingErrorOutput = null;
        _serverReady = false;  // Сбрасываем флаг готовности
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

            // Modern Forge (47.x+) и NeoForge запускаются через @args файлы, без -jar
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

            // Создаём пустой server.properties, если его нет — иначе Minecraft сервер пишет ERROR в лог
            var serverPropsPath = Path.Combine(Server.Path, "server.properties");
            if (!File.Exists(serverPropsPath))
                File.Create(serverPropsPath).Dispose();

            // Запуск процесса
            StartProcess(javaCheckResult.JavaPath, javaArgs);

            // После создания процесса — сбрасываем флаг запуска
            lock (_startStopLock)
            {
                _isStarting = false;
            }

            _ = MonitorProcessExitAsync();

            // Статус устанавливается в StartProcess после успешного запуска
            AppendLog($" {LocalizationManager.Get("Log_WaitingForStartup")}");
            AppendLog("========================================");
        }
        catch (OperationCanceledException)
        {
            AppendLog($" {LocalizationManager.Get("Log_LaunchCancelled")}");
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
            throw; // Пробрасываем отмену дальше
        }
        catch (Exception ex)
        {
            Logger.Error($"Start failed for {Server.Name}: {ex.Message}", ex, "McServerProcess");
            HandleStartError(ex);
            throw;
        }
    }

    public async Task StopAsync() => await StopInternalAsync();

    /// <summary>
    /// Внутренняя логика остановки (универсальная)
    /// </summary>
    private async Task StopInternalAsync()
    {
        bool isStarting;
        bool isReady;

        lock (_startStopLock)
        {
            isStarting = _isStarting;
            isReady = _serverReady;

            // Если ещё идёт запуск — принудительная отмена
            if (isStarting)
            {
                _startCts?.Cancel();
                _isStarting = false;

                // Просто сбрасываем состояние, не трогаем процесс
                _process = null;
                Status = ServerStatus.Stopped;
                OnStatusChanged?.Invoke(Status);
                _intentionalStop = true;
                AppendLog($" {LocalizationManager.Get("Log_LaunchForceCancelled")}");
                return;
            }
        }

        // Если сервер ещё не запущен — выходим
        if (_process == null || _process.HasExited)
        {
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
            _intentionalStop = true;
            return;
        }

        // Если сервер ещё не загрузился — принудительная остановка
        if (!isReady)
        {
            _intentionalStop = true;
            await ForceKillProcessAsync();
            return;
        }

        // Сервер запущен и готов — graceful остановка
        _intentionalStop = true;
        await GracefulStopAsync();
    }

    /// <summary>
    /// Принудительная остановка процесса (для Starting/не готовых серверов)
    /// </summary>
    private async Task ForceKillProcessAsync()
    {
        try
        {
            _process?.Kill();
            await Task.Run(() => _process?.Dispose());
            AppendLog("");
            AppendLog($" {LocalizationManager.Get("Log_ProcessForceKilled")}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to kill process: {ex.Message}", "McServerProcess");
            AppendLog($"[ERROR] {LocalizationManager.Get("Log_ExitError")} {ex.Message}");
        }
        finally
        {
            _process = null;
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
        }
    }

    /// <summary>
    /// Корректная остановка через команду stop (для Running серверов)
    /// </summary>
    private async Task GracefulStopAsync()
    {
        Status = ServerStatus.Stopping;
        OnStatusChanged?.Invoke(Status);

        try
        {
            SendCommand("stop");
            var startTime = Environment.TickCount64;

            while (_process != null && !_process.HasExited && (Environment.TickCount64 - startTime) < Constants.ServerStopTimeoutMs)
            {
                await Task.Delay(100);
            }

            if (_process is { HasExited: false })
            {
                AppendLog($"[WARN] {LocalizationManager.Get("Log_TimeoutForceKill")}");
                _process.Kill();
                AppendLog($" {LocalizationManager.Get("Log_ProcessForceKilledAfterTimeout")}");
            }
            else
            {
                // Ждём немного для обработки последних строк вывода
                await Task.Delay(Constants.ServerStatusCheckDelayMs);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog($" {LocalizationManager.Get("Log_StopCancelled")}");
            // Принудительное завершение при отмене
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
            // Обновляем статус сразу после завершения
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
        }
    }

    /// <summary>
    /// Отправить команду в сервер
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
    /// Найти jar файл сервера
    /// </summary>
    private string FindServerJar(string path) => GetInstaller().FindServerJar(path);

    /// <summary>
    /// Построить аргументы Java
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Отписка от событий процесса
        if (_process != null)
        {
            _process.OutputDataReceived -= OnOutput;
            _process.ErrorDataReceived -= OnError;
            _process.Dispose();
            _process = null;
        }

        // Отмена всех операций
        _startCts?.Cancel();
        _startCts?.Dispose();
        _startCts = null;

        _stopCts?.Cancel();
        _stopCts?.Dispose();
        _stopCts = null;

        _readyMsgCts?.Cancel();
        _readyMsgCts?.Dispose();
        _readyMsgCts = null;

        // Очистка логов
        lock (_lock)
        {
            _logs.Clear();
            _logLines.Clear();
        }

        // Сброс всех событий (отписка внешних подписчиков)
        OnLog = null;
        OnStatusChanged = null;
        OnPlayersChanged = null;

        // Примечание: _installerField не диспозим — он получен через DI (singleton),
        // фабрика/контейнер управляет его жизненным циклом.

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
