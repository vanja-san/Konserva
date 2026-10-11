using Konserva.Utilities;

namespace Konserva.Services;

/// <summary>
/// Downloading the Vanilla Minecraft server and resolving its download URL
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Get the download URL for a vanilla server
    /// </summary>
    public async Task<string?> GetVanillaServerDownloadUrl(string version, CancellationToken ct = default)
    {
        try
        {
            Logger.Info($"Getting Vanilla server URL for version {version}", "McServerInstaller");

            var manifest = await GetVersionManifest(ct);
            var versionEntry = manifest.FirstOrDefault(v => v.Id == version);
            if (versionEntry == null)
            {
                Logger.Warning($"Version {version} not found in manifest", "McServerInstaller");
                return null;
            }

            Logger.Info($"Found version entry, fetching from {versionEntry.Url}", "McServerInstaller");

            using var doc = await FetchJsonAsync(versionEntry.Url, ct);

            var downloads = doc.RootElement.GetProperty("downloads");
            if (downloads.TryGetProperty("server", out var server))
            {
                var url = server.GetProperty("url").GetString();
                Logger.Info($"Got server download URL: {url}", "McServerInstaller");
                return url;
            }

            Logger.Warning($"No 'server' download for version {version}", "McServerInstaller");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get Vanilla server URL: {ex.Message}", ex, "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Download a vanilla server
    /// </summary>
    public async Task<bool> DownloadVanillaServer(string version, string destinationPath,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Logger.Info($"Downloading Vanilla server for version {version}", "McServerInstaller");

        var url = await GetVanillaServerDownloadUrl(version, ct);
        if (string.IsNullOrEmpty(url))
        {
            Logger.Error($"Failed to get download URL for Vanilla {version}", null, "McServerInstaller");
            return false;
        }

        Logger.Info($"Downloading from: {url}", "McServerInstaller");

        var (success, error) = await DownloadFile(url, destinationPath, "server.jar", progress, ct);

        if (success)
        {
            Logger.Info($"Successfully downloaded server.jar for {version}", "McServerInstaller");
        }
        else
        {
            Logger.Error($"Failed to download: {error}", null, "McServerInstaller");
        }

        return success;
    }
}
