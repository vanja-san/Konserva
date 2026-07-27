using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Models;
using Konserva.Utilities;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace Konserva.Services;

/// <summary>
/// Сервис управления Java установками
/// </summary>
public class JavaManagementService(IConfigService configService) : IJavaManagementService
{
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
    /// Поиск установленных Java на компьютере
    /// </summary>
    public List<JavaInstallation> FindInstalledJava()
    {
        var javaInstallations = new List<JavaInstallation>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Все java.exe из PATH (покрывает Scoop, Chocolatey, ручные установки)
        foreach (var java in FindAllJavaInPath())
        {
            if (seenPaths.Add(java.Path))
                javaInstallations.Add(java);
        }

        // 2. Из реестра Windows
        foreach (var path in FindJavaInRegistry())
        {
            if (seenPaths.Add(path))
            {
                var java = GetJavaInfo(path);
                if (java != null)
                    javaInstallations.Add(java);
            }
        }

        // 3. Рекурсивный поиск в Program Files, Program Files (x86), LocalAppData
        foreach (var path in FindJavaInProgramFiles())
        {
            if (seenPaths.Add(path))
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
    /// Поиск всех Java в PATH (where выводит все совпадения)
    /// </summary>
    private List<JavaInstallation> FindAllJavaInPath()
    {
        var results = new List<JavaInstallation>();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "java",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return results;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(Constants.JavaPathCheckTimeoutMs);

            var paths = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                              .Select(p => p.Trim())
                              .Where(p => !string.IsNullOrEmpty(p));

            foreach (var javaPath in paths)
            {
                var java = GetJavaInfo(javaPath);
                if (java != null)
                    results.Add(java);
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
    /// Поиск Java в реестре Windows через прямой RegistryKey API (вместо PowerShell)
    /// </summary>
    private static List<string> FindJavaInRegistry()
    {
        var paths = new List<string>();

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

        foreach (var (hive, keyPath) in registryRoots)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = baseKey.OpenSubKey(keyPath);
                if (key == null) continue;

                CollectJavaHomes(key, paths);
            }
            catch
            {
                // Hive недоступен — пропускаем
            }
        }

        return paths;
    }

    /// <summary>
    /// Рекурсивно собирает все JavaHome из указанной ветки реестра
    /// </summary>
    private static void CollectJavaHomes(RegistryKey key, List<string> paths)
    {
        try
        {
            // Пробуем получить JavaHome в текущем ключе
            var javaHome = key.GetValue("JavaHome") as string;
            if (!string.IsNullOrEmpty(javaHome) && !paths.Contains(javaHome, StringComparer.OrdinalIgnoreCase))
            {
                var javaPath = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(javaPath))
                    paths.Add(javaPath);
            }

            // Рекурсивно обходим подразделы
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey != null)
                        CollectJavaHomes(subKey, paths);
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
    /// Поиск Java в Program Files
    /// </summary>
    private static List<string> FindJavaInProgramFiles()
    {
        var paths = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var searchRoots = new List<string>
        {
            programFiles,
            programFilesX86,
            localAppData,
            Path.Combine(localAppData, "Programs"),
        };

        // Scoop: проверяем переменную окружения SCOOP и стандартный путь
        var scoopEnv = Environment.GetEnvironmentVariable("SCOOP");
        if (!string.IsNullOrEmpty(scoopEnv) && Directory.Exists(scoopEnv))
            searchRoots.Add(Path.Combine(scoopEnv, "apps"));
        var defaultScoop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps");
        if (Directory.Exists(defaultScoop))
            searchRoots.Add(defaultScoop);

        var keywords = new[] { "jdk", "jre", "java", "temurin", "corretto", "zulu",
                               "liberica", "graalvm", "jbr", "openjdk", "adopt", "jetbrains" };

        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root)) continue;

            try
            {
                var javaDirs = Directory.EnumerateDirectories(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 4,
                    IgnoreInaccessible = true
                })
                .Where(d =>
                {
                    var name = Path.GetFileName(d).ToLowerInvariant();
                    return keywords.Any(k => name.Contains(k));
                });

                foreach (var dir in javaDirs)
                {
                    var javaPath = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(javaPath))
                        paths.Add(javaPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to search {root}: {ex.Message}", "JavaManagementService");
            }
        }

        return paths;
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
    /// Добавление Java в конфигурацию
    /// </summary>
    public JavaInstallation? AddJava(string javaPath)
    {
        var java = GetJavaInfo(javaPath);
        if (java == null)
            return null;

        var config = configService.GetConfig();

        // Проверка на дубликат
        if (config.JavaInstallations.Any(j => j.Path == java.Path))
            return config.JavaInstallations.First(j => j.Path == java.Path);

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
    /// Scans the system for all installed Java runtimes and adds new ones to config.
    /// Skips paths already present in the configuration.
    /// </summary>
    public List<JavaInstallation> ScanAndAddJava()
    {
        var config = configService.GetConfig();
        var foundJava = FindInstalledJava();
        var addedCount = 0;

        foreach (var java in foundJava)
        {
            // Skip if already in config (by path)
            if (config.JavaInstallations.Any(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase)))
                continue;

            // If this is the first Java, make it default
            if (config.JavaInstallations.Count == 0 && addedCount == 0)
                java.IsDefault = true;

            config.JavaInstallations.Add(java);
            addedCount++;
        }

        if (addedCount > 0)
        {
            // If a default Java was added, update DefaultJavaId
            var newDefault = config.JavaInstallations.FirstOrDefault(j => j.IsDefault);
            if (newDefault != null)
                config.DefaultJavaId = newDefault.Id;

            configService.SaveConfig(config);
        }

        Logger.Info($"ScanAndAddJava: found {foundJava.Count} total, added {addedCount} new", "JavaManagementService");
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