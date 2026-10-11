using Konserva.Localization;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Konserva.Services;

/// <summary>
/// Quilt server installation: fetching information, downloading, running the installer
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Get information about the Quilt installer
    /// API: https://meta.quiltmc.org/v3/versions/installer (returns an array)
    /// </summary>
    public async Task<(string url, string version)?> GetQuiltInstallerInfo(CancellationToken ct = default)
    {
        try
        {
            var response = await GetHttpClient().GetStringAsync(
                "https://meta.quiltmc.org/v3/versions/installer", ct);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // The Quilt API returns a plain array (not an object with value)
            var array = root.EnumerateArray();
            if (!array.MoveNext())
                return null;

            var latest = array.Current;
            var url = latest.GetProperty("url").GetString()!;
            var version = latest.GetProperty("version").GetString()!;
            return (url, version);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get Quilt installer info: {ex.Message}", ex, "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Install a Quilt server automatically
    /// </summary>
    public async Task<bool> InstallQuiltServer(string mcVersion, string loaderVersion,
        string destinationPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Logger.Info($"Installing Quilt server MC {mcVersion} loader {loaderVersion}", "McServerInstaller");
            Directory.CreateDirectory(destinationPath);

            // 1. Download the Quilt installer
            var installerInfo = await GetQuiltInstallerInfo(ct);
            if (installerInfo == null)
            {
                Logger.Error("Failed to get Quilt installer info", null, "McServerInstaller");
                return false;
            }

            Logger.Info($"Got Quilt installer: {installerInfo.Value.version}", "McServerInstaller");

            var downloadsDir = Constants.DownloadsPath;
            Directory.CreateDirectory(downloadsDir);
            var installerPath = Path.Combine(downloadsDir, "quilt-installer.jar");
            progress?.Report(LocalizationManager.Get("Installer_DownloadingInstaller"));
            var downloadResult = await DownloadFile(installerInfo.Value.url, downloadsDir, "quilt-installer.jar", progress, ct);
            if (!downloadResult.success)
            {
                Logger.Error($"Failed to download Quilt installer: {downloadResult.error}", null, "McServerInstaller");
                return false;
            }

            Logger.Info("Downloaded Quilt installer, running...", "McServerInstaller");

            // 2. Run the installer with the --download-server flag (30-90%)
            progress?.Report(string.Format(LocalizationManager.Get("Installer_RunningInstaller"), "Quilt"));
            var success = await RunQuiltInstaller(installerPath, mcVersion, destinationPath, ct, progress);

            // 3. If Quilt created a "server" subfolder - move the files to the root
            if (success)
            {
                var serverSubfolder = Path.Combine(destinationPath, "server");
                if (Directory.Exists(serverSubfolder))
                {
                    await MoveQuiltSubfolderAsync(serverSubfolder, destinationPath);
                }
            }

            // Delete the installer in any case
            try
            {
                if (File.Exists(installerPath))
                    File.Delete(installerPath);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McServerInstaller] Cleanup delete failed: {ex.Message}"); }

            if (success)
            {
                await WaitForFileStabilityAsync(destinationPath, "quilt", ct, maxWaitSeconds: 30, checkRunBat: false);
                progress?.Report(LocalizationManager.Get("Installer_Finishing"));
                Logger.Info("Quilt file operations completed", "McServerInstaller");
            }

            return success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Run the Quilt installer
    /// Official command: java -jar quilt-installer.jar install server MINECRAFT_VERSION --download-server
    /// </summary>
    private async Task<bool> RunQuiltInstaller(string installerPath, string mcVersion,
        string destinationPath, CancellationToken ct, IProgress<string>? progress = null)
    {
        // Determine the Java version based on the Minecraft version
        var javaPath = FindJavaPathForVersion(mcVersion);
        if (string.IsNullOrEmpty(javaPath))
        {
            Logger.Error($"Java not found for Minecraft {mcVersion}", null, "McServerInstaller");
            return false;
        }

        Logger.Info($"Using Java: {javaPath} for Quilt installer", "McServerInstaller");

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-Dfile.encoding=UTF-8 -jar \"{installerPath}\" install server {mcVersion} --download-server",
            WorkingDirectory = destinationPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Logger.Error("Failed to start Quilt installer process", null, "McServerInstaller");
                return false;
            }

            progress?.Report(LocalizationManager.Get("Installer_Running"));

            var output = new List<string>();
            var error = new List<string>();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));
            var combinedToken = timeoutCts.Token;

            try
            {
                var stdoutTask = Task.Run(() =>
                {
                    string? line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        lock (output) output.Add(line);
                        Logger.Info($"[Quilt] {line}", "McServerInstaller");
                    }
                }, combinedToken);

                var stderrTask = Task.Run(() =>
                {
                    string? line;
                    while ((line = process.StandardError.ReadLine()) != null)
                    {
                        lock (error) error.Add(line);
                        Logger.Info($"[Quilt/stderr] {line}", "McServerInstaller");
                    }
                }, combinedToken);

                await process.WaitForExitAsync(combinedToken);
                await Task.WhenAll(stdoutTask, stderrTask);
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }

                if (ct.IsCancellationRequested)
                    Logger.Info("Quilt installer cancelled by user", "McServerInstaller");
                else
                    Logger.Warning("Quilt installer timeout after 5 minutes", "McServerInstaller");
                return false;
            }

            if (output.Count > 0)
                Logger.Info($"Quilt installer output: {string.Join("\n", output.Take(20))}", "McServerInstaller");
            if (error.Count > 0)
                Logger.Warning($"Quilt installer errors: {string.Join("\n", error.Take(20))}", "McServerInstaller");

            var jarFiles = Directory.GetFiles(destinationPath, "quilt-server-*.jar");
            var success = process.ExitCode == 0 || jarFiles.Length > 0;

            if (!success)
                Logger.Error($"Quilt installer failed with exit code: {process.ExitCode}", null, "McServerInstaller");
            else
                Logger.Info($"Quilt installer completed successfully (exit code: {process.ExitCode})", "McServerInstaller");

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception running Quilt installer: {ex.Message}", ex, "McServerInstaller");
            return false;
        }
    }

    /// <summary>
    /// Move the files from the server/ subfolder to the root (Quilt sometimes creates a nested structure)
    /// </summary>
    private async Task MoveQuiltSubfolderAsync(string serverSubfolder, string destinationPath)
    {
        try
        {
            foreach (var file in Directory.GetFiles(serverSubfolder))
            {
                var fileName = Path.GetFileName(file);
                var destPath = Path.Combine(destinationPath, fileName);
                if (!File.Exists(destPath))
                {
                    await Task.Run(() => File.Move(file, destPath));
                }
            }

            foreach (var dir in Directory.GetDirectories(serverSubfolder))
            {
                var dirName = Path.GetFileName(dir);
                var destDir = Path.Combine(destinationPath, dirName);
                if (!Directory.Exists(destDir))
                {
                    Directory.Move(dir, destDir);
                }
            }

            try { Directory.Delete(serverSubfolder, true); }
            catch { }

            Logger.Info("Moved Quilt server files from subfolder to root", "McServerInstaller");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to move Quilt files: {ex.Message}", "McServerInstaller");
        }
    }


}
