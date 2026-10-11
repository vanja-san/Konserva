using CommunityToolkit.Mvvm.DependencyInjection;
using Konserva.Dialogs;
using Konserva.Localization;
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
/// Service for managing Java installations
/// </summary>
public class JavaManagementService(IConfigService configService) : IJavaManagementService
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle hFile, [Out] StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);
    /// <summary>
    /// Searches for installed Java runtimes on the machine.
    /// Only PATH (where java) + the Windows registry — no file system scanning.
    /// </summary>
    public List<JavaInstallation> FindInstalledJava()
    {
        var javaInstallations = new List<JavaInstallation>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. All java.exe from PATH (covers Scoop, Chocolatey, manual installations)
        foreach (var java in FindAllJavaInPath())
        {
            if (seenPaths.Add(ResolveRealPath(java.Path)))
                javaInstallations.Add(java);
        }

        // 2. From the Windows registry (64-bit and 32-bit views)
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
    /// Searches for all Java installations in PATH via the registry (always fresh, no restart needed).
    /// </summary>
    private List<JavaInstallation> FindAllJavaInPath()
    {
        var results = new List<JavaInstallation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var pathDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // System PATH (HKLM)
            var systemPath = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment",
                "Path", "") as string;
            if (!string.IsNullOrEmpty(systemPath))
            {
                foreach (var dir in systemPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!string.IsNullOrEmpty(dir))
                        pathDirs.Add(Environment.ExpandEnvironmentVariables(dir));
            }

            // User PATH (HKCU)
            var userPath = Registry.GetValue(
                @"HKEY_CURRENT_USER\Environment",
                "Path", "") as string;
            if (!string.IsNullOrEmpty(userPath))
            {
                foreach (var dir in userPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (!string.IsNullOrEmpty(dir))
                        pathDirs.Add(Environment.ExpandEnvironmentVariables(dir));
            }

            // Look for javaw.exe (preferred) or java.exe in each PATH directory.
            // If javaw.exe exists — java.exe in the same folder is not checked (duplicate).
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
    /// Standard Java paths
    /// </summary>
    /// <summary>
    /// Searches for Java in the Windows registry.
    /// Checks the 64-bit and 32-bit (Wow6432Node) views for every key.
    /// </summary>
    private static List<string> FindJavaInRegistry()
    {
        var paths = new List<string>();
        var seenRealPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // (registryHive, subKey) pairs used to look up JavaHome
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
                    // Hive is unavailable — skip it
                }
            }
        }

        return paths;
    }

    /// <summary>
    /// Recursively collects all JavaHome values from the given registry branch
    /// </summary>
    private static void CollectJavaHomes(RegistryKey key, List<string> paths, HashSet<string> seenRealPaths)
    {
        try
        {
            // Try to read JavaHome from the current key
            var javaHome = key.GetValue("JavaHome") as string;
            if (!string.IsNullOrEmpty(javaHome))
            {
                var javaPath = FindJavaExeInBin(Path.Combine(javaHome, "bin"));
                if (javaPath != null && seenRealPaths.Add(ResolveRealPath(javaPath)))
                    paths.Add(javaPath);
            }

            // Recurse into subkeys
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
                    // Subkey is unavailable — skip it
                }
            }
        }
        catch
        {
            // Key is unavailable — skip it
        }
    }



    /// <summary>
    /// Getting information about a Java installation
    /// </summary>
    public JavaInstallation? GetJavaInfo(string javaPath)
    {
        try
        {
            // Check that the file exists (with or without .exe)
            if (!File.Exists(javaPath) && !File.Exists(javaPath + ".exe"))
            {
                Logger.Warning($"Java file not found: {javaPath}", "JavaManagementService");
                return null;
            }

            var actualPath = File.Exists(javaPath) ? javaPath : javaPath + ".exe";

            // Prefer javaw.exe (no console window) for launching the server
            actualPath = ResolvePreferredJavaPath(actualPath);

            Logger.Info($"Checking Java: {actualPath}", "JavaManagementService");

            // For javaw.exe use java.exe to get the version
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

            // Parse the version
            var version = JavaVersionParser.ParseVersion(versionOutput);
            var majorVersion = JavaVersionParser.ParseMajorVersion(versionOutput);

            // Get the name
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
    /// Gets the Java display name from a path
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
    /// If javaw.exe exists next to java.exe — returns the path to javaw.exe.
    /// Otherwise returns the original path.
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
    /// Looks for java.exe or javaw.exe in the given bin folder.
    /// Returns null if neither is found.
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
    /// Resolves all junctions/symlinks in a path via Win32 GetFinalPathNameByHandle.
    /// Works reliably for Scoop current → version and any other redirections.
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
            // could not open the file — fall back to plain normalization
        }

        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    /// <summary>
    /// Adding a Java installation to the configuration
    /// </summary>
    public JavaInstallation? AddJava(string javaPath)
    {
        var java = GetJavaInfo(javaPath);
        if (java == null)
            return null;

        var config = configService.GetConfig();

        // Duplicate check (case-insensitive for Windows)
        if (config.JavaInstallations.Any(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase)))
            return config.JavaInstallations.First(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase));

        // If this is the first Java, make it the default one
        if (config.JavaInstallations.Count == 0)
            java.IsDefault = true;

        config.JavaInstallations.Add(java);

        if (java.IsDefault)
            config.DefaultJavaId = java.Id;

        configService.SaveConfig(config);
        return java;
    }

    /// <summary>
    /// Removing a Java installation from the configuration
    /// </summary>
    public bool RemoveJava(string javaId)
    {
        var config = configService.GetConfig();
        var java = config.JavaInstallations.FirstOrDefault(j => j.Id == javaId);
        if (java == null)
            return false;

        config.JavaInstallations.Remove(java);

        // If the default Java was removed, pick another one
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
    /// Setting the default Java installation
    /// </summary>
    public bool SetDefaultJava(string javaId)
    {
        var config = configService.GetConfig();
        var java = config.JavaInstallations.FirstOrDefault(j => j.Id == javaId);
        if (java == null)
            return false;

        // Update IsDefault for all of them
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

        // The whole read-modify-write runs under the config lock: this method mutates the
        // list that a starting server reads from another thread.
        config.MutateJavaInstallations(list =>
        {
            // 1. Clean up missing entries and update paths to javaw.exe
            var toRemove = new List<string>();
            foreach (var existing in list)
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
                var entry = list.FirstOrDefault(j => j.Id == id);
                if (entry == null)
                    continue;

                list.Remove(entry);
                if (config.DefaultJavaId == id)
                {
                    var newDefault = list.FirstOrDefault();
                    config.DefaultJavaId = newDefault?.Id;
                    if (newDefault != null) newDefault.IsDefault = true;
                }
                changed = true;
            }

            // 2. Add new installations (paths are already resolved to javaw.exe by GetJavaInfo)
            foreach (var java in foundJava)
            {
                if (list.Any(j => string.Equals(j.Path, java.Path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (list.Count == 0)
                    java.IsDefault = true;

                list.Add(java);
                changed = true;
            }

            if (changed)
            {
                var newDefault = list.FirstOrDefault(j => j.IsDefault);
                if (newDefault != null)
                    config.DefaultJavaId = newDefault.Id;
            }
        });

        if (changed)
        {
            configService.SaveConfig(config);
        }

        Logger.Info($"ScanAndAddJava: found {foundJava.Count} total, config has {config.JavaInstallations.Count} after cleanup", "JavaManagementService");
        return [.. config.JavaInstallations];
    }
}
