namespace Konserva.Services;

/// <summary>
/// Загрузка файлов модов напрямую с CDN репозитория.
/// </summary>
public interface IModFileDownloader
{
    Task DownloadAsync(string url, string destinationPath, CancellationToken ct = default);
}
