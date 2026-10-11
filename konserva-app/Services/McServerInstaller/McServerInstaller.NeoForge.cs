using Konserva.Localization;
using Konserva.Utilities;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Konserva.Services;

/// <summary>
/// NeoForge server installation: resolving the URL, reading versions from Maven metadata, installing
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Get the NeoForge installer URL
    /// </summary>
    public async Task<string?> GetNeoForgeInstallerUrl(string mcVersion, string neoforgeVersion, CancellationToken ct = default)
    {
        try
        {
            // If the version is "latest", we need to resolve a concrete version
            var actualVersion = neoforgeVersion;
            if (neoforgeVersion == "latest" || neoforgeVersion == "recommended")
            {
                Logger.Info($"Fetching NeoForge version list for {mcVersion} to resolve '{neoforgeVersion}'", "McServerInstaller");

                // Try to obtain the latest version from the Maven metadata
                // The NeoForge promotions API (promotions_slim.json) was removed after the Forge/NeoForge split
                actualVersion = await GetLatestNeoForgeVersionFromMetadata(mcVersion, ct) ?? neoforgeVersion;
            }

            // Build the URL with the NeoForge version
            // For MC < 1.21 the neoforged/forge/ path is used, for MC >= 1.21 — neoforged/neoforge/
            var isNewNeoForge = IsNewNeoForgePath(mcVersion);
            var baseGroup = isNewNeoForge ? "neoforge" : "forge";
            var prefix = isNewNeoForge ? "neoforge-" : "forge-";
            string mavenPath = actualVersion;

            // Try several URL formats
            var urls = new List<string>
            {
                // Format 1: classic Maven (new neoforged/neoforge/ path or old neoforged/forge/)
                $"https://maven.neoforged.net/releases/net/neoforged/{baseGroup}/{mavenPath}/{prefix}{mavenPath}-installer.jar",
                // Format 2: Maven with the mc version
                $"https://maven.neoforged.net/releases/net/neoforged/{baseGroup}/{mcVersion}-{mavenPath}/{prefix}{mcVersion}-{mavenPath}-installer.jar",
                // Format 3: direct download
                $"https://maven.neoforged.net/api/v1/installer/{mcVersion}-{mavenPath}"
            };

            // If we are not sure about the path, try both variants
            if (!isNewNeoForge)
            {
                urls.Insert(0, $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{mavenPath}/neoforge-{mavenPath}-installer.jar");
                urls.Insert(1, $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{mcVersion}-{mavenPath}/neoforge-{mcVersion}-{mavenPath}-installer.jar");
            }

            foreach (var url in urls)
            {
                Logger.Info($"Checking NeoForge URL: {url}", "McServerInstaller");

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, url);
                    using var response = await GetHttpClient().SendAsync(request, ct);

                    if (response.IsSuccessStatusCode)
                    {
                        Logger.Info($"NeoForge installer found: {url}", "McServerInstaller");
                        return url;
                    }
                    else
                    {
                        Logger.Warning($"NeoForge URL check failed: {response.StatusCode}", "McServerInstaller");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"NeoForge URL error: {ex.Message}", "McServerInstaller");
                }
            }

            // If none of the URLs worked, try to get the version from the Maven metadata (fallback)
            Logger.Info("Trying to fetch NeoForge version from Maven metadata (fallback)...", "McServerInstaller");
            var fallbackVersion = await GetLatestNeoForgeVersionFromMetadata(mcVersion, ct);
            if (fallbackVersion != null)
            {
                var fallbackUrl = $"https://maven.neoforged.net/releases/net/neoforged/{baseGroup}/{fallbackVersion}/{prefix}{fallbackVersion}-installer.jar";
                Logger.Info($"Using latest NeoForge version from metadata: {fallbackVersion}", "McServerInstaller");
                return fallbackUrl;
            }

            Logger.Error("All NeoForge URLs failed", null, "McServerInstaller");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get NeoForge installer URL: {ex.GetType().Name} - {ex.Message}", ex, "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Determine whether the new NeoForge path (neoforged/neoforge/) is used for a given MC version
    /// </summary>
    private static bool IsNewNeoForgePath(string mcVersion)
    {
        // NeoForge switched to neoforged/neoforge/ starting with MC 1.21
        if (Version.TryParse(mcVersion, out var ver))
        {
            return ver.Major >= 1 && ver.Minor >= 21;
        }
        return false;
    }

    /// <summary>
    /// Get the latest NeoForge version matching an MC version from the Maven metadata
    /// </summary>
    private async Task<string?> GetLatestNeoForgeVersionFromMetadata(string mcVersion, CancellationToken ct = default)
    {
        try
        {
            var neoforgePrefix = ConvertMcVersionToNeoForgeFormat(mcVersion);
            var metadataUrls = new[]
            {
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
                $"https://maven.neoforged.net/releases/net/neoforged/forge/maven-metadata.xml"
            };

            foreach (var metadataUrl in metadataUrls)
            {
                try
                {
                    Logger.Info($"Fetching NeoForge metadata: {metadataUrl}", "McServerInstaller");
                    var metadata = await GetHttpClient().GetStringAsync(metadataUrl, ct);

                    // Search for all versions
                    var versionMatches = Regex.Matches(metadata, @"<version>([^<]+)</version>");
                    var versions = versionMatches
                        .Select(m => m.Groups[1].Value)
                        .Where(v => v.StartsWith(neoforgePrefix) || v.StartsWith($"{mcVersion}-{neoforgePrefix}"))
                        .ToList();

                    if (versions.Count > 0)
                    {
                        // Sort in descending order (latest version first)
                        var latest = versions.OrderByDescending(v => v).First();
                        Logger.Info($"Found NeoForge version {latest} for MC {mcVersion} in {metadataUrl}", "McServerInstaller");
                        return latest;
                    }

                    Logger.Warning($"No NeoForge versions matching prefix '{neoforgePrefix}' in {metadataUrl}", "McServerInstaller");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to fetch metadata from {metadataUrl}: {ex.Message}", "McServerInstaller");
                }
            }

            // Fallback to known versions
            var fallback = mcVersion switch
            {
                "1.21.4" => "21.4.0",
                "1.21.3" => "21.3.0",
                "1.21.1" => "21.1.0",
                "1.21" => "21.0.167",
                "1.20.6" => "20.6.106",
                "1.20.4" => "20.4.238",
                "1.20.1" => "19.0.56",
                "1.19.4" => "18.0.0",
                "1.19.3" => "17.0.0",
                "1.19.2" => "16.0.0",
                "1.18.2" => "14.0.0",
                _ => null
            };
            return fallback;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get NeoForge version from metadata: {ex.Message}", "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Install a NeoForge server automatically
    /// </summary>
    public async Task<bool> InstallNeoForgeServer(string mcVersion, string neoforgeVersion,
        string destinationPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Logger.Info($"Installing NeoForge server: MC={mcVersion}, Version={neoforgeVersion}, Path={destinationPath}");

        try
        {
            Directory.CreateDirectory(destinationPath);

            var installerUrl = await GetNeoForgeInstallerUrl(mcVersion, neoforgeVersion, ct);
            if (string.IsNullOrEmpty(installerUrl))
            {
                Logger.Error("NeoForge installer URL is null or empty");
                return false;
            }

            // 1. Download the installer
            var downloadsDir = Constants.DownloadsPath;
            Directory.CreateDirectory(downloadsDir);
            var installerPath = Path.Combine(downloadsDir, "neoforge-installer.jar");
            progress?.Report(LocalizationManager.Get("Installer_DownloadingInstaller"));

            Logger.Info($"Downloading NeoForge installer from: {installerUrl}");
            var downloadResult = await DownloadFile(installerUrl, downloadsDir, "neoforge-installer.jar", progress, ct);

            if (!downloadResult.success)
            {
                Logger.Error($"Failed to download NeoForge installer: {downloadResult.error}");
                return false;
            }

            Logger.Info("NeoForge installer downloaded, running installer...");

            // 2. Run the installer
            progress?.Report(string.Format(LocalizationManager.Get("Installer_RunningInstaller"), "NeoForge"));
            var success = await RunForgeInstaller(installerPath, destinationPath, ct, progress);

            if (success && File.Exists(installerPath))
            {
                try { File.Delete(installerPath); } catch { /* Suppress cleanup errors */ }
            }

            if (success)
            {
                await WaitForFileStabilityAsync(destinationPath, "neoforge", ct, progress);

                // Save the NeoForge launch configuration (classpath + main class)
                try
                {
                    await SaveNeoForgeLaunchConfigAsync(destinationPath, ct);
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to save NeoForge launch config: {ex.Message}", "McServerInstaller");
                }

                Logger.Info("NeoForge server installed successfully");
            }
            else
            {
                Logger.Error("NeoForge installer failed");
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error($"NeoForge installation failed: {ex}");
            return false;
        }
    }


}
