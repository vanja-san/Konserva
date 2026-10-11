using Konserva.Models;
using Konserva.Utilities;
using System.IO;
using System.Text;

namespace Konserva.Services;

/// <summary>
/// Building the Java arguments used to start the server,
/// locating the Forge/NeoForge @args files, saving the NeoForge launch config.
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Read a Java argument file (@-file) and append its content to a StringBuilder.
    /// This replaces the use of @file with passing the arguments directly, which is necessary
    /// because some Java builds (for example, openjdk8-redhat) do not support the @-syntax.
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

            // Each line may contain one or more arguments
            // (separated by spaces). Split them and append each one.
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                args.Append($"{part} ");
            }
        }
    }

    /// <summary>
    /// Locate win_args.txt or unix_args.txt for a modern Forge version (47.x+, 1.20.1+).
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
    /// Locate win_args.txt or unix_args.txt in the NeoForge version folder inside libraries/.
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
    /// Build the Java arguments used for launching
    /// </summary>
    /// <param name="jarPath">Path to the server jar file</param>
    /// <param name="settings">Server settings</param>
    /// <param name="launchType">Mod loader type</param>
    public string BuildLaunchArgs(string jarPath, ServerSettings settings, ServerLaunchType launchType = ServerLaunchType.Standard, int javaMajorVersion = 0, string? serverPath = null)
    {
        var args = new StringBuilder();

        // RAM settings
        args.Append($"-Xms{settings.RamMin}M -Xmx{settings.RamMax}M ");

        // Force UTF-8 for all output (so that Russian text is not garbled)
        args.Append("-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 ");

        // Suppress "Advanced terminal features are not available in this environment"
        // The server runs without a real terminal (through the GUI), so this is expected
        // ANSI is disabled so that escape sequences do not pollute the log
        args.Append("-Dterminal.jline=false -Dterminal.ansi=false ");

        // Custom JVM arguments (GC optimizations and so on — configurable in the server settings)
        foreach (var arg in settings.JavaArgs)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            // ParallelRefProcEnabled is deprecated in Java 26+, skip it for JDK >= 26
            if (javaMajorVersion >= 26 && arg.Contains("ParallelRefProcEnabled", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"Skipping deprecated arg '{arg}' for Java {javaMajorVersion}", "McServerInstaller");
                continue;
            }

            args.Append($"{arg} ");
        }

        // NeoForge (21.x+) does not use -jar, it needs a classpath/module-path + bootstrap main class
        if (launchType == ServerLaunchType.NeoForge && !string.IsNullOrEmpty(serverPath))
        {
            var launchConfig = LoadNeoForgeLaunchConfig(serverPath);
            if (launchConfig != null)
            {
                if (!string.IsNullOrEmpty(launchConfig.ArgsFile))
                {
                    // ClassPath holds a relative path to the argument file — convert it to an absolute path
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

        // Modern Forge (47.x+, 1.20.1+): forge-*.jar without Main-Class, launched via the @args file
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

        // Jar file and nogui
        // For a jar from the libraries folder — use the path relative to serverPath,
        // otherwise — only the file name (the jar sits in the server root)
        string jarName;
        if (!string.IsNullOrEmpty(serverPath) && jarPath.StartsWith(serverPath, StringComparison.OrdinalIgnoreCase))
        {
            // Make the path relative to serverPath
            jarName = jarPath.Substring(serverPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        else
        {
            // Just the file name (jar in the server root or tests)
            jarName = Path.GetFileName(jarPath);
        }
        args.Append($"-jar \"{jarName}\" nogui");

        return args.ToString();
    }

    /// <summary>
    /// Save the NeoForge launch configuration (classpath + main class).
    /// It first tries to read win_args.txt/unix_args.txt (which contain the exact classpath from the installer),
    /// otherwise it scans libraries/.
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

        // Step 1: look for win_args.txt/unix_args.txt via the shared helper
        argsFilePath = FindNeoForgeArgsFile(serverPath);
        if (argsFilePath != null)
        {
            Logger.Info($"Found NeoForge args file: {argsFilePath}", "McServerInstaller");
            // Make the path relative to serverPath for use with @ in the Java command
            classPath = argsFilePath.Substring(serverPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        else
        {
            Logger.Info("No NeoForge args file found, scanning libraries for classpath...", "McServerInstaller");

            // Collect all jar files from libraries (excluding sources, javadoc)
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

        // Save the config
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
    /// Load the NeoForge launch configuration
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
    /// NeoForge launch configuration (classpath + main class instead of -jar)
    /// </summary>
    public class NeoForgeLaunchConfig
    {
        public string ClassPath { get; set; } = "";
        public string MainClass { get; set; } = "net.neoforged.bootstrap.Bootstrap";
        public string? ArgsFile { get; set; }
        public DateTime SavedAt { get; set; }
    }
}
