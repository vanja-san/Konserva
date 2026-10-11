using Konserva.Localization;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Konserva.Services;

/// <summary>
/// Forge server installation: resolving the URL, downloading the installer, running it
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Get the Forge installer URL
    /// </summary>
    public async Task<string?> GetForgeInstallerUrl(string mcVersion, string forgeVersion, CancellationToken ct = default)
    {
        try
        {
            // Forge uses the format: forge-{mc_version}-{forge_version}-installer.jar
            // URL: https://maven.minecraftforge.net/net/minecraftforge/forge/{mc_version}-{forge_version}/forge-{mc_version}-{forge_version}-installer.jar

            // If the version is "latest" or "recommended", we need to resolve a concrete version
            var actualVersion = forgeVersion;
            if (forgeVersion == "latest" || forgeVersion == "recommended")
            {
                Logger.Info($"Fetching Forge version list for {mcVersion} to resolve '{forgeVersion}'", "McServerInstaller");

                try
                {
                    var manifestUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
                    var manifest = await GetHttpClient().GetStringAsync(manifestUrl, ct);

                    using var doc = JsonDocument.Parse(manifest);
                    var promos = doc.RootElement.GetProperty("promos");

                    // Resolve the version for {mc_version}-latest or {mc_version}-recommended
                    var promoKey = $"{mcVersion}-{forgeVersion}";
                    if (promos.TryGetProperty(promoKey, out var promoVersion))
                    {
                        actualVersion = promoVersion.GetString()!;
                        Logger.Info($"Resolved '{forgeVersion}' to {actualVersion}", "McServerInstaller");
                    }
                    else
                    {
                        Logger.Warning($"Promo '{promoKey}' not found, using version list", "McServerInstaller");
                        // Fallback - parse HTML
                        var htmlUrl = $"https://files.minecraftforge.net/net/minecraftforge/forge/index_{mcVersion}.html";
                        var html = await GetHttpClient().GetStringAsync(htmlUrl, ct);
                        var matches = ForgeVersionRegex().Matches(html);
                        if (matches.Count > 0)
                        {
                            actualVersion = matches[0].Groups[1].Value;
                            Logger.Info($"Found Forge version {actualVersion} from HTML", "McServerInstaller");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to resolve Forge version: {ex.Message}", "McServerInstaller");
                    // Use the last known version for popular MC releases
                    actualVersion = mcVersion switch
                    {
                        "1.21.1" => "52.1.9",
                        "1.21" => "51.0.29",
                        "1.20.4" => "49.0.50",
                        "1.20.1" => "47.2.0",
                        "1.19.2" => "43.3.0",
                        "1.18.2" => "40.2.0",
                        "1.16.5" => "36.2.39",
                        _ => forgeVersion
                    };
                }
            }

            // Build the correct Maven path: {mc_version}-{forge_version}
            // forgeVersion already contains the full version (for example "52.1.9"), so mcVersion must not be duplicated
            var mavenPath = $"{mcVersion}-{actualVersion}";

            // Check whether actualVersion already starts with mcVersion (to avoid duplication)
            if (actualVersion.StartsWith(mcVersion + "-"))
            {
                mavenPath = actualVersion;
            }

            var urls = new[]
            {
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mavenPath}/forge-{mavenPath}-installer.jar",
                $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{mavenPath}/forge-{mavenPath}-installer.jar"
            };

            foreach (var url in urls)
            {
                Logger.Info($"Checking Forge URL: {url}", "McServerInstaller");

                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await GetHttpClient().SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    Logger.Info($"Forge installer found: {url}", "McServerInstaller");
                    return url;
                }
                else
                {
                    Logger.Warning($"Forge URL check failed: {response.StatusCode}", "McServerInstaller");
                }
            }

            Logger.Error("All Forge URLs failed", null, "McServerInstaller");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get Forge installer URL: {ex.GetType().Name} - {ex.Message}", ex, "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Install a Forge server automatically
    /// </summary>
    public async Task<bool> InstallForgeServer(string mcVersion, string forgeVersion,
        string destinationPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(destinationPath);

            var installerUrl = await GetForgeInstallerUrl(mcVersion, forgeVersion, ct);
            if (string.IsNullOrEmpty(installerUrl))
                return false;

            // 1. Download the installer
            var downloadsDir = Constants.DownloadsPath;
            Directory.CreateDirectory(downloadsDir);
            var installerPath = Path.Combine(downloadsDir, "forge-installer.jar");
            progress?.Report(LocalizationManager.Get("Installer_DownloadingInstaller"));
            var downloadResult = await DownloadFile(installerUrl, downloadsDir, "forge-installer.jar", progress, ct);
            if (!downloadResult.success)
                return false;

            // 2. Run the installer
            progress?.Report(string.Format(LocalizationManager.Get("Installer_RunningInstaller"), "Forge"));
            var success = await RunForgeInstaller(installerPath, destinationPath, ct, progress);

            // 3. Delete the installer
            if (success && File.Exists(installerPath))
            {
                try { File.Delete(installerPath); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McServerInstaller] Cleanup delete failed: {ex.Message}"); }
            }

            if (success)
            {
                await WaitForFileStabilityAsync(destinationPath, "forge", ct, progress);

                // Copy the forge universal jar from libraries to the root
                try
                {
                    var librariesPath = Path.Combine(destinationPath, "libraries");
                    if (Directory.Exists(librariesPath))
                    {
                        var forgeJars = Directory.GetFiles(librariesPath, "forge-*-universal.jar", SearchOption.AllDirectories);
                        if (forgeJars.Length == 0)
                            forgeJars = Directory.GetFiles(librariesPath, "forge-*.jar", SearchOption.AllDirectories);
                        if (forgeJars.Length > 0)
                        {
                            var srcJar = forgeJars[0];
                            var jarName = $"forge-{forgeVersion}.jar";
                            var dstJar = Path.Combine(destinationPath, jarName);
                            if (!File.Exists(dstJar))
                            {
                                File.Copy(srcJar, dstJar);
                                Logger.Info($"Copied universal jar to {jarName}", "McServerInstaller");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to copy Forge universal jar: {ex.Message}", "McServerInstaller");
                }
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Run the Forge installer
    /// </summary>
    private async Task<bool> RunForgeInstaller(string installerPath, string destinationPath, CancellationToken ct, IProgress<string>? progress = null)
    {
        Logger.Info($"Running Forge/NeoForge installer: {installerPath}");

        var javaPath = FindJavaPath();
        if (string.IsNullOrEmpty(javaPath))
        {
            Logger.Error("Java not found for Forge/NeoForge installer");
            return false;
        }

        Logger.Info($"Using Java: {javaPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-Dfile.encoding=UTF-8 -jar \"{installerPath}\" --installServer",
            WorkingDirectory = destinationPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process == null)
            {
                Logger.Error("Failed to start Forge/NeoForge installer process");
                return false;
            }

            // On cancellation we kill the process so that ReadLine() completes
            using var cancellationRegistration = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(); }
                catch { }
            });

            Logger.Info($"Installer process started, PID: {process.Id}");

            // Read the output LINE BY LINE SYNCHRONOUSLY — this guarantees that we do not miss
            // the completion moment. WaitForExit is unreliable on Windows.
            var outputLines = new List<string>();
            var errorLines = new List<string>();
            var startTime = SystemTime.Now;
            bool installedSuccessfully = false;
            int patchCount = 0;
            int libraryCount = 0;

            // Read stdout line by line — blocks while there is output
            var lastReportedLibraryCount = 0;
            await Task.Run(() =>
            {
                string? line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();

                    lock (outputLines) outputLines.Add(line);
                    Logger.Info($"[Forge] {line}", "McServerInstaller");

                    // Count for the status report
                    if (line.Contains("Considering library"))
                        libraryCount++;
                    if (line.Contains("Patching "))
                        patchCount++;
                    if (line.Contains("installed successfully"))
                        installedSuccessfully = true;

                    // Report progress once every 50 libraries
                    if (libraryCount > 0 && libraryCount - lastReportedLibraryCount >= 50)
                    {
                        lastReportedLibraryCount = libraryCount;
                        progress?.Report($"{LocalizationManager.Get("Installer_Installing")} ({libraryCount} libraries)");
                    }
                }
            }, ct);

            // Read stderr
            await Task.Run(() =>
            {
                string? line;
                while ((line = process.StandardError.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    lock (errorLines) errorLines.Add(line);
                    Logger.Info($"[Forge] {line}", "McServerInstaller");
                }
            }, ct);

            // Wait for the process to finish (returns immediately after Kill())
            process.WaitForExit();

            var elapsed = SystemTime.Now - startTime;
            Logger.Info($"Installer exited with code: {process.ExitCode} after {elapsed.TotalSeconds:F1}s", "McServerInstaller");
            Logger.Info($"Forge stats: {libraryCount} libraries, {patchCount} patches, installedSuccessfully={installedSuccessfully}", "McServerInstaller");

            // Verify success
            var hasForgeJar = Directory.GetFiles(destinationPath, "forge-*.jar").Length > 0;
            var hasNeoForgeJar = Directory.GetFiles(destinationPath, "neoforge-*.jar").Length > 0;
            var hasRunBat = File.Exists(Path.Combine(destinationPath, "run.bat"));
            var hasLibraries = Directory.Exists(Path.Combine(destinationPath, "libraries"));

            var success = installedSuccessfully || process.ExitCode == 0 || hasForgeJar || hasNeoForgeJar || hasRunBat || hasLibraries;

            if (!success)
            {
                Logger.Error($"Forge installer failed. ExitCode={process.ExitCode}", null, "McServerInstaller");
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            // Kill the process if it is still running (safety net — ct.Register already called Kill)
            if (process != null) { try { if (!process.HasExited) process.Kill(); } catch { } }
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"Forge/NeoForge installer exception: {ex.Message}", ex, "McServerInstaller");
            return false;
        }
        finally
        {
            process?.Dispose();
        }
    }


}
