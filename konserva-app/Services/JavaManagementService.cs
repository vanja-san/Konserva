using Konserva.Models;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;

namespace Konserva.Services;

/// <summary>
/// Сервис управления Java установками
/// </summary>
public partial class JavaManagementService(IConfigService configService) : IJavaManagementService
{

    /// <summary>
    /// Поиск установленных Java на компьютере
    /// </summary>
    public List<JavaInstallation> FindInstalledJava()
    {
        var javaInstallations = new List<JavaInstallation>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Поиск в PATH (java)
        var javaInPath = FindJavaInPath();
        if (javaInPath != null && seenPaths.Add(javaInPath.Path))
            javaInstallations.Add(javaInPath);

        // 2. Поиск в стандартных расположениях Windows
        foreach (var path in GetStandardJavaPaths())
        {
            if ((File.Exists(path) || File.Exists(path + ".exe")) && seenPaths.Add(path))
            {
                var java = GetJavaInfo(path);
                if (java != null)
                    javaInstallations.Add(java);
            }
        }

        // 3. Поиск через реестр Windows
        foreach (var path in FindJavaInRegistry())
        {
            if (seenPaths.Add(path))
            {
                var java = GetJavaInfo(path);
                if (java != null)
                    javaInstallations.Add(java);
            }
        }

        // 4. Поиск в Program Files
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
    /// Поиск Java в PATH
    /// </summary>
    private static JavaInstallation? FindJavaInPath()
    {
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
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            var paths = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length > 0)
            {
                var javaPath = paths[0].Trim();
                return new JavaManagementService(App.ConfigService).GetJavaInfo(javaPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to find Java in PATH: {ex.Message}", "JavaManagementService");
        }

        return null;
    }

    /// <summary>
    /// Стандартные пути к Java
    /// </summary>
    private static List<string> GetStandardJavaPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return
        [
            Path.Combine(programFiles, "Java", "jdk-21", "bin", "java.exe"),
            Path.Combine(programFiles, "Java", "jdk-17", "bin", "java.exe"),
            Path.Combine(programFiles, "Java", "jdk-11", "bin", "java.exe"),
            Path.Combine(programFiles, "Java", "jdk-8", "bin", "java.exe"),
            Path.Combine(programFiles, "Java", "jre-21", "bin", "java.exe"),
            Path.Combine(programFiles, "Java", "jre-17", "bin", "java.exe"),
            Path.Combine(programFiles, "Java", "jre-8", "bin", "java.exe"),
            Path.Combine(programFilesX86, "Java", "jdk-21", "bin", "java.exe"),
            Path.Combine(programFilesX86, "Java", "jdk-17", "bin", "java.exe"),
            Path.Combine(programFilesX86, "Java", "jdk-11", "bin", "java.exe"),
            Path.Combine(programFilesX86, "Java", "jdk-8", "bin", "java.exe"),
            Path.Combine(programFiles, "Microsoft", "jdk-21.0.101-hotspot", "bin", "java.exe"),
            Path.Combine(programFiles, "Microsoft", "jdk-17.0.101-hotspot", "bin", "java.exe"),
            Path.Combine(programFiles, "Microsoft", "jdk-11.0.20.101-hotspot", "bin", "java.exe"),
            Path.Combine(localAppData, "Programs", "Eclipse Adoptium", "jdk-21.0.101-hotspot", "bin", "java.exe"),
            Path.Combine(localAppData, "Programs", "Eclipse Adoptium", "jdk-17.0.101-hotspot", "bin", "java.exe"),
        ];
    }

    /// <summary>
    /// Поиск Java в реестре Windows
    /// </summary>
    private static List<string> FindJavaInRegistry()
    {
        var paths = new List<string>();

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-Command \"Get-ChildItem -Path 'HKLM:\\SOFTWARE\\JavaSoft\\Java Runtime Environment' -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty -Path $_.PsPath -ErrorAction SilentlyContinue | Select-Object -ExpandProperty JavaHome }\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var javaHome = line.Trim();
                    if (!string.IsNullOrEmpty(javaHome) && Directory.Exists(javaHome))
                    {
                        var javaPath = Path.Combine(javaHome, "bin", "java.exe");
                        if (File.Exists(javaPath))
                            paths.Add(javaPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to search registry for Java: {ex.Message}", "JavaManagementService");
        }

        return paths;
    }

    /// <summary>
    /// Поиск Java в Program Files
    /// </summary>
    private static List<string> FindJavaInProgramFiles()
    {
        var paths = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        try
        {
            var javaDirs = Directory.GetDirectories(programFiles)
                .Where(d => d.Contains("jdk", StringComparison.OrdinalIgnoreCase) ||
                           d.Contains("java", StringComparison.OrdinalIgnoreCase) ||
                           d.Contains("jre", StringComparison.OrdinalIgnoreCase));

            foreach (var dir in javaDirs)
            {
                var javaPath = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(javaPath))
                    paths.Add(javaPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to search Program Files for Java: {ex.Message}", "JavaManagementService");
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
            process.WaitForExit(10000);

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
    private static int GetRequiredJavaVersion(string mcVersion, McServerInstaller.ServerLaunchType launchType)
    {
        return JavaVersionParser.GetRequiredJavaVersion(mcVersion, launchType);
    }
}