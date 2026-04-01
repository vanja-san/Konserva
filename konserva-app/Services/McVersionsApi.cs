using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using Konserva.Utilities;

namespace Konserva.Services;

/// <summary>
/// API для получения версий Minecraft и модов
/// </summary>
public partial class McVersionsApi(HttpClient? httpClient = null) : IMcVersionsApi, IAsyncDisposable
{
    private readonly HttpClient _http = httpClient ?? CreateDefaultHttpClient();
    private string[]? _mcVersions;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _disposed;

    private static HttpClient CreateDefaultHttpClient() => new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders =
        {
            UserAgent = { new("Konserva", "1.0") },
            AcceptEncoding =
            {
                new("gzip", 1.0),
                new("deflate", 1.0)
            }
        }
    };

    /// <summary>
    /// Получение всех версий Minecraft
    /// </summary>
    public async Task<string[]> GetMcVersions(CancellationToken ct = default)
    {
        if (_mcVersions != null)
            return _mcVersions;

        // Ограничиваем время ожидания в 30 секунд
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

        await _cacheLock.WaitAsync(linkedCts.Token);
        try
        {
            if (_mcVersions != null)
                return _mcVersions;

            var response = await GetStringWithDecompressionAsync(
                "https://launchermeta.mojang.com/mc/game/version_manifest.json", linkedCts.Token);

            using var doc = JsonDocument.Parse(response);
            var versions = doc.RootElement.GetProperty("versions")
                .EnumerateArray()
                .Select(v => v.GetProperty("id").GetString()!)
                .ToArray();

            _mcVersions = versions;
            return versions;
        }
        catch
        {
            _mcVersions = [];
            return _mcVersions;
        }
        finally
        {
            try { _cacheLock.Release(); } catch { }
        }
    }

    /// <summary>
    /// Получение версий Forge для версии Minecraft
    /// </summary>
    public async Task<string[]> GetForgeVersions(string mcVersion, CancellationToken ct = default)
    {
        Logger.Info($"Getting Forge versions for MC {mcVersion}...", "McVersionsApi");

        // Forge использует Maven API
        try
        {
            var url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
            Logger.Info($"Fetching Forge from Maven: {url}", "McVersionsApi");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await httpClient.GetStringAsync(url, ct);

            var versions = new HashSet<string>();

            // Ищем версии в Maven metadata.xml
            var matches = System.Text.RegularExpressions.Regex.Matches(response, @"<version>([^<]+)</version>");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var fullVersion = match.Groups[1].Value;
                // Forge версия имеет формат: MC_VERSION-FORGE_VERSION
                // Пример: 1.20.1-47.2.0
                if (fullVersion.StartsWith(mcVersion + "-"))
                {
                    // Извлекаем номер Forge отдельно
                    var forgeVersion = fullVersion[(mcVersion.Length + 1)..];
                    versions.Add(forgeVersion);
                }
            }

            if (versions.Count > 0)
            {
                var result = versions.OrderByDescending(v => v).Take(50).ToArray();
                Logger.Info($"Found {result.Length} Forge versions from Maven", "McVersionsApi");
                return result;
            }

            Logger.Warning($"No Forge versions found for MC {mcVersion} in Maven", "McVersionsApi");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Forge versions from Maven: {ex.Message}", "McVersionsApi");
        }

        Logger.Warning("Returning fallback versions: latest, recommended", "McVersionsApi");
        return ["latest", "recommended"];
    }

    /// <summary>
    /// Получение версий Fabric для версии Minecraft
    /// </summary>
    public async Task<string[]> GetFabricVersions(string mcVersion, CancellationToken ct = default)
    {
        try
        {
            var response = await GetStringWithDecompressionAsync(
                $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}", ct);

            using var doc = JsonDocument.Parse(response);
            var array = doc.RootElement.EnumerateArray();

            var versions = new List<string>();
            foreach (var item in array)
            {
                if (item.TryGetProperty("loader", out var loaderObj) &&
                    loaderObj.TryGetProperty("version", out var versionProp))
                {
                    versions.Add(versionProp.GetString()!);
                }
                else if (item.TryGetProperty("version", out var vp))
                {
                    versions.Add(vp.GetString()!);
                }
            }

            var result = versions.Take(50).ToArray();
            return result.Length > 0 ? result : ["latest"];
        }
        catch
        {
            return ["latest"];
        }
    }

    /// <summary>
    /// Получение версий NeoForge
    /// </summary>
    public async Task<string[]> GetNeoForgeVersions(string mcVersion, CancellationToken ct = default)
    {
        Logger.Info($"Getting NeoForge versions for MC {mcVersion}...", "McVersionsApi");

        // NeoForge использует Maven API (как и Forge)
        // Репозитории: https://maven.neoforged.net
        // Альтернативный: https://maven.creeperhost.net
        var mavenUrls = new[]
        {
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml",
            "https://maven.creeperhost.net/neoforged/neoforge/maven-metadata.xml"
        };

        foreach (var url in mavenUrls)
        {
            try
            {
                Logger.Info($"Fetching NeoForge from: {url}", "McVersionsApi");

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var response = await httpClient.GetStringAsync(url, ct);

                Logger.Info($"Got NeoForge response: {response.Length} bytes", "McVersionsApi");

                var versions = new List<string>();

                // Ищем версии в Maven metadata.xml
                var matches = System.Text.RegularExpressions.Regex.Matches(response, @"<version>([^<]+)</version>");
                Logger.Info($"Found {matches.Count} total NeoForge versions in XML", "McVersionsApi");

                // Преобразуем MC версию в формат NeoForge: 1.21.11 -> 21.11
                var neoForgeMcVersion = ExtractNeoForgeMcVersion(mcVersion);
                Logger.Info($"Looking for NeoForge versions for MC {mcVersion} (NeoForge format: {neoForgeMcVersion})", "McVersionsApi");

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var fullVersion = match.Groups[1].Value;

                    // NeoForge версия имеет формат: MAJOR.MINOR.PATCH[-суффикс]
                    // Пример: 21.1.0-beta (для MC 1.21.1) или 21.11.9 (для MC 1.21.11)
                    if (fullVersion.StartsWith(neoForgeMcVersion + ".") || fullVersion.StartsWith(neoForgeMcVersion + "-"))
                    {
                        // Сохраняем полную версию (например, "21.11.9" или "21.11.0-beta")
                        versions.Add(fullVersion);
                        Logger.Info($"Added NeoForge version: {fullVersion}", "McVersionsApi");
                    }
                }

                if (versions.Count > 0)
                {
                    var result = versions.OrderByDescending(v => v).Take(50).ToArray();
                    Logger.Info($"Found {result.Length} NeoForge versions for MC {mcVersion} (from {url})", "McVersionsApi");
                    return result;
                }

                Logger.Warning($"No NeoForge versions found for MC {mcVersion} from {url}", "McVersionsApi");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to get NeoForge versions from {url}: {ex.Message}", "McVersionsApi");
                // Пробуем следующий репозиторий
            }
        }

        Logger.Warning($"All NeoForge mirrors failed for MC {mcVersion}", "McVersionsApi");
        return ["latest"];
    }

    /// <summary>
    /// Преобразование версии Minecraft в формат NeoForge
    /// 1.21.11 -> 21.11, 1.20.1 -> 20.1, 1.21.1 -> 21.1
    /// </summary>
    private static string ExtractNeoForgeMcVersion(string mcVersion)
    {
        Logger.Info($"ExtractNeoForgeMcVersion: input={mcVersion}", "McVersionsApi");

        // Убираем префикс "1."
        if (mcVersion.StartsWith("1."))
        {
            var result = mcVersion[2..];
            Logger.Info($"ExtractNeoForgeMcVersion: result={result}", "McVersionsApi");
            return result;
        }

        Logger.Info($"ExtractNeoForgeMcVersion: no prefix, result={mcVersion}", "McVersionsApi");
        return mcVersion;
    }

    /// <summary>
    /// Получение версий Quilt
    /// </summary>
    public async Task<string[]> GetQuiltVersions(string mcVersion, CancellationToken ct = default)
    {
        try
        {
            // Quilt API v3
            var url = $"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}";

            Logger.Info($"Fetching Quilt from: {url}", "McVersionsApi");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await httpClient.GetStringAsync(url, ct);

            using var doc = JsonDocument.Parse(response);
            var array = doc.RootElement.EnumerateArray();

            var versions = new List<string>();
            foreach (var item in array)
            {
                // Quilt API v3: версия находится внутри объекта "loader"
                if (item.TryGetProperty("loader", out var loaderObj))
                {
                    if (loaderObj.TryGetProperty("version", out var versionProp))
                    {
                        versions.Add(versionProp.GetString()!);
                    }
                    // Иногда используется "maven" для версии
                    else if (loaderObj.TryGetProperty("maven", out var mavenProp))
                    {
                        versions.Add(mavenProp.GetString()!);
                    }
                }
                // Fallback на прямое поле version
                else if (item.TryGetProperty("version", out var vp))
                {
                    versions.Add(vp.GetString()!);
                }
            }

            // Сортируем: сначала стабильные (без -beta), потом бета
            var stableVersions = versions
                .Where(v => !v.Contains("-beta", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v);

            var betaVersions = versions
                .Where(v => v.Contains("-beta", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(v => v);

            var sortedVersions = stableVersions.Concat(betaVersions).Take(50).ToArray();

            Logger.Info($"Found {sortedVersions.Length} Quilt versions", "McVersionsApi");
            return sortedVersions.Length > 0 ? sortedVersions : ["latest"];
        }
        catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 404 означает, что эта версия Minecraft не поддерживается Quilt
            Logger.Warning($"Quilt does not support MC {mcVersion} (404 Not Found)", "McVersionsApi");
            return [];
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Quilt versions: {ex.Message}", "McVersionsApi");
            return ["latest"];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _http.Dispose();
        _cacheLock.Dispose();
        _disposed = true;

        await ValueTask.CompletedTask;
    }

    /// <summary>
    /// Получение строки с поддержкой gzip/deflate
    /// </summary>
    public async Task<string> GetStringWithDecompressionAsync(string url, CancellationToken ct = default)
    {
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var decompressedStream = GetDecompressedStream(stream, response.Content.Headers.ContentEncoding);
        using var reader = new StreamReader(decompressedStream);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Распаковка потока в зависимости от gzip/deflate
    /// </summary>
    private static Stream GetDecompressedStream(Stream compressedStream, ICollection<string> contentEncoding)
    {
        var encoding = contentEncoding.FirstOrDefault()?.ToLowerInvariant();

        if (encoding == "gzip")
            return new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);

        if (encoding == "deflate")
            return new System.IO.Compression.DeflateStream(compressedStream, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);

        return compressedStream;
    }
}