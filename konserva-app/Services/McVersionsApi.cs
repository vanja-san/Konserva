using System.Net;
using System.Net.Http;
using Konserva.Utilities;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static Konserva.Models.ApiUrls;

namespace Konserva.Services;

/// <summary>
/// API для получения версий Minecraft и модов
/// </summary>
public partial class McVersionsApi : IMcVersionsApi, IAsyncDisposable
{
    private readonly HttpClient _http;
    private string[]? _mcVersions;
    private DateTime _mcVersionsCacheTime;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan FileCacheTtl = TimeSpan.FromDays(7);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private bool _disposed;
    private VersionsCache? _fileCache;

    private static readonly string CacheFolder = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Cache");

    private static readonly string CacheFilePath = Path.Combine(CacheFolder, "versions_cache.json");

    [GeneratedRegex(@"<version>([^<]+)</version>")]
    private static partial Regex XmlVersionRegex();

    public McVersionsApi(HttpClient httpClient)
    {
        _http = httpClient;
        _ = LoadFileCacheAsync();
    }

    /// <summary>
    /// Получение всех версий Minecraft
    /// </summary>
    public async Task<string[]> GetMcVersions(CancellationToken ct = default)
    {
        // Пытаемся получить сеть (всегда проверяем на новые версии)
        string[]? networkVersions = null;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(10)); // Короткий таймаут

            var response = await GetStringWithDecompressionAsync(
                MojangManifest, linkedCts.Token);

            using var doc = JsonDocument.Parse(response);
            networkVersions = doc.RootElement.GetProperty("versions")
                .EnumerateArray()
                .Select(v => v.GetProperty("id").GetString()!)
                .ToArray();

            // Сохраняем в файл кэш
            SaveMcVersionsToFileCache(networkVersions);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to fetch MC versions from network: {ex.Message}", "McVersionsApi");
        }

        if (networkVersions != null)
        {
            _mcVersions = networkVersions;
            _mcVersionsCacheTime = SystemTime.UtcNow;
            return networkVersions;
        }

        // Сеть недоступна - используем кэш
        var cachedVersions = GetMcVersionsFromFileCache();
        if (cachedVersions != null)
        {
            _mcVersions = cachedVersions;
            _mcVersionsCacheTime = SystemTime.UtcNow;
            return cachedVersions;
        }

        // Если совсем ничего нет, возвращаем последние известные версии
        string[]? memoryCached = _mcVersions;
        if (memoryCached != null)
            return memoryCached;

        // Финальный fallback - пустой массив
        _mcVersions = [];
        return _mcVersions;
    }

    /// <summary>
    /// Получение версий Forge для версии Minecraft
    /// </summary>
    public async Task<string[]> GetForgeVersions(string mcVersion, CancellationToken ct = default)
    {
        // Сначала пробуем загрузить из кэша
        var cachedVersions = GetFromFileCache("forge", mcVersion);
        if (cachedVersions != null)
        {
            Logger.Info($"Returning cached Forge versions for MC {mcVersion}: {cachedVersions.Length}", "McVersionsApi");
            return cachedVersions;
        }

        Logger.Info($"Getting Forge versions for MC {mcVersion}...", "McVersionsApi");

        try
        {
            var url = $"{ForgeMaven}/net/minecraftforge/forge/maven-metadata.xml";
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

                // Сохраняем в кэш
                SaveToFileCache("forge", mcVersion, result);

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
        // Сначала пробуем загрузить из кэша
        var cachedVersions = GetFromFileCache("fabric", mcVersion);
        if (cachedVersions != null)
        {
            Logger.Info($"Returning cached Fabric versions for MC {mcVersion}: {cachedVersions.Length}", "McVersionsApi");
            return cachedVersions;
        }

        try
        {
            var response = await GetStringWithDecompressionAsync(
                $"{FabricVersionsLoader}/{mcVersion}", ct);

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
            if (result.Length > 0)
            {
                SaveToFileCache("fabric", mcVersion, result);
                return result;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Fabric versions for MC {mcVersion}: {ex.Message}", "McVersionsApi");
        }

        return ["latest"];
    }

    private readonly Dictionary<string, CachedEntry<string[]>> _neoForgeCache = new();
    private readonly SemaphoreSlim _neoForgeCacheLock = new(1, 1);

    private readonly struct CachedEntry<T>(T value, DateTime time)
    {
        public readonly T Value = value;
        public readonly DateTime Time = time;
        public bool IsFresh => (SystemTime.UtcNow - Time) < CacheTtl;
    }

    /// <summary>
    /// Получение версий NeoForge
    /// </summary>
    public async Task<string[]> GetNeoForgeVersions(string mcVersion, CancellationToken ct = default)
    {
        // Сначала пробуем загрузить из кэша
        var cachedVersions = GetFromFileCache("neoforge", mcVersion);
        if (cachedVersions != null)
        {
            Logger.Info($"Returning cached NeoForge versions for MC {mcVersion}: {cachedVersions.Length}", "McVersionsApi");
            return cachedVersions;
        }

        Logger.Info($"Getting NeoForge versions for MC {mcVersion}...", "McVersionsApi");

        // Проверяем память
        await _neoForgeCacheLock.WaitAsync(ct);
        try
        {
            if (_neoForgeCache.TryGetValue(mcVersion, out var cached) && cached.IsFresh)
            {
                Logger.Info($"Returning memory cached NeoForge versions for MC {mcVersion}: {cached.Value.Length}", "McVersionsApi");
                return cached.Value;
            }
        }
        finally
        {
            try { _neoForgeCacheLock.Release(); }
                catch (ObjectDisposedException) { /* Disposed during shutdown */ }
                catch (SemaphoreFullException) { /* Already released */ }
        }

        // Основной источник: maven.neoforged.net
        var mavenUrl = NeoForgeMetadata;

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

                // Сохраняем в кэш
                SaveToFileCache("neoforge", mcVersion, result);

                // Кэшируем в память
                await _neoForgeCacheLock.WaitAsync(ct);
                try { _neoForgeCache[mcVersion] = new CachedEntry<string[]>(result, SystemTime.UtcNow); }
                finally { try { _neoForgeCacheLock.Release(); }
                    catch (ObjectDisposedException) { /* Disposed during shutdown */ }
                    catch (SemaphoreFullException) { /* Already released */ } }

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

                SaveToFileCache("neoforge", mcVersion, result);

                await _neoForgeCacheLock.WaitAsync(ct);
                try { _neoForgeCache[mcVersion] = new CachedEntry<string[]>(result, SystemTime.UtcNow); }
                finally { try { _neoForgeCacheLock.Release(); }
                    catch (ObjectDisposedException) { /* Disposed during shutdown */ }
                    catch (SemaphoreFullException) { /* Already released */ } }

                return result;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Fallback also failed for NeoForge MC {mcVersion}: {ex.Message}", "McVersionsApi");
        }

        Logger.Warning("All NeoForge sources failed for MC {mcVersion}", "McVersionsApi");
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
            MojangManifest, ct);

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
        // Сначала пробуем загрузить из кэша
        var cachedVersions = GetFromFileCache("quilt", mcVersion);
        if (cachedVersions != null)
        {
            Logger.Info($"Returning cached Quilt versions for MC {mcVersion}: {cachedVersions.Length}", "McVersionsApi");
            return cachedVersions;
        }

        try
        {
            var url = $"{QuiltVersionsLoader}/{mcVersion}";
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

            if (sortedVersions.Length > 0)
            {
                SaveToFileCache("quilt", mcVersion, sortedVersions);
                return sortedVersions;
            }

            return ["latest"];
        }
        catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.Warning($"Quilt does not support MC {mcVersion} (404 Not Found)", "McVersionsApi");
            return ["latest"];
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
        return await _http.GetStringAsync(url, ct);
    }

    #region File Cache

    private async Task LoadFileCacheAsync()
    {
        try
        {
            if (!File.Exists(CacheFilePath))
                return;

            var json = await File.ReadAllTextAsync(CacheFilePath);
            _fileCache = JsonSerializer.Deserialize<VersionsCache>(json);
            Logger.Info($"Loaded file cache: {_fileCache?.LastUpdated}", "McVersionsApi");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load file cache: {ex.Message}", "McVersionsApi");
        }
    }

    private async Task SaveFileCacheAsync()
    {
        if (_fileCache == null)
            return;

        await _fileLock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_fileCache);
            await File.WriteAllTextAsync(CacheFilePath, json);
            Logger.Info("File cache saved", "McVersionsApi");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to save file cache: {ex.Message}", "McVersionsApi");
        }
        finally
        {
            try { _fileLock.Release(); }
                catch (ObjectDisposedException) { /* Disposed during shutdown */ }
                catch (SemaphoreFullException) { Logger.Warning("File lock already released", "McVersionsApi"); }
        }
    }

    private string[]? GetFromFileCache(string loader, string mcVersion)
    {
        if (_fileCache == null)
            return null;

        var cacheTime = _fileCache.LastUpdated;
        if (SystemTime.UtcNow - cacheTime > FileCacheTtl)
            return null;

        var loaderCache = loader.ToLower() switch
        {
            "forge" => _fileCache.Forge,
            "neoforge" => _fileCache.NeoForge,
            "fabric" => _fileCache.Fabric,
            "quilt" => _fileCache.Quilt,
            _ => null
        };

        if (loaderCache == null)
            return null;

        return loaderCache.TryGetValue(mcVersion, out var versions) ? versions : null;
    }

    private void SaveMcVersionsToFileCache(string[] versions)
    {
        _fileCache ??= new VersionsCache();
        _fileCache.McVersions = versions;
        _fileCache.LastUpdated = SystemTime.UtcNow;
        _ = SaveFileCacheAsync();
    }

    private string[]? GetMcVersionsFromFileCache()
    {
        return _fileCache?.McVersions;
    }

    private void SaveToFileCache(string loader, string mcVersion, string[] versions)
    {
        _fileCache ??= new VersionsCache();
        _fileCache.LastUpdated = SystemTime.UtcNow;

        var loaderCache = loader.ToLower() switch
        {
            "forge" => _fileCache.Forge ??= new Dictionary<string, string[]>(),
            "neoforge" => _fileCache.NeoForge ??= new Dictionary<string, string[]>(),
            "fabric" => _fileCache.Fabric ??= new Dictionary<string, string[]>(),
            "quilt" => _fileCache.Quilt ??= new Dictionary<string, string[]>(),
            _ => null
        };

        if (loaderCache != null)
        {
            loaderCache[mcVersion] = versions;
            _ = SaveFileCacheAsync();
        }
    }

    #endregion

    #region Cache Classes

    public class VersionsCache
    {
        [JsonPropertyName("mcVersions")]
        public string[]? McVersions { get; set; }

        [JsonPropertyName("forge")]
        public Dictionary<string, string[]>? Forge { get; set; }

        [JsonPropertyName("neoforge")]
        public Dictionary<string, string[]>? NeoForge { get; set; }

        [JsonPropertyName("fabric")]
        public Dictionary<string, string[]>? Fabric { get; set; }

        [JsonPropertyName("quilt")]
        public Dictionary<string, string[]>? Quilt { get; set; }

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }
    }

    #endregion
}