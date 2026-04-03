using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Konserva.Services;

/// <summary>
/// Управление процессом сервера Minecraft
/// </summary>
public partial class McServerProcess(Server server, IConfigService? configService = null) : IDisposable
{
    private Process? _process;
    private readonly StringBuilder _logs = new();
    private readonly List<string> _logLines = [];
    private readonly Lock _lock = new();
    private readonly Lock _startStopLock = new();
    private CancellationTokenSource? _startCts;
    private CancellationTokenSource? _stopCts;
    private int _playersOnline;
    private string? _pendingErrorOutput;
    private volatile bool _isStarting;
    private volatile bool _serverReady;
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
        _ = Task.Run(async () =>
        {
            try
            {
                await StartAsync();
            }
            catch (Exception ex)
            {
                Logger.Error($"[Start] Unhandled exception: {ex.Message}", ex, "McServerProcess");
            }
        });
    }

    /// <summary>
    /// Запустить сервер
    /// </summary>
    public async Task StartAsync()
    {
        Logger.Info($"[StartAsync] Starting for server {Server.Name}", "McServerProcess");

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
        }

        try
        {
            Logger.Info($"[StartAsync] Calling StartInternalAsync for {Server.Name}", "McServerProcess");
            await StartInternalAsync(_startCts.Token);
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

        _stopCts = new CancellationTokenSource();
        _pendingErrorOutput = null;
        _serverReady = false;  // Сбрасываем флаг готовности
        Status = ServerStatus.Starting;
        OnStatusChanged?.Invoke(Status);

        try
        {
            LogHeader($"Запуск сервера: {Server.Name}");
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_ServerId")}", Server.Id));
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_ModLoader")}", Server.ModLoader.Type));
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_MinecraftVersion")}", Server.McVersion));
            AppendLog("========================================");

            Logger.Info($"[StartInternalAsync] Checking directory for {Server.Path}", "McServerProcess");
            ct.ThrowIfCancellationRequested();

            if (!Directory.Exists(Server.Path))
                throw new DirectoryNotFoundException($"Папка сервера не найдена: {Server.Path}");
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_ServerFolder")}", Server.Path));

            ClearOldServerLogs();
            ValidateEula();

            ct.ThrowIfCancellationRequested();

            var launchType = McServerInstaller.GetServerLaunchType(Server.Path);
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_LaunchType")}", launchType));

            var jarFile = FindServerJar(Server.Path);
            if (string.IsNullOrEmpty(jarFile))
            {
                var fileList = Directory.Exists(Server.Path)
                    ? string.Join(", ", Directory.GetFiles(Server.Path, "*.jar").Select(Path.GetFileName))
                    : "папка пуста";
                throw new FileNotFoundException($"Не найден jar файл сервера. Файлы в папке: {fileList}");
            }
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_JarFile")}", Path.GetFileName(jarFile), new FileInfo(jarFile).Length / 1024 / 1024));

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

            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_JavaVersion")}", javaCheckResult.Version));
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_JavaPath_Info")}", javaCheckResult.JavaPath));

            ct.ThrowIfCancellationRequested();

            ValidateJavaVersion(javaCheckResult);

            var javaArgs = BuildJavaArgs(jarFile, launchType);
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_JavaArgs")}", javaArgs));
            AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_WorkingDirectory")}", Server.Path));
            AppendLog($"[INFO] {LocalizationManager.Get("Log_LaunchingProcess")}");

            ct.ThrowIfCancellationRequested();

            // Запуск процесса
            StartProcess(javaCheckResult.JavaPath, javaArgs);

            // После создания процесса — сбрасываем флаг запуска
            lock (_startStopLock)
            {
                _isStarting = false;
            }

            _ = MonitorProcessExitAsync();

            // Статус устанавливается в StartProcess после успешного запуска
            AppendLog($"[INFO] {LocalizationManager.Get("Log_WaitingForStartup")}");
            AppendLog("========================================");
        }
        catch (OperationCanceledException)
        {
            AppendLog($"[INFO] {LocalizationManager.Get("Log_LaunchCancelled")}");
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
            throw; // Пробрасываем отмену дальше
        }
        catch (Exception ex)
        {
            HandleStartError(ex);
            throw; // Пробрасываем исключение дальше для обработки в McServerManager
        }
    }

    /// <summary>
    /// Проверка eula.txt
    /// </summary>
    private void ValidateEula()
    {
        var eulaPath = Path.Combine(Server.Path, "eula.txt");
        if (!File.Exists(eulaPath))
            throw new FileNotFoundException("Файл eula.txt не найден. Сервер не был установлен корректно.");

        var eulaContent = File.ReadAllText(eulaPath);
        if (!eulaContent.Contains("eula=true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("EULA не принята! Измените eula.txt и установите eula=true");

        AppendLog($"[INFO] {LocalizationManager.Get("Log_EulaAccepted")}");
    }

    /// <summary>
    /// Проверка совместимости версии Java
    /// </summary>
    private void ValidateJavaVersion(JavaCheckResult javaCheckResult)
    {
        var launchType = McServerInstaller.GetServerLaunchType(Server.Path);
        var requiredJavaVersion = GetRequiredJavaVersion(Server.McVersion, launchType);

        Logger.Info($"[ValidateJavaVersion] Required Java {requiredJavaVersion}+, Found Java {javaCheckResult.MajorVersion} ({javaCheckResult.Version})", "McServerProcess");
        Logger.Info($"[ValidateJavaVersion] LaunchType={launchType}, McVersion={Server.McVersion}", "McServerProcess");

        if (javaCheckResult.MajorVersion < requiredJavaVersion)
        {
            LogJavaVersionError(requiredJavaVersion, javaCheckResult);
            Logger.Error($"Java version mismatch: required {requiredJavaVersion}+, found {javaCheckResult.MajorVersion}", null, "McServerProcess");
            throw new InvalidOperationException(
                $"Требуется Java {requiredJavaVersion}+ для Minecraft {Server.McVersion}, но найдена Java {javaCheckResult.MajorVersion} ({javaCheckResult.Version})");
        }

        AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_JavaVersionCompatible")}", requiredJavaVersion));
    }

    /// <summary>
    /// Логирование ошибки версии Java
    /// </summary>
    private void LogJavaVersionError(int required, JavaCheckResult actual)
    {
        AppendLog($"[ERROR] ═══════════════════════════════════════");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_JavaVersionMismatch")}");
        AppendLog($"[ERROR] ═══════════════════════════════════════");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_JavaVersionMismatch_Detail", Server.McVersion, required)}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_JavaFound", actual.Version, actual.MajorVersion)}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_JavaPath", actual.JavaPath)}");
        AppendLog($"[ERROR] ");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_Solution")}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_Solution_Install_Java", required)}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_Solution_Add_Java", required)}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_Solution_Older_Minecraft")}");
        AppendLog($"[ERROR] ═══════════════════════════════════════");
    }

    /// <summary>
    /// Логирование ошибки - Java не найдена
    /// </summary>
    private void LogJavaNotFoundError(string javaPath, string error)
    {
        AppendLog($"[ERROR] ═══════════════════════════════════════");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_JavaNotFound")}");
        AppendLog($"[ERROR] ═══════════════════════════════════════");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_JavaNotFound_Path", javaPath)}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_JavaNotFound_Error", error)}");
        AppendLog($"[ERROR] ");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_Solution")}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_Solution_Install_Java_General")}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_Solution_Add_Java_Settings")}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_Solution_Check_PATH")}");
        AppendLog($"[ERROR] ═══════════════════════════════════════");
    }

    /// <summary>
    /// Запуск процесса
    /// </summary>
    private void StartProcess(string javaPath, string javaArgs)
    {
        var utf8Args = "-Dfile.encoding=UTF-8 -Dconsole.encoding=UTF-8 " + javaArgs;

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = utf8Args,
            WorkingDirectory = Server.Path,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Для Minecraft 1.20.5+ и новых версий (26.1 и т.д.) используем UTF-8
            // Для старых версий - OEM кодировку (866 для России)
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        // Подписка на события ДО запуска процесса
        _process.OutputDataReceived += OnOutput;
        _process.ErrorDataReceived += OnError;

        // Запуск процесса
        if (!_process.Start())
        {
            throw new InvalidOperationException($"Не удалось запустить процесс: {javaPath} {utf8Args}");
        }

        // Начинаем асинхронное чтение вывода
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_ProcessStarted")}", _process.Id));

        // Устанавливаем статус Running после успешного запуска
        Status = ServerStatus.Running;
        OnStatusChanged?.Invoke(Status);
    }

    /// <summary>
    /// Обработка ошибки запуска
    /// </summary>
    private void HandleStartError(Exception ex)
    {
        Status = ServerStatus.Error;
        LastError = ex.Message + (string.IsNullOrEmpty(_pendingErrorOutput)
            ? ""
            : $"\n[Дополнительно: {_pendingErrorOutput}]");
        _process = null;

        OnStatusChanged?.Invoke(Status);

        AppendLog($"[ERROR] ═══════════════════════════════════════");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_CriticalError")}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_ErrorType", ex.GetType().Name)}");
        AppendLog($"[ERROR] {LocalizationManager.Get("Log_ErrorMessage", ex.Message)}");
        if (!string.IsNullOrEmpty(ex.StackTrace))
            AppendLog($"[ERROR] StackTrace: {ex.StackTrace}");
        AppendLog($"[ERROR] ═══════════════════════════════════════");

        if (!string.IsNullOrEmpty(_pendingErrorOutput))
            AppendLog($"[ERROR] {LocalizationManager.Get("Log_ErrorOutput", _pendingErrorOutput)}");
    }

    private void LogHeader(string message)
    {
        AppendLog("========================================");
        AppendLog($"[INFO] {message}");
    }

    /// <summary>
    /// Получить требуемую минимальную версию Java для версии Minecraft
    /// </summary>
    private static int GetRequiredJavaVersion(string mcVersion, McServerInstaller.ServerLaunchType launchType) =>
        JavaVersionParser.GetRequiredJavaVersion(mcVersion, launchType);

    /// <summary>
    /// Результат проверки Java
    /// </summary>
    private sealed class JavaCheckResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public int MajorVersion { get; set; }
        public string JavaPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Проверка доступности Java с получением версии
    /// </summary>
    private static JavaCheckResult CheckJava(string javaPath)
    {
        try
        {
            string actualJavaPath = javaPath;
            string versionOutput = string.Empty;

            if (javaPath == "java")
            {
                // Поиск в PATH
                var startInfo = new ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return new JavaCheckResult { Success = false, Error = "Не удалось запустить процесс java" };

                versionOutput = process.StandardError.ReadToEnd();
                process.WaitForExit(Constants.JavaCheckTimeoutMs);

                if (process.ExitCode != 0)
                    return new JavaCheckResult { Success = false, Error = $"java вернула код ошибки {process.ExitCode}" };

                // Поиск полного пути
                try
                {
                    var pathInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "java",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var pathProcess = Process.Start(pathInfo);
                    if (pathProcess != null)
                    {
                        var pathOutput = pathProcess.StandardOutput.ReadToEnd();
                        pathProcess.WaitForExit(Constants.JavaPathCheckTimeoutMs);
                        var paths = pathOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        if (paths.Length > 0)
                            actualJavaPath = paths[0].Trim();
                    }
                }
                catch { }
            }
            else
            {
                // Проверка полного пути
                if (!File.Exists(javaPath) && !File.Exists(javaPath + ".exe"))
                    return new JavaCheckResult { Success = false, Error = $"Файл не найден: {javaPath}" };

                actualJavaPath = File.Exists(javaPath) ? javaPath : javaPath + ".exe";

                var startInfo = new ProcessStartInfo
                {
                    FileName = actualJavaPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return new JavaCheckResult { Success = false, Error = "Не удалось запустить процесс" };

                versionOutput = process.StandardError.ReadToEnd();
                process.WaitForExit(Constants.JavaCheckTimeoutMs);

                if (process.ExitCode != 0)
                    return new JavaCheckResult { Success = false, Error = $"Java вернула код ошибки {process.ExitCode}" };
            }

            var version = JavaVersionParser.ParseVersion(versionOutput);
            int majorVersion = JavaVersionParser.ParseMajorVersion(versionOutput);

            return new JavaCheckResult
            {
                Success = true,
                Version = version,
                MajorVersion = majorVersion,
                JavaPath = actualJavaPath
            };
        }
        catch (Exception ex)
        {
            return new JavaCheckResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Остановить сервер (асинхронная версия)
    /// </summary>
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
                AppendLog($"[INFO] {LocalizationManager.Get("Log_LaunchForceCancelled")}");
                return;
            }
        }

        // Если сервер ещё не запущен — выходим
        if (_process == null || _process.HasExited)
        {
            Status = ServerStatus.Stopped;
            OnStatusChanged?.Invoke(Status);
            return;
        }

        // Если сервер ещё не загрузился — принудительная остановка
        if (!isReady)
        {
            await ForceKillProcessAsync();
            return;
        }

        // Сервер запущен и готов — graceful остановка
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
            AppendLog($"[INFO] {LocalizationManager.Get("Log_ProcessForceKilled")}");
        }
        catch (Exception ex)
        {
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
        AppendLog($"[INFO] {LocalizationManager.Get("Log_StoppingServer")}");

        try
        {
            AppendLog($"[INFO] {LocalizationManager.Get("Log_SendingStopCommand")}");
            SendCommand("stop");

            // Ждём завершения процесса с таймаутом 60 сек
            AppendLog($"[INFO] {LocalizationManager.Get("Log_WaitingForProcessExit")}");
            var startTime = Environment.TickCount64;

            while (_process != null && !_process.HasExited && (Environment.TickCount64 - startTime) < Constants.ServerStopTimeoutMs)
            {
                await Task.Delay(100);
            }

            if (_process is { HasExited: false })
            {
                AppendLog($"[WARN] {LocalizationManager.Get("Log_TimeoutForceKill")}");
                _process.Kill();
                AppendLog($"[INFO] {LocalizationManager.Get("Log_ProcessForceKilledAfterTimeout")}");
            }
            else
            {
                AppendLog($"[INFO] {LocalizationManager.Get("Log_ServerStoppedSuccessfully")}");

                // Ждём немного для обработки последних строк вывода
                await Task.Delay(Constants.ServerStatusCheckDelayMs);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog($"[INFO] {LocalizationManager.Get("Log_StopCancelled")}");
            // Принудительное завершение при отмене
            try
            {
                _process?.Kill();
            }
            catch (Exception killEx)
            {
                AppendLog($"[ERROR] {string.Format(LocalizationManager.Get("Log_KillFailedOnCancel"), killEx.Message)}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {LocalizationManager.Get("Log_StopError")} {ex.Message}");
            try
            {
                AppendLog($"[WARN] {LocalizationManager.Get("Log_ForceKillAttempt")}");
                _process?.Kill();
            }
            catch (Exception killEx)
            {
                AppendLog($"[ERROR] Не удалось завершить процесс: {killEx.Message}");
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
            AppendLog($"[CMD] {string.Format(LocalizationManager.Get("Log_CommandSent"), command)}");
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
    private static string FindServerJar(string path) => McServerInstaller.FindServerJar(path);

    /// <summary>
    /// Построить аргументы Java
    /// </summary>
    private string BuildJavaArgs(string jarFile, McServerInstaller.ServerLaunchType launchType) =>
        McServerInstaller.BuildLaunchArgs(jarFile, Server.Settings, launchType);

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        if (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            e.Data.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
            e.Data.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            _pendingErrorOutput ??= "";
            _pendingErrorOutput += e.Data + "\n";
        }

        AppendLog(e.Data);
        ParseOutput(e.Data);
    }

    private void OnError(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;

        var errorLine = $"[STDERR] {e.Data}";
        AppendLog(errorLine);
        _pendingErrorOutput ??= "";
        _pendingErrorOutput += e.Data + "\n";
    }

    private async Task MonitorProcessExitAsync()
    {
        try
        {
            if (_process == null) return;

            await _process.WaitForExitAsync();

            // Дочитываем оставшийся вывод после завершения процесса (неблокирующее чтение)
            await Task.Delay(200);

            // Проверка: процесс ещё существует
            if (_process == null) return;

            var exitCode = _process.ExitCode;

            // Статус уже установлен в GracefulStopAsync, обновляем только если процесс завершился сам
            if (Status != ServerStatus.Stopped && Status != ServerStatus.Error)
            {
                Status = ServerStatus.Stopped;
                OnStatusChanged?.Invoke(Status);
            }

            if (exitCode != 0)
            {
                AppendLog($"[ERROR] ═══════════════════════════════════════");
                AppendLog($"[ERROR] {LocalizationManager.Get("Log_ServerStoppedWithCode", exitCode)}");
                AppendLog($"[ERROR] {LocalizationManager.Get("Log_ServerConfigProblem")}");
                AppendLog($"[ERROR] {LocalizationManager.Get("Log_MemoryProblem")}");
                AppendLog($"[ERROR] ═══════════════════════════════════════");

                // Если сервер упал с ошибкой, устанавливаем статус Error
                if (exitCode != 0 && Status == ServerStatus.Stopped)
                {
                    Status = ServerStatus.Error;
                    OnStatusChanged?.Invoke(Status);
                }
            }
            else
            {
                AppendLog("[INFO] Сервер остановлен (код выхода: 0)");
            }

            _process?.Dispose();
            _process = null;

            // Авто-рестарт
            if (Server.Settings.AutoRestart)
            {
                AppendLog($"[INFO] {LocalizationManager.Get("Log_AutoRestart")} {Server.Settings.AutoRestartDelay} {LocalizationManager.Get("Log_Seconds")}...");
                await Task.Delay(Server.Settings.AutoRestartDelay * Constants.MsPerSecond);
                Start();
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("[INFO] Мониторинг процесса отменён");
            // Нормальное завершение при отмене
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Ошибка мониторинга процесса: {ex.Message}");
        }
    }

    internal void AppendLog(string line)
    {
        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var logLine = $"[{timestamp}] {line}";

            _logs.AppendLine(logLine);
            _logLines.Add(logLine);

            if (_logLines.Count > Constants.MaxLogLines)
            {
                _logLines.RemoveAt(0);
                // Ограничиваем размер StringBuilder (примерно 100KB)
                if (_logs.Length > 100_000)
                {
                    var fullText = _logs.ToString();
                    var startIndex = Math.Max(0, fullText.Length - 80_000);
                    _logs.Clear();
                    _logs.Append(fullText.Substring(startIndex));
                }
            }

            OnLog?.Invoke(logLine);
        }
    }

    /// <summary>
    /// Очистка старых логов сервера для избежания блокировки файлов
    /// </summary>
    private void ClearOldServerLogs()
    {
        try
        {
            var logsDir = Path.Combine(Server.Path, "logs");
            if (!Directory.Exists(logsDir))
                return;

            var latestLog = Path.Combine(logsDir, "latest.log");
            if (File.Exists(latestLog))
            {
                try
                {
                    File.Delete(latestLog);
                    AppendLog($"[INFO] {LocalizationManager.Get("Log_OldLogDeleted")}");
                }
                catch (IOException)
                {
                    var backupName = Path.Combine(logsDir, $"latest.log.old-{DateTime.Now:yyyyMMdd-HHmmss}");
                    File.Move(latestLog, backupName);
                    AppendLog(string.Format($"[INFO] {LocalizationManager.Get("Log_LogMoved")}", Path.GetFileName(backupName)));
                }
            }

            // Удаление архивов старше 7 дней
            foreach (var file in Directory.GetFiles(logsDir, "*.log.gz"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Игнорируем ошибки
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog(string.Format($"[WARN] {LocalizationManager.Get("Log_CleanupFailed")}", ex.Message));
        }
    }

    private void ParseOutput(string line)
    {
        // Проверка: сервер полностью загрузился (разные триггеры для разных серверов)
        if (!_serverReady)
        {
            // Vanilla/Forge/NeoForge
            if (line.Contains("Done ("))
            {
                _serverReady = true;
                AppendLog($"[INFO] {LocalizationManager.Get("Log_ServerStarted")}");
            }
            // Fabric/Quilt
            else if (line.Contains("Done!"))
            {
                _serverReady = true;
                AppendLog($"[INFO] {LocalizationManager.Get("Log_ServerStarted")}");
            }
            // Paper/Purpur
            else if (line.Contains("Done (") || line.Contains("For help, type"))
            {
                _serverReady = true;
                AppendLog($"[INFO] {LocalizationManager.Get("Log_ServerStarted")}");
            }
            // Universal fallback - "Starting minecraft server version"
            else if (line.Contains("Starting minecraft server version") || line.Contains("Loading properties"))
            {
                // Это раннее сообщение, не считаем готовым
            }
            // Дополнительно для NeoForge
            else if (line.Contains("NEOFORGE") && line.Contains("Loaded"))
            {
                _serverReady = true;
                AppendLog($"[INFO] {LocalizationManager.Get("Log_ServerStarted")}");
            }
        }

        var playersMatch = PlayersRegex().Match(line);
        if (playersMatch.Success)
        {
            var newPlayers = int.Parse(playersMatch.Groups[1].Value);
            if (newPlayers != _playersOnline)
            {
                _playersOnline = newPlayers;
                OnPlayersChanged?.Invoke(_playersOnline);
            }
        }
    }

    [GeneratedRegex(@"There are (\d+) players? of a max of \d+")]
    private static partial Regex PlayersRegex();

    /// <summary>
    /// Получить путь к Java для сервера
    /// </summary>
    private string GetJavaPathForServer()
    {
        var config = configService?.GetConfig();

        if (config == null)
        {
            AppendLog($"[WARN] Конфигурация недоступна, используем Java из PATH");
            return "java";
        }

        // Если указан конкретный Java ID для сервера
        if (!string.IsNullOrEmpty(Server.Settings.JavaId))
        {
            var java = config.JavaInstallations.FirstOrDefault(j => j.Id == Server.Settings.JavaId);
            if (java != null)
            {
                if (java.Exists)
                {
                    AppendLog($"[INFO] Используем Java из настроек сервера: {java.DisplayName}");
                    return java.Path;
                }
                AppendLog($"[WARN] {LocalizationManager.Get("Log_JavaFromSettingsNotFound", java.Path)}");
            }
        }

        // Автовыбор Java по версии Minecraft
        if (Server.Settings.JavaAutoSelect)
        {
            var requiredJavaVersion = GetRequiredJavaVersion(Server.McVersion, McServerInstaller.GetServerLaunchType(Server.Path));
            AppendLog($"[INFO] Автовыбор Java: требуется Java {requiredJavaVersion}+ для Minecraft {Server.McVersion}");

            // Ищем подходящую Java среди установленных
            var suitableJava = config.JavaInstallations
                .Where(j => j.Exists && j.MajorVersion >= requiredJavaVersion)
                .OrderByDescending(j => j.MajorVersion)
                .FirstOrDefault();

            if (suitableJava != null)
            {
                AppendLog($"[INFO] Выбрана Java: {suitableJava.DisplayName}");
                return suitableJava.Path;
            }

            AppendLog($"[WARN] {LocalizationManager.Get("Log_JavaVersionNotFound_TryDefault", requiredJavaVersion)}");
        }

        // Java по умолчанию
        var defaultJava = config.GetDefaultJava();
        if (defaultJava != null)
        {
            if (defaultJava.Exists)
            {
                AppendLog($"[INFO] Используем Java: {defaultJava.DisplayName}");
                return defaultJava.Path;
            }
            AppendLog($"[WARN] {LocalizationManager.Get("Log_JavaDefaultNotFound", defaultJava.Path)}");
        }

        AppendLog($"[INFO] Используем Java из PATH: java");
        return "java";
    }

    /// <summary>
    /// Освободить ресурсы
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

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
