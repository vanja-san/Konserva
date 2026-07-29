using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Models;
using Konserva.Utilities;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Konserva.Services;

/// <summary>
/// Сервис управления Java установками
/// </summary>
public class JavaManagementService(IConfigService configService) : IJavaManagementService
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle hFile, [Out] StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);
    /// <summary>
    /// Проверяет является ли ошибка ошибкой Java-совместимости и показывает snackbar.
    /// Общий метод для ServersPage, ServerDetailPage и MainWindow.
    /// </summary>
    public static void HandleServerStartError(Server server, string errorMessage, IConfigService? configService = null)
    {
        var requiredVersion = JavaVersionParser.ParseRequiredJavaVersion(errorMessage);
        var foundVersion = JavaVersionParser.ParseFoundJavaVersion(errorMessage);

        // Считаем ошибкой Java-совместимости, если удалось извлечь требуемую версию,
        // сообщение содержит явный паттерн несовместимости,
        // или это Java-краш без явного указания версии (например, InvocationTargetException)
        bool isJavaVersionError = requiredVersion > 0 ||
                                  errorMessage.Contains("Требуется Java", StringComparison.OrdinalIgnoreCase) ||
                                  errorMessage.Contains("class file version", StringComparison.OrdinalIgnoreCase) ||
                                  errorMessage.Contains("Unsupported class file major version", StringComparison.OrdinalIgnoreCase) ||
                                  IsJavaCrashError(errorMessage);

        if (isJavaVersionError)
        {
            // Если не удалось извлечь версию из сообщения — вычисляем требуемую по версии Minecraft
            if (requiredVersion <= 0)
            {
                requiredVersion = JavaVersionParser.GetRequiredJavaVersion(
                    server.McVersion,
                    GetLaunchType(server.ModLoader.Type));
            }

            // Forge modloader: NoSuchMethodError на ManifestEntryVerifier —
            // свежие сборки Java 8 (≥ 8u400) удалили конструктор ManifestEntryVerifier(Manifest).
            // Нужна старая сборка: Zulu JRE 8u302b08 или аналогичная.
            if (errorMessage.Contains("NoSuchMethodError", StringComparison.OrdinalIgnoreCase) &&
                errorMessage.Contains("ManifestEntryVerifier", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning("═══════════════════════════════════════════════════════════════", "JavaManagementService");
                Logger.Warning("Forge modloader requires ManifestEntryVerifier(Manifest) constructor", "JavaManagementService");
                Logger.Warning("which was removed in Java 8 builds >= 8u400 (Temurin 8u492, Zulu 8u502, etc.)", "JavaManagementService");
                Logger.Warning("Install Zulu JRE 8u302b08 or older Java 8 build.", "JavaManagementService");
                Logger.Warning("═══════════════════════════════════════════════════════════════", "JavaManagementService");
            }

            // Ошибка от нашего собственного пре-стартового детекта сломанной Java 8 (≥ 8u400):
            // снекбар уже был показан в GetJavaPathForServer, второй показывать не нужно.
            if (errorMessage.Contains("8u400", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info("[HandleServerStartError] Broken Java 8 (≥ 8u400) snackbar already shown, skipping generic error.", "JavaManagementService");
                return;
            }

            // Получаем все установленные Java
            var cfg = configService ?? Ioc.Default.GetService<IConfigService>()!;
            var allJava = cfg?.GetConfig().JavaInstallations.Where(j => j.Exists).ToList();

            var mainWindow = Ioc.Default.GetService<MainWindow>();
            mainWindow?.Dispatcher.Invoke(() =>
            {
                mainWindow?.ShowJavaErrorSnackbar(server, errorMessage, requiredVersion, foundVersion, allJava);
            });
        }
        else
        {
            // Не Java-ошибка (или неизвестный формат) — делегируем стандартному обработчику
            _ = UiHelper.ShowError(errorMessage);
        }
    }

    /// <summary>
    /// Определяет, является ли сообщение об ошибке Java-крашем, даже если в нём нет
    /// явного упоминания требуемой версии (например, <see cref="System.TypeInitializationException"/>,
    /// <see cref="System.Reflection.TargetInvocationException"/>).
    /// Используется только как fallback для <see cref="HandleServerStartError"/> — если
    /// не удалось извлечь версию из текста, но это явно Java-краш, то считаем ошибкой
    /// Java-совместимости, чтобы показать информативный snackbar со списком Java.
    /// </summary>
    private static bool IsJavaCrashError(string message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        // Паттерны, которые точно указывают на внутреннюю ошибку Java/JVM,
        // а не на problems с classpath, jar-файлами или аргументами.
        // "Could not find or load main class", "Unable to access jarfile" и т.п.
        // НЕ включены — они могут быть вызваны разными причинами (не только версией Java),
        // и для них нужно показывать исходный текст ошибки, а не snackbar о несовместимости.
        var javaCrashPatterns = new[]
        {
            "InvocationTargetException",
            "Exception in thread",
            "at java.base/",
            "java.lang.reflect",
            "java.lang.UnsupportedClassVersionError",
        };

        // Forge modlauncher: NoSuchMethodError на ManifestEntryVerifier —
        // свежие сборки Java 8 (≥ 8u400) удалили конструктор, нужен Zulu 8u302b08 или older
        if (message.Contains("NoSuchMethodError", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("ManifestEntryVerifier", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return javaCrashPatterns.Any(p => message.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Преобразует ModLoaderType в ServerLaunchType для вычисления требуемой версии Java.
    /// </summary>
    private static ServerLaunchType GetLaunchType(ModLoaderType modLoaderType) => modLoaderType switch
    {
        ModLoaderType.Fabric => ServerLaunchType.Fabric,
        ModLoaderType.Quilt => ServerLaunchType.Quilt,
        ModLoaderType.Forge => ServerLaunchType.Forge,
        ModLoaderType.NeoForge => ServerLaunchType.NeoForge,
        _ => ServerLaunchType.Standard, // Vanilla, Paper
    };

    /// <summary>
    /// Поиск установленных Java на компьютере.
    /// Только PATH (where java) + реестр Windows — без сканирования файловой системы.
    /// </summary>
    public List<JavaInstallation> FindInstalledJava()
    {
        var javaInstallations = new List<JavaInstallation>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Все java.exe из PATH (покрывает Scoop, Chocolatey, ручные установки)
        foreach (var java in FindAllJavaInPath())
        {
            if (seenPaths.Add(ResolveRealPath(java.Path)))
                javaInstallations.Add(java);
        }

        // 2. Из реестра Windows (64-bit и 32-bit view)
        foreach (var path in FindJavaInRegistry())
        {
            if (seenPaths.Add(ResolveRealPath(path)))
            {
                var java = GetJavaInfo(path);
                if (java != null)
                    javaInstallations.Add(java);
            }
        }

        Logger.Info($"Found {javaInstallations.Count} Java installations", "JavaManagementService");
        return [.. javaInstallations.OrderByDescending(j => j.MajorVersion)];
    }

    /// <summary>
    /// Поиск всех Java в PATH через реестр (всегда свежий, без перезапуска).
    /// </summary>
    private List<JavaInstallation> FindAllJavaInPath()
    {
        var results = new List<JavaInstallation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var pathDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Системный PATH (HKLM)
            var systemPath = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
                "Path", "") as string;
            if (!string.IsNullOrEmpty(systemPath))
            {
                foreach (var dir in systemPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!string.IsNullOrEmpty(dir))
                        pathDirs.Add(Environment.ExpandEnvironmentVariables(dir));
            }

            // Пользовательский PATH (HKCU)
            var userPath = Registry.GetValue(
                @"HKEY_CURRENT_USER\Environment",
                "Path", "") as string;
            if (!string.IsNullOrEmpty(userPath))
            {
                foreach (var dir in userPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!string.IsNullOrEmpty(dir))
                        pathDirs.Add(Environment.ExpandEnvironmentVariables(dir));
            }

            // Ищем javaw.exe (предпочтительно) или java.exe в каждой директории PATH.
            // Если есть javaw.exe — java.exe в той же папке не проверяем (дубликат).
            foreach (var dir in pathDirs)
            {
                if (!Directory.Exists(dir)) continue;

                var javawPath = Path.Combine(dir, "javaw.exe");
                if (File.Exists(javawPath) && seen.Add(javawPath))
                {
                    var java = GetJavaInfo(javawPath);
                    if (java != null) results.Add(java);
                    continue;
                }

                var javaExePath = Path.Combine(dir, "java.exe");
                if (File.Exists(javaExePath) && seen.Add(javaExePath))
                {
                    var java = GetJavaInfo(javaExePath);
                    if (java != null) results.Add(java);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to find Java in PATH: {ex.Message}", "JavaManagementService");
        }

        return results;
    }

    /// <summary>
    /// Стандартные пути к Java
    /// </summary>
    /// <summary>
    /// Поиск Java в реестре Windows.
    /// Проверяет 64-bit и 32-bit (Wow6432Node) view для каждого ключа.
    /// </summary>
    private static List<string> FindJavaInRegistry()
    {
        var paths = new List<string>();
        var seenRealPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // (registryHive, subKey) пары для поиска JavaHome
        var registryRoots = new[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\JavaSoft"),
            (RegistryHive.LocalMachine, @"SOFTWARE\IBM\Java"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Azul Zulu"),
            (RegistryHive.CurrentUser,  @"SOFTWARE\JavaSoft"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\JDK"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Amazon Corretto"),
        };

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var (hive, keyPath) in registryRoots)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(keyPath);
                    if (key == null) continue;

                    CollectJavaHomes(key, paths, seenRealPaths);
                }
                catch
                {
                    // Hive недоступен — пропускаем
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Рекурсивно собирает все JavaHome из указанной ветки реестра
    /// </summary>
    private static void CollectJavaHomes(RegistryKey key, List<string> paths, HashSet<string> seenRealPaths)
    {
        try
        {
            // Пробуем получить JavaHome в текущем ключе
            var javaHome = key.GetValue("JavaHome") as string;
            if (!string.IsNullOrEmpty(javaHome))
            {
                var javaPath = FindJavaExeInBin(Path.Combine(javaHome, "bin"));
                if (javaPath != null && seenRealPaths.Add(ResolveRealPath(javaPath)))
                    paths.Add(javaPath);
            }

            // Рекурсивно обходим подразделы
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey != null)
                        CollectJavaHomes(subKey, paths, seenRealPaths);
                }
                catch
                {
                    // Подраздел недоступен — пропускаем
                }
            }
        }
        catch
        {
            // Ключ недоступен — пропускаем
        }
    }



    /// <summary>
    /// Получение информации о Java
    /// </summary>
    public JavaInstallation? GetJavaInfo(string javaPath)
    {
        try
        {
            // Проверяем существование файла (с .exe или без)
            if (!File.Exists(javaPath) && !File.Exists(javaPath + ".exe"))
            {
                Logger.Warning($"Java file not found: {javaPath}", "JavaManagementService");
                return null;
            }

            var actualPath = File.Exists(javaPath) ? javaPath : javaPath + ".exe";

            // Предпочитаем javaw.exe (без консольного окна) для запуска сервера
            actualPath = ResolvePreferredJavaPath(actualPath);

            Logger.Info($"Checking Java: {actualPath}", "JavaManagementService");

            // Для javaw.exe используем java.exe для получения версии
            var versionCheckPath = actualPath;
            if (actualPath.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
            {
                var javaExePath = Path.Combine(Path.GetDirectoryName(actualPath)!, "java.exe");
                if (File.Exists(javaExePath))
                {
                    versionCheckPath = javaExePath;
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = versionCheckPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Logger.Warning($"Failed to start Java process: {versionCheckPath}", "JavaManagementService");
                return null;
            }

            var versionOutput = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(Constants.JavaCheckTimeoutMs))
            {
                try { process.Kill(); } catch { /* ignore */ }
                Logger.Warning("Java version check timed out, process killed", "JavaManagementService");
                return null;
            }

            if (process.ExitCode != 0)
            {
                Logger.Warning($"Java returned exit code {process.ExitCode}", "JavaManagementService");
                return null;
            }

            Logger.Info($"Java version output: {versionOutput.Trim()}", "JavaManagementService");

            // Парсим версию
            var version = JavaVersionParser.ParseVersion(versionOutput);
            var majorVersion = JavaVersionParser.ParseMajorVersion(versionOutput);

            // Получаем имя
            var name = GetJavaName(actualPath, majorVersion);
            Logger.Info($"Found Java {majorVersion} ({version}) - {name}", "JavaManagementService");

            return new JavaInstallation
            {
                Path = actualPath,
                Version = version,
                MajorVersion = majorVersion,
                Name = name,
                IsDefault = false
            };
        }
        catch (Exception ex)
        {
            Logger.Error($"Error getting Java info: {ex.Message}", ex, "JavaManagementService");
            return null;
        }
    }

    /// <summary>
    /// Получение имени Java по пути
    /// </summary>
    private static string GetJavaName(string path, int majorVersion)
    {
        if (path.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            return "Microsoft Java";
        if (path.Contains("Adoptium", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("Eclipse", StringComparison.OrdinalIgnoreCase))
            return "Eclipse Adoptium";
        if (path.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
            return "Oracle Java";
        if (path.Contains("Amazon", StringComparison.OrdinalIgnoreCase))
            return "Amazon Corretto";
        if (path.Contains("JetBrains", StringComparison.OrdinalIgnoreCase))
            return "JetBrains Runtime";

        return $"Java {majorVersion}";
    }

    /// <summary>
    /// Если рядом с java.exe есть javaw.exe — возвращает путь к javaw.exe.
    /// Иначе возвращает исходный путь.
    /// </summary>
    private static string ResolvePreferredJavaPath(string javaPath)
    {
        if (string.IsNullOrEmpty(javaPath))
            return javaPath;

        if (javaPath.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
            return javaPath;

        var dir = Path.GetDirectoryName(javaPath);
        if (!string.IsNullOrEmpty(dir))
        {
            var javawPath = Path.Combine(dir, "javaw.exe");
            if (File.Exists(javawPath))
                return javawPath;
        }

        return javaPath;
    }

    /// <summary>
    /// Ищет java.exe или javaw.exe в указанной bin-папке.
    /// Возвращает null, если ни один не найден.
    /// </summary>
    private static string? FindJavaExeInBin(string binDir)
    {
        var javawPath = Path.Combine(binDir, "javaw.exe");
        if (File.Exists(javawPath))
            return javawPath;
        var javaPath = Path.Combine(binDir, "java.exe");
        if (File.Exists(javaPath))
            return javaPath;
        return null;
    }

    /// <summary>
    /// Разрешает все junction/symlink в пути через Win32 GetFinalPathNameByHandle.
    /// Надёжно работает для Scoop current → version и любых других перенаправлений.
    /// </summary>
    private static string ResolveRealPath(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.None);
            var sb = new StringBuilder(1024);
            var len = GetFinalPathNameByHandle(handle, sb, (uint)sb.Capacity, 0);
            if (len > 0 && len < sb.Capacity)
            {
                var result = sb.ToString();
                if (result.StartsWith(@"\\?\"))
                    result = result.Substring(4);
                return result;
            }
        }
        catch
        {
            // не удалось открыть файл — fallback на обычную нормализацию
        }

        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    /// <summary>
    /// Добавление Java в конфигурацию
    /// </summary>
    public JavaInstallation? AddJava(string javaPath)
    {
        var java = GetJavaInfo(javaPath);
        if (java == null)
            return null;

        var config = configService.GetConfig();

        // Проверка на дубликат (case-insensitive для Windows)
        if (config.JavaInstallations.Any(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase)))
            return config.JavaInstallations.First(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase));

        // Если это первая Java, делаем её стандартной
        if (config.JavaInstallations.Count == 0)
            java.IsDefault = true;

        config.JavaInstallations.Add(java);

        if (java.IsDefault)
            config.DefaultJavaId = java.Id;

        configService.SaveConfig(config);
        return java;
    }

    /// <summary>
    /// Удаление Java из конфигурации
    /// </summary>
    public bool RemoveJava(string javaId)
    {
        var config = configService.GetConfig();
        var java = config.JavaInstallations.FirstOrDefault(j => j.Id == javaId);
        if (java == null)
            return false;

        config.JavaInstallations.Remove(java);

        // Если удалили Java по умолчанию, выбираем другую
        if (config.DefaultJavaId == javaId)
        {
            var newDefault = config.JavaInstallations.FirstOrDefault();
            config.DefaultJavaId = newDefault?.Id;
            newDefault?.IsDefault = true;
        }

        configService.SaveConfig(config);
        return true;
    }

    /// <summary>
    /// Установка Java по умолчанию
    /// </summary>
    public bool SetDefaultJava(string javaId)
    {
        var config = configService.GetConfig();
        var java = config.JavaInstallations.FirstOrDefault(j => j.Id == javaId);
        if (java == null)
            return false;

        // Обновляем IsDefault у всех
        foreach (var j in config.JavaInstallations)
        {
            j.IsDefault = j.Id == javaId;
        }

        config.DefaultJavaId = javaId;
        configService.SaveConfig(config);

        Logger.Info($"Set default Java: {java.DisplayName}", "JavaManagementService");
        return true;
    }

    /// <summary>
    /// Scans the system for all installed Java runtimes.
    /// Removes config entries whose Java file no longer exists.
    /// Updates existing entries to use javaw.exe when available.
    /// Adds new installations not yet in the configuration.
    /// </summary>
    public List<JavaInstallation> ScanAndAddJava()
    {
        var config = configService.GetConfig();
        var foundJava = FindInstalledJava();
        var changed = false;

        // 1. Clean up missing entries and update paths to javaw.exe
        var toRemove = new List<string>();
        foreach (var existing in config.JavaInstallations)
        {
            if (!existing.Exists)
            {
                toRemove.Add(existing.Id);
                continue;
            }

            var preferredPath = ResolvePreferredJavaPath(existing.Path);
            if (!string.Equals(existing.Path, preferredPath, StringComparison.OrdinalIgnoreCase))
            {
                var updated = GetJavaInfo(preferredPath);
                if (updated != null)
                {
                    existing.Path = updated.Path;
                    existing.Version = updated.Version;
                    existing.MajorVersion = updated.MajorVersion;
                    existing.Name = updated.Name;
                    changed = true;
                }
            }
        }

        foreach (var id in toRemove)
        {
            config.JavaInstallations.Remove(config.JavaInstallations.First(j => j.Id == id));
            if (config.DefaultJavaId == id)
            {
                var newDefault = config.JavaInstallations.FirstOrDefault();
                config.DefaultJavaId = newDefault?.Id;
                if (newDefault != null) newDefault.IsDefault = true;
            }
            changed = true;
        }

        // 2. Add new installations (paths are already resolved to javaw.exe by GetJavaInfo)
        foreach (var java in foundJava)
        {
            if (config.JavaInstallations.Any(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (config.JavaInstallations.Count == 0)
                java.IsDefault = true;

            config.JavaInstallations.Add(java);
            changed = true;
        }

        if (changed)
        {
            var newDefault = config.JavaInstallations.FirstOrDefault(j => j.IsDefault);
            if (newDefault != null)
                config.DefaultJavaId = newDefault.Id;

            configService.SaveConfig(config);
        }

        Logger.Info($"ScanAndAddJava: found {foundJava.Count} total, config has {config.JavaInstallations.Count} after cleanup", "JavaManagementService");
        return [.. config.JavaInstallations];
    }

    /// <summary>
    /// Получение совместимой Java версии для сервера
    /// </summary>
    public async Task<JavaInstallation?> GetCompatibleJavaAsync(
        string mcVersion,
        IServerInstaller installer,
        string serverPath,
        CancellationToken ct = default)
    {
        var config = configService.GetConfig();
        var launchType = installer.GetServerLaunchType(serverPath);
        var requiredVersion = GetRequiredJavaVersion(mcVersion, launchType);

        // Ищем Java с подходящей версией
        var compatibleJava = config.JavaInstallations
            .FirstOrDefault(j => j.MajorVersion >= requiredVersion && j.Exists);

        if (compatibleJava != null)
        {
            Logger.Info($"Found compatible Java: {compatibleJava.DisplayName} (required {requiredVersion}+)", "JavaManagementService");
            return compatibleJava;
        }

        // Fallback на любую установленную Java
        var anyJava = config.JavaInstallations.FirstOrDefault(j => j.Exists);
        if (anyJava != null)
        {
            Logger.Warning($"No compatible Java found (required {requiredVersion}+), using {anyJava.DisplayName}", "JavaManagementService");
            return anyJava;
        }

        Logger.Error($"No Java installations found", category: "JavaManagementService");
        return null;
    }

    /// <summary>
    /// Получение требуемой версии Java
    /// </summary>
    private static int GetRequiredJavaVersion(string mcVersion, ServerLaunchType launchType)
    {
        return JavaVersionParser.GetRequiredJavaVersion(mcVersion, launchType);
    }
}
