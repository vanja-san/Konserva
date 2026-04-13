using Konserva.Utilities;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Konserva.Services;

/// <summary>
/// API для получения версий Minecraft и модов
/// </summary>
public partial class McVersionsApi(HttpClient? httpClient = null) : IMcVersionsApi, IAsyncDisposable
{
    private readonly HttpClient _http = httpClient ?? CreateDefaultHttpClient();
    private string[]? _mcVersions;
    private DateTime _mcVersionsCacheTime;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _disposed;

    [GeneratedRegex(@"<version>([^<]+)</version>")]
    private static partial Regex XmlVersionRegex();

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
        string[]? cached = _mcVersions;
        if (cached != null && (DateTime.UtcNow - _mcVersionsCacheTime) < CacheTtl)
            return cached;

        // Ограничиваем время ожидания в 30 секунд
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(30));

        await _cacheLock.WaitAsync(linkedCts.Token);
        try
        {
            string[]? local = _mcVersions;
            if (local != null)
                return local;

            var response = await GetStringWithDecompressionAsync(
                "https://launchermeta.mojang.com/mc/game/version_manifest.json", linkedCts.Token);

            using var doc = JsonDocument.Parse(response);
            var versions = doc.RootElement.GetProperty("versions")
                .EnumerateArray()
                .Select(v => v.GetProperty("id").GetString()!)
                .ToArray();

            _mcVersions = versions;
            _mcVersionsCacheTime = DateTime.UtcNow;
            return versions;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Minecraft versions: {ex.Message}", "McVersionsApi");
            _mcVersions = [];
            return _mcVersions;
        }
        finally
        {
            try { _cacheLock.Release(); } catch { /* Suppress lock release errors */ }
        }
    }

    /// <summary>
    /// Получение версий Forge для версии Minecraft
    /// </summary>
    public async Task<string[]> GetForgeVersions(string mcVersion, CancellationToken ct = default)
    {
        Logger.Info($"Getting Forge versions for MC {mcVersion}...", "McVersionsApi");

        try
        {
            var url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
            Logger.Info($"Fetching Forge from Maven: {url}", "McVersionsApi");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await _http.GetStringAsync(url, timeoutCts.Token);

            var versions = new HashSet<string>();

            var matches = XmlVersionRegex().Matches(response);
            foreach (Match match in matches)
            {
                var fullVersion = match.Groups[1].Value;
                if (fullVersion.StartsWith(mcVersion + "-"))
                {
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
        catch (OperationCanceledException)
        {
            Logger.Warning($"Timeout getting Forge versions for MC {mcVersion}", "McVersionsApi");
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
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Fabric versions for MC {mcVersion}: {ex.Message}", "McVersionsApi");
            return ["latest"];
        }
    }

    private readonly Dictionary<string, CachedEntry<string[]>> _neoForgeCache = new();
    private readonly SemaphoreSlim _neoForgeCacheLock = new(1, 1);

    private readonly struct CachedEntry<T>(T value, DateTime time)
    {
        public readonly T Value = value;
        public readonly DateTime Time = time;
        public bool IsFresh => (DateTime.UtcNow - Time) < CacheTtl;
    }

    /// <summary>
    /// Получение версий NeoForge
    /// </summary>
    public async Task<string[]> GetNeoForgeVersions(string mcVersion, CancellationToken ct = default)
    {
        Logger.Info($"Getting NeoForge versions for MC {mcVersion}...", "McVersionsApi");

        // Проверяем кэш
        await _neoForgeCacheLock.WaitAsync(ct);
        try
        {
            if (_neoForgeCache.TryGetValue(mcVersion, out var cached) && cached.IsFresh)
            {
                Logger.Info($"Returning cached NeoForge versions for MC {mcVersion}: {cached.Value.Length}", "McVersionsApi");
                return cached.Value;
            }
        }
        finally
        {
            try { _neoForgeCacheLock.Release(); } catch { /* Suppress lock release errors */ }
        }

        // Основной источник: maven.neoforged.net
        var mavenUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

        try
        {
            Logger.Info($"Fetching NeoForge from: {mavenUrl}", "McVersionsApi");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _http.GetStringAsync(mavenUrl, timeoutCts.Token);

            Logger.Info($"Got NeoForge response: {response.Length} bytes", "McVersionsApi");

            var versions = ParseNeoForgeVersions(response, mcVersion);

            if (versions.Count > 0)
            {
                var result = versions.OrderByDescending(v => v).Take(50).ToArray();
                Logger.Info($"Found {result.Length} NeoForge versions for MC {mcVersion}", "McVersionsApi");

                // Кэшируем результат
                await _neoForgeCacheLock.WaitAsync(ct);
                try { _neoForgeCache[mcVersion] = new CachedEntry<string[]>(result, DateTime.UtcNow); }
                finally { try { _neoForgeCacheLock.Release(); } catch { /* Suppress lock release errors */ } }

                return result;
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Warning($"Timeout getting NeoForge versions for MC {mcVersion}", "McVersionsApi");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get NeoForge versions from maven.neoforged.net: {ex.Message}", "McVersionsApi");
        }

        // Fallback: используем список из launchermeta.mojang.com + маппинг версий
        Logger.Info($"Trying fallback: launchermeta for NeoForge MC {mcVersion}", "McVersionsApi");
        try
        {
            var fallbackVersions = await GetNeoForgeFromLauncherMeta(ct);
            if (fallbackVersions.Count > 0)
            {
                var result = fallbackVersions.OrderByDescending(v => v).Take(50).ToArray();
                Logger.Info($"Found {result.Length} NeoForge versions from fallback for MC {mcVersion}", "McVersionsApi");

                await _neoForgeCacheLock.WaitAsync(ct);
                try { _neoForgeCache[mcVersion] = new CachedEntry<string[]>(result, DateTime.UtcNow); }
                finally { try { _neoForgeCacheLock.Release(); } catch { /* Suppress lock release errors */ } }

                return result;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Fallback also failed for NeoForge MC {mcVersion}: {ex.Message}", "McVersionsApi");
        }

        Logger.Warning($"All NeoForge sources failed for MC {mcVersion}", "McVersionsApi");
        return ["latest"];
    }

    private static List<string> ParseNeoForgeVersions(string xml, string mcVersion)
    {
        var versions = new List<string>();
        var matches = XmlVersionRegex().Matches(xml);
        Logger.Info($"Found {matches.Count} total NeoForge versions in XML", "McVersionsApi");

        var neoForgeMcVersion = ExtractNeoForgeMcVersion(mcVersion);
        Logger.Info($"Looking for NeoForge versions for MC {mcVersion} (NeoForge format: {neoForgeMcVersion})", "McVersionsApi");

        foreach (Match match in matches)
        {
            var fullVersion = match.Groups[1].Value;

            if (fullVersion.StartsWith(neoForgeMcVersion + ".") || fullVersion.StartsWith(neoForgeMcVersion + "-"))
            {
                versions.Add(fullVersion);
                Logger.Info($"Added NeoForge version: {fullVersion}", "McVersionsApi");
            }
        }

        return versions;
    }

    /// <summary>
    /// Fallback: получает список версий NeoForge через launchermeta.mojang.com.
    /// NeoForge версии содержатся в version_manifest.json как отдельные записи.
    /// </summary>
    private async Task<List<string>> GetNeoForgeFromLauncherMeta(CancellationToken ct)
    {
        var response = await GetStringWithDecompressionAsync(
            "https://launchermeta.mojang.com/mc/game/version_manifest.json", ct);

        using var doc = JsonDocument.Parse(response);
        var allVersions = doc.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(v => v.GetProperty("id").GetString()!)
            .ToArray();

        // NeoForge версии имеют формат "neoforge-MC_VERSION-NEOFORGE_VERSION"
        // или содержат "neoforge" в type
        var versions = allVersions
            .Where(v => v.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return versions;
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
            var url = $"https://meta.quiltmc.org/v3/versions/loader/{mcVersion}";
            Logger.Info($"Fetching Quilt from: {url}", "McVersionsApi");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _http.GetStringAsync(url, timeoutCts.Token);

            using var doc = JsonDocument.Parse(response);
            var array = doc.RootElement.EnumerateArray();

            var versions = new List<string>();
            foreach (var item in array)
            {
                if (item.TryGetProperty("loader", out var loaderObj))
                {
                    if (loaderObj.TryGetProperty("version", out var versionProp))
                    {
                        versions.Add(versionProp.GetString()!);
                    }
                    else if (loaderObj.TryGetProperty("maven", out var mavenProp))
                    {
                        versions.Add(mavenProp.GetString()!);
                    }
                }
                else if (item.TryGetProperty("version", out var vp))
                {
                    versions.Add(vp.GetString()!);
                }
            }

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
            Logger.Warning($"Quilt does not support MC {mcVersion} (404 Not Found)", "McVersionsApi");
            return [];
        }
        catch (OperationCanceledException)
        {
            Logger.Warning($"Timeout getting Quilt versions for MC {mcVersion}", "McVersionsApi");
            return ["latest"];
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Quilt versions for MC {mcVersion}: {ex.Message}", "McVersionsApi");
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
        using var decompressedStream = StreamUtilities.GetDecompressedStream(stream, response.Content.Headers.ContentEncoding);
        using var reader = new StreamReader(decompressedStream);
        return await reader.ReadToEndAsync(ct);
    }
}