namespace Konserva.Services;

/// <summary>
/// Downloading of mod files directly from the repository CDN.
/// </summary>
public interface IModFileDownloader
{
    Task DownloadAsync(string url, string destinationPath, CancellationToken ct = default);
}
