using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Wpf.Ui.Controls;

namespace Konserva.Services;

/// <summary>
/// Java checking, path lookup, version validation, process start
/// </summary>
public partial class McServerProcess
{
    // Cache of module-path (-p) support per Java installation — check once and remember
    private static readonly ConcurrentDictionary<string, bool> _modulePathSupportCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether the specified Java supports the <c>-p</c> option (module path).
    /// The result is cached.
    /// </summary>
    private static bool SupportsModulePath(string javaPath)
    {
        if (_modulePathSupportCache.TryGetValue(javaPath, out var cached))
            return cached;

        // javaw.exe does not support stdout/stderr redirection —
        // use java.exe for the check
        var checkPath = javaPath;
        if (checkPath.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(checkPath);
            if (dir != null)
            {
                var javaExe = Path.Combine(dir, "java.exe");
                if (File.Exists(javaExe))
                    checkPath = javaExe;
            }
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = checkPath,
                Arguments = "-p . -version",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _modulePathSupportCache[javaPath] = false;
                return false;
            }

            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            // The Java 8 launcher will reply with "Unrecognized option: -p"
            var supports = !error.Contains("Unrecognized option", StringComparison.OrdinalIgnoreCase)
                        && !error.Contains("Error", StringComparison.OrdinalIgnoreCase);

            _modulePathSupportCache[javaPath] = supports;
            Logger.Info($"[SupportsModulePath] {javaPath} → {(supports ? "да" : "нет")}", "McServerProcess");
            return supports;
        }
        catch
        {
            _modulePathSupportCache[javaPath] = false;
            return false;
        }
    }

    /// <summary>
    /// Checks whether Java is a Java 8 build with update >= 400.
    /// Starting with Java 8u400, the ManifestEntryVerifier(Manifest) constructor was removed,
    /// which causes a NoSuchMethodError when launching Forge (ModLauncher SecureJarHandler).
    /// </summary>
    private static bool IsBrokenJava8(JavaInstallation java)
    {
        if (java.MajorVersion != 8)
            return false;

        var version = java.Version;
        if (string.IsNullOrEmpty(version))
            return false;

        // "1.8.0_502" format — extract the update number (the number after _)
        if (version.StartsWith("1.8."))
        {
            var parts = version.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[^1], out var update) && update >= 400)
                return true;
        }

        // "8.0.4020.8" format used by some builds
        if (version.StartsWith("8."))
        {
            var segments = version.Split('.');
            if (segments.Length >= 3 && int.TryParse(segments[2], out var third) && third >= 400)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Get the Java path for the server
    /// </summary>
    private string GetJavaPathForServer()
    {
        var config = configService?.GetConfig();

        if (config == null)
        {
            AppendLog($"[WARN] {LocalizationManager.Get("Log_ConfigUnavailable")}");
            return "java";
        }

        if (!string.IsNullOrEmpty(Server.Settings.JavaId))
        {
            var java = config.JavaInstallations.FirstOrDefault(j => j.Id == Server.Settings.JavaId);
            if (java != null)
            {
                if (java.Exists)
                {
                    _lastJavaDisplayName = java.DisplayName;
                    return java.Path;
                }
                AppendLog($"[WARN] {LocalizationManager.Get("Log_JavaFromSettingsNotFound", java.Path)}");
            }
        }

        if (Server.Settings.JavaAutoSelect)
        {
            var launchType = GetInstaller().GetServerLaunchType(Server.Path);
            var requiredJavaVersion = GetRequiredJavaVersion(Server.McVersion, launchType);
            Logger.Info($"[GetJavaPathForServer] Auto-select: required Java {requiredJavaVersion}+, launch type {launchType}", "McServerProcess");

            // NeoForge uses module-path (-p) — filter out Java installations that do not support it.
            // Forge uses -jar, no modules needed.
            bool needsModulePath = launchType is ServerLaunchType.NeoForge;

            var maxJavaVersion = GetMaxJavaVersion(Server.McVersion, launchType);

            // Forge/NeoForge: exact match only (as in FluentLauncher).
            // The Forge modlauncher is built for a specific Java version — a mismatch causes NoSuchMethodError / IllegalAccessError.
            bool isForgeOrNeoForge = launchType == ServerLaunchType.Forge || launchType == ServerLaunchType.NeoForge;

            var exactMatch = config.JavaInstallations
                .Where(j => j.Exists && j.MajorVersion == requiredJavaVersion && !IsBrokenJava8(j))
                .OrderByDescending(j => j.MajorVersion)
                .FirstOrDefault(j => (!needsModulePath || SupportsModulePath(j.Path))
                                  && (maxJavaVersion <= 0 || j.MajorVersion <= maxJavaVersion));

            if (exactMatch != null)
            {
                Logger.Info($"[GetJavaPathForServer] Found exact match: {exactMatch.DisplayName}", "McServerProcess");
                _lastJavaDisplayName = exactMatch.DisplayName;
                return exactMatch.Path;
            }

            // Forge/NeoForge: if no exact match is found — try a newer version
            if (isForgeOrNeoForge)
            {
                // Check whether there are Java 8 builds with update >= 400 that we filtered out
                var hasBrokenJava8 = config.JavaInstallations
                    .Any(j => j.Exists && j.MajorVersion == requiredJavaVersion && IsBrokenJava8(j));

                if (hasBrokenJava8)
                {
                    var msg = LocalizationManager.Get("Log_Java8UpdateTooNew");
                    AppendLog($"[ERROR] {msg}");
                    (Application.Current.MainWindow as MainWindow)?.ShowSnackbar(
                        LocalizationManager.Get("Snackbar_Java8Broken_Title"),
                        string.Format(LocalizationManager.Get("Snackbar_Java8Broken_Message"), Server.McVersion),
                        ControlAppearance.Danger, 12);
                    throw new InvalidOperationException(msg);
                }

                var newerMatch = config.JavaInstallations
                    .Where(j => j.Exists && j.MajorVersion > requiredJavaVersion && !IsBrokenJava8(j))
                    .OrderBy(j => j.MajorVersion)
                    .FirstOrDefault(j => (!needsModulePath || SupportsModulePath(j.Path))
                                      && (maxJavaVersion <= 0 || j.MajorVersion <= maxJavaVersion));

                if (newerMatch != null)
                {
                    Logger.Info($"[GetJavaPathForServer] Forge/NeoForge fallback to newer Java {newerMatch.MajorVersion} (required {requiredJavaVersion})", "McServerProcess");
                    _lastJavaDisplayName = newerMatch.DisplayName;
                    return newerMatch.Path;
                }

                AppendLog($"[WARN] {LocalizationManager.Get("Log_JavaVersionNotFound_TryDefault", requiredJavaVersion)}");
            }
            else
            {
                var suitableJava = config.JavaInstallations
                    .Where(j => j.Exists && j.MajorVersion > requiredJavaVersion)
                    .OrderBy(j => j.MajorVersion)
                    .FirstOrDefault(j => (!needsModulePath || SupportsModulePath(j.Path))
                                      && (maxJavaVersion <= 0 || j.MajorVersion <= maxJavaVersion));

                if (suitableJava != null)
                {
                    Logger.Info($"[GetJavaPathForServer] Using newer Java {suitableJava.MajorVersion} (required {requiredJavaVersion})", "McServerProcess");
                    _lastJavaDisplayName = suitableJava.DisplayName;

                    if (suitableJava.MajorVersion - requiredJavaVersion > 4)
                    {
                        var warnMsg = string.Format(
                            LocalizationManager.Get("Log_JavaTooNewWarning"),
                            suitableJava.DisplayName,
                            suitableJava.MajorVersion,
                            requiredJavaVersion);
                        AppendLog($"[WARN] {warnMsg}");
                    }

                    return suitableJava.Path;
                }

                // If nothing fits — try without the module-path filter (the user may have selected it manually)
                AppendLog($"[WARN] {LocalizationManager.Get("Log_JavaVersionNotFound_TryDefault", requiredJavaVersion)}");
            }
        }

        var defaultJava = config.GetDefaultJava();
        if (defaultJava != null)
        {
            if (defaultJava.Exists)
            {
                _lastJavaDisplayName = defaultJava.DisplayName;
                return defaultJava.Path;
            }
            AppendLog($"[WARN] {LocalizationManager.Get("Log_JavaDefaultNotFound", defaultJava.Path)}");
        }

        _lastJavaDisplayName = "PATH";
        return "java";
    }

    /// <summary>
    /// Get the minimum required Java version for a Minecraft version
    /// </summary>
    private static int GetRequiredJavaVersion(string mcVersion, ServerLaunchType launchType) =>
        JavaVersionParser.GetRequiredJavaVersion(mcVersion, launchType);

    /// <summary>
    /// The maximum Java version compatible with a given Minecraft version + mod loader.
    /// 0 = no limit.
    /// Forge before 1.17 breaks on Java 17+ (the module system does not export sun.security.util).
    /// </summary>
    private static int GetMaxJavaVersion(string mcVersion, ServerLaunchType launchType)
    {
        if (launchType is ServerLaunchType.Forge or ServerLaunchType.NeoForge or ServerLaunchType.Fabric or ServerLaunchType.Quilt or ServerLaunchType.Standard)
        {
            var parts = mcVersion.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor))
            {
                if (major == 1)
                {
                    // MC 1.16.x and below — the Forge modlauncher is incompatible with Java 17+
                    // Java 11 also lacks ManifestEntryVerifier(Manifest) — Java 8 < 8u400 is required
                    if (minor <= 16) return 8;

                    // MC 1.17 uses Java 16, maximum 17
                    if (minor == 17) return 17;
                }
            }
        }
        return 0; // no limit
    }

    /// <summary>
    /// Java check result
    /// </summary>
    private sealed class JavaCheckResult
    {
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public int MajorVersion { get; set; }
        public string JavaPath { get; set; } = string.Empty;
        public bool? SupportsModulePath { get; set; }
    }

    /// <summary>
    /// Java availability check with version detection
    /// </summary>
    private static JavaCheckResult CheckJava(string javaPath)
    {
        try
        {
            string actualJavaPath = javaPath;
            string versionOutput = string.Empty;

            if (javaPath == "java")
            {
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
                if (!process.WaitForExit(Constants.JavaCheckTimeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return new JavaCheckResult { Success = false, Error = "Проверка Java зависла (таймаут)" };
                }

                if (process.ExitCode != 0)
                    return new JavaCheckResult { Success = false, Error = $"java вернула код ошибки {process.ExitCode}" };

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
                if (!process.WaitForExit(Constants.JavaCheckTimeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return new JavaCheckResult { Success = false, Error = "Проверка Java зависла (таймаут)" };
                }

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
                JavaPath = actualJavaPath,
                SupportsModulePath = SupportsModulePath(actualJavaPath)
            };
        }
        catch (Exception ex)
        {
            Logger.Warning($"Java check failed: {ex.Message}", "McServerProcess");
            return new JavaCheckResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// eula.txt presence check
    /// </summary>
    private void ValidateEula()
    {
        var eulaPath = Path.Combine(Server.Path, "eula.txt");
        if (!File.Exists(eulaPath))
            throw new FileNotFoundException("Файл eula.txt не найден. Сервер не был установлен корректно.");

        var eulaContent = File.ReadAllText(eulaPath, Encoding.UTF8);
        if (!eulaContent.Contains("eula=true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("EULA не принята! Измените eula.txt и установите eula=true");

        AppendLog($" {LocalizationManager.Get("Log_EulaAccepted")}");
    }

    /// <summary>
    /// Java version compatibility check
    /// </summary>
    private void ValidateJavaVersion(JavaCheckResult javaCheckResult)
    {
        var launchType = GetInstaller().GetServerLaunchType(Server.Path);
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
    }

    /// <summary>
    /// Logging the Java version error
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
    /// Logging the error - Java not found
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
    /// Process start
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
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
            CreateNewProcessGroup = true
        };

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!_process.Start())
            throw new InvalidOperationException($"Не удалось запустить процесс: {javaPath} {utf8Args}");

        _process.OutputDataReceived += OnOutput;
        _process.ErrorDataReceived += OnError;

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        AppendLog($" {string.Format(LocalizationManager.Get("Log_ProcessStarted"), _process.Id)}");
    }

    /// <summary>
    /// Start failure handling
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
        AppendLog($" {message}");
    }

    private string BuildJavaArgs(string jarFile, ServerLaunchType launchType, int javaMajorVersion) =>
        GetInstaller().BuildLaunchArgs(jarFile, Server.Settings, launchType, javaMajorVersion, Server.Path);
}
