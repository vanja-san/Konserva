using System.IO;
using System.Net.Http;

namespace Konserva.Services;

/// <summary>
/// Скачивает файлы модов потоком, не загружая их целиком в память.
/// </summary>
public sealed class ModFileDownloader : IModFileDownloader
{
    private readonly HttpClient _http;

    public ModFileDownloader(HttpClient httpClient)
    {
        _http = httpClient;
    }

    public async Task DownloadAsync(string url, string destinationPath, CancellationToken ct = default)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content
            .ReadAsStreamAsync(ct)
            .ConfigureAwait(false);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await source.CopyToAsync(destination, ct).ConfigureAwait(false);
    }
}
