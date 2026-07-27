using Konserva.Models;
using Konserva.Utilities;
using System.IO;
using System.Text;

namespace Konserva.Services;

/// <summary>
/// Построение аргументов Java для запуска сервера,
/// поиск @args файлов Forge/NeoForge, сохранение конфига запуска NeoForge.
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Прочитать файл аргументов Java (@-файл) и добавить его содержимое в StringBuilder.
    /// Заменяет использование @file на прямую передачу аргументов, что необходимо,
    /// так как некоторые Java-сборки (например, openjdk8-redhat) не поддерживают @-синтаксис.
    /// </summary>
    private static void AppendArgsFile(StringBuilder args, string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            Logger.Warning($"Args file not found: {absolutePath}", "McServerInstaller");
            return;
        }

        var lines = File.ReadAllLines(absolutePath);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Каждая строка может содержать один или несколько аргументов
            // (разделённых пробелами). Разделяем и добавляем каждый.
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                args.Append($"{part} ");
            }
        }
    }

    /// <summary>
    /// Найти win_args.txt или unix_args.txt для современной версии Forge (47.x+, 1.20.1+).
    /// </summary>
    private static string? FindModernForgeArgsFile(string serverPath)
    {
        var forgeLibDir = Path.Combine(serverPath, "libraries", "net", "minecraftforge", "forge");
        if (!Directory.Exists(forgeLibDir))
            return null;

        try
        {
            var versionDirs = Directory.GetDirectories(forgeLibDir);
            if (versionDirs.Length == 0)
                return null;

            var versionDir = versionDirs.OrderByDescending(d => d).First();
            var winArgs = Path.Combine(versionDir, "win_args.txt");
            if (File.Exists(winArgs)) return winArgs;

            var unixArgs = Path.Combine(versionDir, "unix_args.txt");
            if (File.Exists(unixArgs)) return unixArgs;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to find modern Forge args file: {ex.Message}", "McServerInstaller");
        }

        return null;
    }

    /// <summary>
    /// Найти win_args.txt или unix_args.txt в папке версии NeoForge в libraries/.
    /// </summary>
    private static string? FindNeoForgeArgsFile(string serverPath)
    {
        var librariesDir = Path.Combine(serverPath, "libraries");
        if (!Directory.Exists(librariesDir))
            return null;

        try
        {
            foreach (var group in new[] { "neoforge", "forge" })
            {
                var neoforgeDir = Path.Combine(librariesDir, "net", "neoforged", group);
                if (!Directory.Exists(neoforgeDir)) continue;

                var versionDirs = Directory.GetDirectories(neoforgeDir);
                if (versionDirs.Length == 0) continue;

                var versionDir = versionDirs.OrderByDescending(d => d).First();
                var winArgs = Path.Combine(versionDir, "win_args.txt");
                if (File.Exists(winArgs)) return winArgs;

                var unixArgs = Path.Combine(versionDir, "unix_args.txt");
                if (File.Exists(unixArgs)) return unixArgs;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to find NeoForge args file: {ex.Message}", "McServerInstaller");
        }

        return null;
    }

    /// <summary>
    /// Построить аргументы Java для запуска
    /// </summary>
    /// <param name="jarPath">Путь к jar файлу сервера</param>
    /// <param name="settings">Настройки сервера</param>
    /// <param name="launchType">Тип модлоадера</param>
    public string BuildLaunchArgs(string jarPath, ServerSettings settings, ServerLaunchType launchType = ServerLaunchType.Standard, int javaMajorVersion = 0, string? serverPath = null)
    {
        var args = new StringBuilder();

        // RAM настройки
        args.Append($"-Xms{settings.RamMin}M -Xmx{settings.RamMax}M ");

        // Принудительно UTF-8 для всего вывода (чтобы русский текст не кракозябрился)
        args.Append("-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 ");

        // Подавляем "Advanced terminal features are not available in this environment"
        // Сервер запущен без реального терминала (через GUI), это ожидаемо
        // ANSI выключен, чтобы escape-последовательности не засоряли лог
        args.Append("-Dterminal.jline=false -Dterminal.ansi=false ");

        // Пользовательские JVM аргументы (GC оптимизации и т.п. — настраивается в настройках сервера)
        foreach (var arg in settings.JavaArgs)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            // ParallelRefProcEnabled deprecated в Java 26+, пропускаем для JDK >= 26
            if (javaMajorVersion >= 26 && arg.Contains("ParallelRefProcEnabled", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"Skipping deprecated arg '{arg}' for Java {javaMajorVersion}", "McServerInstaller");
                continue;
            }

            args.Append($"{arg} ");
        }

        // NeoForge (21.x+) не использует -jar, нужен classpath/module-path + bootstrap main class
        if (launchType == ServerLaunchType.NeoForge && !string.IsNullOrEmpty(serverPath))
        {
            var launchConfig = LoadNeoForgeLaunchConfig(serverPath);
            if (launchConfig != null)
            {
                if (!string.IsNullOrEmpty(launchConfig.ArgsFile))
                {
                    // ClassPath хранит относительный путь к файлу аргументов — конвертируем в абсолютный
                    var neoArgsPath = Path.Combine(serverPath, launchConfig.ClassPath);
                    AppendArgsFile(args, neoArgsPath);
                    args.Append("nogui ");
                }
                else if (FindNeoForgeArgsFile(serverPath) is string argsFile)
                {
                    AppendArgsFile(args, argsFile);
                    args.Append("nogui ");
                }
                else
                {
                    args.Append($"-cp \"{launchConfig.ClassPath}\" {launchConfig.MainClass} nogui");
                }
                return args.ToString();
            }
        }

        // Modern Forge (47.x+, 1.20.1+): forge-*.jar без Main-Class, запуск через @args файл
        if (launchType == ServerLaunchType.Forge && !string.IsNullOrEmpty(serverPath)
            && (string.IsNullOrEmpty(jarPath) || !HasMainClass(jarPath)))
        {
            var forgeArgs = FindModernForgeArgsFile(serverPath);
            if (forgeArgs != null)
            {
                AppendArgsFile(args, forgeArgs);
                args.Append("nogui ");
                return args.ToString();
            }
        }

        // Jar файл и nogui
        // Для jar из папки libraries — используем относительный путь от serverPath,
        // иначе — только имя файла (jar лежит в корне сервера)
        string jarName;
        if (!string.IsNullOrEmpty(serverPath) && jarPath.StartsWith(serverPath, StringComparison.OrdinalIgnoreCase))
        {
            // Делаем путь относительным от serverPath
            jarName = jarPath.Substring(serverPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        else
        {
            // Просто имя файла (jar в корне сервера или тесты)
            jarName = Path.GetFileName(jarPath);
        }
        args.Append($"-jar \"{jarName}\" nogui");

        return args.ToString();
    }

    /// <summary>
    /// Сохранить конфигурацию запуска NeoForge (classpath + main class).
    /// Сначала пробует прочитать win_args.txt/unix_args.txt (содержат точный classpath от установщика),
    /// иначе сканирует libraries/.
    /// </summary>
    public async Task SaveNeoForgeLaunchConfigAsync(string serverPath, CancellationToken ct = default)
    {
        var librariesDir = Path.Combine(serverPath, "libraries");
        if (!Directory.Exists(librariesDir))
        {
            Logger.Warning("No libraries directory found for NeoForge launch config", "McServerInstaller");
            return;
        }

        var mainClass = "net.neoforged.bootstrap.Bootstrap";
        string? classPath = null;
        string? argsFilePath = null;

        // Шаг 1: ищем win_args.txt/unix_args.txt через общий helper
        argsFilePath = FindNeoForgeArgsFile(serverPath);
        if (argsFilePath != null)
        {
            Logger.Info($"Found NeoForge args file: {argsFilePath}", "McServerInstaller");
            // Делаем путь относительным от serverPath для использования с @ в Java команде
            classPath = argsFilePath.Substring(serverPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        else
        {
            Logger.Info("No NeoForge args file found, scanning libraries for classpath...", "McServerInstaller");

            // Собираем все jar-файлы из libraries (исключая sources, javadoc)
            var allJars = Directory.GetFiles(librariesDir, "*.jar", SearchOption.AllDirectories)
                .Where(j => !j.EndsWith("-sources.jar", StringComparison.OrdinalIgnoreCase) &&
                            !j.EndsWith("-javadoc.jar", StringComparison.OrdinalIgnoreCase))
                .Order()
                .ToList();

            if (allJars.Count == 0)
            {
                Logger.Warning("No jars found in libraries for NeoForge", "McServerInstaller");
                return;
            }

            var classPathParts = new List<string>();
            foreach (var jar in allJars)
            {
                var relativePath = jar.Substring(serverPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                classPathParts.Add(relativePath);
            }
            classPath = string.Join(";", classPathParts);

            Logger.Info($"Built classpath from {allJars.Count} jars", "McServerInstaller");
        }

        // Сохраняем конфиг
        var config = new NeoForgeLaunchConfig
        {
            ClassPath = classPath,
            MainClass = mainClass,
            ArgsFile = argsFilePath != null ? Path.GetFileName(argsFilePath) : null,
            SavedAt = SystemTime.UtcNow
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config);
        await File.WriteAllTextAsync(Path.Combine(serverPath, ".neoforge-launch.json"), json, ct);

        Logger.Info($"Saved NeoForge launch config: cp={classPath.Length} chars, main={mainClass}, hasArgsFile={argsFilePath != null}", "McServerInstaller");
    }

    /// <summary>
    /// Загрузить конфигурацию запуска NeoForge
    /// </summary>
    public NeoForgeLaunchConfig? LoadNeoForgeLaunchConfig(string serverPath)
    {
        var configPath = Path.Combine(serverPath, ".neoforge-launch.json");
        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = File.ReadAllText(configPath);
            return System.Text.Json.JsonSerializer.Deserialize<NeoForgeLaunchConfig>(json);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load NeoForge launch config: {ex.Message}", "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Конфигурация запуска NeoForge (classpath + main class вместо -jar)
    /// </summary>
    public class NeoForgeLaunchConfig
    {
        public string ClassPath { get; set; } = "";
        public string MainClass { get; set; } = "net.neoforged.bootstrap.Bootstrap";
        public string? ArgsFile { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
