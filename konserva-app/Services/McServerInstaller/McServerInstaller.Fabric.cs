using Konserva.Localization;
using Konserva.Utilities;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Konserva.Services;

/// <summary>
    /// Fabric server installation: fetching information, downloading, and installing
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Get information about the latest Fabric installer version
    /// </summary>
    public async Task<(string url, string version)?> GetFabricInstallerInfo(CancellationToken ct = default)
    {
        return await RetryHelper.RetryAsync(async () =>
        {
            Logger.Info("Fetching Fabric installer info");
            using var doc = await FetchJsonAsync("https://meta.fabricmc.net/v2/versions/installer", ct);
            var array = doc.RootElement.EnumerateArray();
            if (!array.MoveNext())
            {
                Logger.Error("Fabric API returned empty array");
                return ((string url, string version)?)null;
            }

            var latest = array.Current;
            var url = latest.GetProperty("url").GetString()!;
            var version = latest.GetProperty("version").GetString()!;
            Logger.Info($"Found Fabric installer: version={version}");
            return (url, version);
        }, operationName: "GetFabricInstallerInfo", ct: ct);
    }

    /// <summary>
    /// Install a Fabric server automatically
    /// </summary>
    public async Task<bool> InstallFabricServer(string mcVersion, string loaderVersion,
        string destinationPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Logger.Info($"Installing Fabric server: MC={mcVersion}, Loader={loaderVersion}, Path={destinationPath}");

        try
        {
            Directory.CreateDirectory(destinationPath);
            Logger.Info($"Created directory: {destinationPath}");

            // The loader version comes from the UI — use it as is
            var selectedLoaderVersion = loaderVersion;
            if (string.IsNullOrEmpty(selectedLoaderVersion) || selectedLoaderVersion == "latest")
            {
                // If no specific version was provided, fetch the latest one
                Logger.Info("No specific loader version provided, fetching latest...");
                var loaderVersions = await GetFabricLoaderVersions(mcVersion, ct);
                if (loaderVersions == null || loaderVersions.Length == 0)
                {
                    Logger.Error("No Fabric loader versions found");
                    return false;
                }
                selectedLoaderVersion = loaderVersions[0];
            }

            Logger.Info($"Using Fabric loader version: {selectedLoaderVersion}");

            // Get the Fabric installer version
            Logger.Info("Getting Fabric installer version...");
            var installerVersion = await RetryHelper.RetryAsync(
                () => GetFabricInstallerVersion(ct),
                operationName: "GetFabricInstallerVersion", ct: ct);

            // Fallback if the installer version could not be obtained
            if (string.IsNullOrEmpty(installerVersion))
            {
                Logger.Warning("Failed to get Fabric installer version, using fallback version 1.1.1");
                installerVersion = "1.1.1";
            }

            // The installer version in the URL is mandatory — meta.fabricmc.net requires it
            var primaryUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{selectedLoaderVersion}/{installerVersion}/server/jar";
            var fallbackUrl = $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{mcVersion}/{selectedLoaderVersion}/{installerVersion}/server/jar";

            // Try the official API first, fall back to BMCLAPI on error
            var urlsToTry = new[] { primaryUrl, fallbackUrl };

            foreach (var url in urlsToTry)
            {
                Logger.Info($"Downloading Fabric server from: {url}");
                progress?.Report(LocalizationManager.Get("Installer_DownloadingServer"));

                var (success, error) = await DownloadFile(url, destinationPath, "fabric-server-launch.jar", progress, ct);

                if (success)
                {
                    Logger.Info("Fabric server downloaded successfully");

                    // Create eula.txt
                    progress?.Report(LocalizationManager.Get("Installer_CreatingConfig"));
                    CreateEula(destinationPath);

                    Logger.Info("Fabric server installation completed");
                    progress?.Report(LocalizationManager.Get("Installer_Finishing"));
                    return true;
                }

                Logger.Warning($"Primary Fabric URL failed, trying BMCLAPI fallback: {error}");
                progress?.Report(LocalizationManager.Get("Installer_ChangingSource"));
            }

            Logger.Error("All Fabric download URLs failed");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"Fabric installation failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Get Fabric loader versions for a Minecraft version
    /// </summary>
    private async Task<string[]?> GetFabricLoaderVersions(string mcVersion, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}";
            Logger.Info($"GET {url}");

            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            requestCts.CancelAfter(TimeSpan.FromSeconds(30));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCts.Token);
            response.EnsureSuccessStatusCode();

            var responseBytes = await response.Content.ReadAsByteArrayAsync(requestCts.Token);
            Logger.Info($"Response: Success ({responseBytes.Length} bytes)");

            // Unpack GZip if necessary
            string responseContent;
            if (responseBytes.Length >= 2 && responseBytes[0] == 0x1F && responseBytes[1] == 0x8B)
            {
                Logger.Info("Detected GZip compression, decompressing...");
                using var ms = new MemoryStream(responseBytes);
                using var gzipStream = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(gzipStream, Encoding.UTF8);
                responseContent = await reader.ReadToEndAsync(ct);
                Logger.Info($"Decompressed to {responseContent.Length} bytes");
            }
            else
            {
                responseContent = Encoding.UTF8.GetString(responseBytes);
            }

            using var doc = JsonDocument.Parse(responseContent);

            // Log the structure for debugging
            var array = doc.RootElement.EnumerateArray();
            if (array.MoveNext())
            {
                var firstItem = array.Current;
                Logger.Info($"First item JSON: {firstItem.GetRawText()[..Math.Min(Constants.LogTruncationLength, firstItem.GetRawText().Length)]}...");

                // Fabric API returns the structure: { "loader": { "version": "x.y.z" }, ... }
                // Return all versions
                array = doc.RootElement.EnumerateArray();
                var versions = array.Select(v =>
                {
                    // Try to read loader.version
                    if (v.TryGetProperty("loader", out var loaderObj) &&
                        loaderObj.TryGetProperty("version", out var versionProp))
                    {
                        return versionProp.GetString()!;
                    }
                    // Fallback to version
                    if (v.TryGetProperty("version", out var vp)) return vp.GetString()!;
                    return v.ToString();
                }).ToArray();

                Logger.Info($"Found {versions.Length} Fabric loader versions");
                return versions;
            }

            Logger.Error("Empty response from Fabric API");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get Fabric loader versions: {ex.GetType().Name} - {ex.Message}");
            if (ex.InnerException != null)
            {
                Logger.Error($"Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
            }
            if (ex.StackTrace != null)
            {
                Logger.Error($"StackTrace: {ex.StackTrace}");
            }
            return null;
        }
    }

    /// <summary>
    /// Get the Fabric installer version
    /// </summary>
    private async Task<string?> GetFabricInstallerVersion(CancellationToken ct = default)
    {
        try
        {
            using var doc = await FetchJsonAsync("https://meta.fabricmc.net/v2/versions/installer", ct);
            var array = doc.RootElement.EnumerateArray();
            if (!array.MoveNext())
                return null;
            return array.Current.GetProperty("version").GetString();
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Fabric installer version request canceled (timeout)");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get Fabric installer version: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }
}
