using Konserva.Utilities;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static Konserva.Models.ApiUrls;

namespace Konserva.Services;

/// <summary>
/// API для получения версий Minecraft и модов
/// </summary>
public partial class McVersionsApi : IMcVersionsApi, IAsyncDisposable, IDisposable
{
    private readonly HttpClient _http;
    private string[]? _mcVersions;
    private DateTime _mcVersionsCacheTime;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan FileCacheTtl = TimeSpan.FromDays(7);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private bool _disposed;
    private Task? _fileCacheLoadTask;
    private VersionsCache? _fileCache;

    // Кэши для Quilt supported versions (загружаются один раз, persist в singleton)
    private Task<HashSet<string>>? _quiltSupportedTask;
    private Task<HashSet<string>>? _paperApiVersionsTask;

    private readonly string _cacheFolder;
    private readonly string _cacheFilePath;
    private readonly IConfigService? _configService;

    [GeneratedRegex(@"<version>([^<]+)</version>")]
    private static partial Regex XmlVersionRegex();

    public McVersionsApi(HttpClient httpClient, IConfigService? configService = null)
        : this(httpClient, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Servers"), configService)
    {
    }

    /// <summary>
    /// Конструктор с указанием папки кэша (для тестов)
    /// </summary>
    internal McVersionsApi(HttpClient httpClient, string cacheFolder, IConfigService? configService = null)
    {
        _http = httpClient;
        _configService = configService;
        _cacheFolder = cacheFolder;
        _cacheFilePath = Path.Combine(_cacheFolder, "versions_cache.json");
        _fileCacheLoadTask = LoadFileCacheAsync();
    }

    /// <summary>
    /// Выбранный источник загрузки (из конфига или VanillaApi по умолчанию)
    /// </summary>
    private string DownloadSource => _configService?.GetConfig().DownloadSource ?? "VanillaApi";

    /// <summary>
    /// Получение всех версий Minecraft
    /// </summary>
    public async Task<string[]> GetMcVersions(CancellationToken ct = default)
    {
        // Пытаемся получить сеть (всегда проверяем на новые версии)
        string[]? networkVersions = null;

        // Выбираем URL в зависимости от источника загрузки
        var manifestUrl = DownloadSource == "BMCLAPI" ? BmclapiManifest : MojangManifest;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(10)); // Короткий таймаут

            var response = await GetStringWithDecompressionAsync(
                manifestUrl, linkedCts.Token);

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
        var useBmclapiFirst = DownloadSource == "BMCLAPI";

        return await FetchLoaderVersionsAsync("forge", mcVersion, ["latest", "recommended"], ct,
            useBmclapiFirst
                ? ct2 => TryGetForgeFromBmclapi(mcVersion, ct2)
                : ct2 => TryGetForgeFromMaven(mcVersion, ct2),
            useBmclapiFirst
                ? ct2 => TryGetForgeFromMaven(mcVersion, ct2)
                : ct2 => TryGetForgeFromBmclapi(mcVersion, ct2));
    }

    /// <summary>
    /// Попытка получить версии Forge из Maven
    /// </summary>
    private async Task<string[]?> TryGetForgeFromMaven(string mcVersion, CancellationToken ct)
    {
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

        return null;
    }

    /// <summary>
    /// Попытка получить версии Forge из BMCLAPI
    /// </summary>
    private async Task<string[]?> TryGetForgeFromBmclapi(string mcVersion, CancellationToken ct)
    {
        try
        {
            var bmclapiUrl = $"{ForgeBmclapiMinecraft}/{mcVersion}";
            Logger.Info($"Fetching Forge from BMCLAPI: {bmclapiUrl}", "McVersionsApi");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _http.GetStringAsync(bmclapiUrl, timeoutCts.Token);
            using var doc = JsonDocument.Parse(response);
            var entries = doc.RootElement.EnumerateArray().ToArray();

            if (entries.Length > 0)
            {
                var versions = entries
                    .Select(e => e.GetProperty("version").GetString()!)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .OrderByDescending(v => v)
                    .Take(50)
                    .ToArray();

                if (versions.Length > 0)
                {
                    Logger.Info($"Found {versions.Length} Forge versions from BMCLAPI for MC {mcVersion}", "McVersionsApi");
                    SaveToFileCache("forge", mcVersion, versions);
                    return versions;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Warning($"Timeout getting Forge versions from BMCLAPI for MC {mcVersion}", "McVersionsApi");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Forge versions from BMCLAPI: {ex.Message}", "McVersionsApi");
        }

        return null;
    }

    /// <summary>
    /// Получение версий Fabric для версии Minecraft
    /// </summary>
    public async Task<string[]> GetFabricVersions(string mcVersion, CancellationToken ct = default)
    {
        var primaryUrl = DownloadSource == "BMCLAPI" ? BmclapiFabricVersionsLoader : FabricVersionsLoader;

        return await FetchLoaderVersionsAsync("fabric", mcVersion, ["latest"], ct,
            ct2 => TryGetFabricFromSource(primaryUrl, mcVersion, ct2),
            DownloadSource == "BMCLAPI"
                ? ct2 => TryGetFabricFromSource(FabricVersionsLoader, mcVersion, ct2)
                : null);
    }

    private async Task<string[]?> TryGetFabricFromSource(string baseUrl, string mcVersion, CancellationToken ct)
    {
        try
        {
            var url = $"{baseUrl}/{mcVersion}";
            var response = await GetStringWithDecompressionAsync(url, ct);

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
                return result;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Fabric versions from {baseUrl} for MC {mcVersion}: {ex.Message}", "McVersionsApi");
        }

        return null;
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
    /// Получение версий NeoForge для указанной версии Minecraft.
    /// Основной источник: BMCLAPI (JSON). Fallback: Maven XML и launchermeta.
    /// </summary>
    public async Task<string[]> GetNeoForgeVersions(string mcVersion, CancellationToken ct = default)
    {
        // Проверяем memory-кэш NeoForge
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

        var result = await FetchLoaderVersionsAsync("neoforge", mcVersion, ["latest"], ct,
            ct2 => TryGetNeoForgeFromBmclapi(mcVersion, ct2),
            ct2 => TryGetNeoForgeFromMaven(mcVersion, ct2),
            ct2 => TryGetNeoForgeFromLauncherMeta(ct2));

        // Сохраняем в memory-кэш если получили real данные (не fallback)
        if (result.Length > 1 || (result.Length == 1 && result[0] != "latest"))
            CacheInMemory(mcVersion, result, ct);

        return result;
    }

    private async Task<string[]?> TryGetNeoForgeFromBmclapi(string mcVersion, CancellationToken ct)
    {
        try
        {
            var url = $"{NeoForgeBmclapiList}/{mcVersion}";
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _http.GetStringAsync(url, timeoutCts.Token);
            using var doc = JsonDocument.Parse(response);
            var entries = doc.RootElement.EnumerateArray().ToArray();

            if (entries.Length == 0) return null;

            var versions = entries
                .Select(e => e.GetProperty("version").GetString()!)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .OrderByDescending(v => v)
                .Take(50)
                .ToArray();

            return versions.Length > 0 ? versions : null;
        }
        catch (OperationCanceledException)
        {
            Logger.Warning($"Timeout getting NeoForge versions from BMCLAPI for MC {mcVersion}", "McVersionsApi");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get NeoForge versions from BMCLAPI: {ex.Message}", "McVersionsApi");
            return null;
        }
    }

    private async Task<string[]?> TryGetNeoForgeFromMaven(string mcVersion, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _http.GetStringAsync(NeoForgeMetadata, timeoutCts.Token);
            var mavenVersions = ParseNeoForgeVersionsFromXml(response, mcVersion);

            if (mavenVersions.Count == 0) return null;

            var result = mavenVersions.OrderByDescending(v => v).Take(50).ToArray();
            return result.Length > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Maven XML fallback failed: {ex.Message}", "McVersionsApi");
            return null;
        }
    }

    private async Task<string[]?> TryGetNeoForgeFromLauncherMeta(CancellationToken ct)
    {
        try
        {
            var fallbackVersions = await GetNeoForgeFromLauncherMeta(ct);
            if (fallbackVersions.Count == 0) return null;

            var result = fallbackVersions.OrderByDescending(v => v).Take(50).ToArray();
            return result.Length > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Launchermeta fallback failed: {ex.Message}", "McVersionsApi");
            return null;
        }
    }

    /// <summary>
    /// Парсинг NeoForge версий из Maven XML maven-metadata.xml.
    /// </summary>
    private static List<string> ParseNeoForgeVersionsFromXml(string xml, string mcVersion)
    {
        var versions = new List<string>();
        var matches = XmlVersionRegex().Matches(xml);

        // NeoForge версии в Maven имеют формат XX.Y.Z (без префикса "1.")
        // MC 1.21.1 → ищем версии, начинающиеся с "21.1."
        // MC 1.20.1 → ищем версии, начинающиеся с "20.1."
        var neoForgePrefix = mcVersion.StartsWith("1.") ? mcVersion[2..] : mcVersion;

        foreach (Match match in matches)
        {
            var fullVersion = match.Groups[1].Value;

            if (fullVersion.StartsWith(neoForgePrefix + ".") || fullVersion.StartsWith(neoForgePrefix + "-"))
            {
                versions.Add(fullVersion);
            }
        }

        return versions;
    }

    /// <summary>
    /// Fallback: получает список версий NeoForge через launchermeta.mojang.com.
    /// </summary>
    private async Task<List<string>> GetNeoForgeFromLauncherMeta(CancellationToken ct)
    {
        var response = await GetStringWithDecompressionAsync(MojangManifest, ct);

        using var doc = JsonDocument.Parse(response);
        var allVersions = doc.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(v => v.GetProperty("id").GetString()!)
            .ToArray();

        var versions = allVersions
            .Where(v => v.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return versions;
    }

    /// <summary>
    /// Сохранить результат в memory-кэш NeoForge.
    /// </summary>
    private void CacheInMemory(string mcVersion, string[] versions, CancellationToken ct)
    {
        try
        {
            _neoForgeCacheLock.Wait(ct);
            try { _neoForgeCache[mcVersion] = new CachedEntry<string[]>(versions, SystemTime.UtcNow); }
            finally { _neoForgeCacheLock.Release(); }
        }
        catch
        {
            // Non-critical cache operation, ignore failures
        }
    }

    /// <summary>
    /// Получение версий Quilt
    /// </summary>
    public async Task<string[]> GetQuiltVersions(string mcVersion, CancellationToken ct = default)
    {
        return await FetchLoaderVersionsAsync("quilt", mcVersion, ["latest"], ct,
            ct2 => TryGetQuiltVersions(mcVersion, ct2));
    }

    private async Task<string[]?> TryGetQuiltVersions(string mcVersion, CancellationToken ct)
    {
        try
        {
            var url = $"{QuiltVersionsLoader}/{mcVersion}";
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
                        versions.Add(versionProp.GetString()!);
                    else if (loaderObj.TryGetProperty("maven", out var mavenProp))
                        versions.Add(mavenProp.GetString()!);
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
            return sortedVersions.Length > 0 ? sortedVersions : null;
        }
        catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.Warning($"Quilt does not support MC {mcVersion} (404 Not Found)", "McVersionsApi");
            return null;
        }
        catch (OperationCanceledException)
        {
            Logger.Warning($"Timeout getting Quilt versions for MC {mcVersion}", "McVersionsApi");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Quilt versions for MC {mcVersion}: {ex.Message}", "McVersionsApi");
            return null;
        }
    }

    /// <summary>
    /// Получение сборок Paper для указанной версии Minecraft.
    /// Возвращает массив строк вида "131" (STABLE) или "132 (ALPHA)" (ALPHA).
    /// </summary>
    public async Task<string[]> GetPaperVersions(string mcVersion, CancellationToken ct = default)
    {
        return await FetchLoaderVersionsAsync("paper", mcVersion, [], ct,
            ct2 => TryGetPaperVersions(mcVersion, ct2));
    }

    private async Task<string[]?> TryGetPaperVersions(string mcVersion, CancellationToken ct)
    {
        try
        {
            var url = $"{PaperVersions}/{mcVersion}/builds";
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

            using var response = await _http.SendAsync(request, timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            var contentStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            using var decompressedStream = StreamUtilities.GetDecompressedStream(contentStream, response.Content.Headers);
            using var reader = new StreamReader(decompressedStream);
            var responseText = await reader.ReadToEndAsync(timeoutCts.Token);

            using var doc = JsonDocument.Parse(responseText);
            var builds = doc.RootElement.EnumerateArray().ToArray();

            var result = new List<string>();
            foreach (var build in builds)
            {
                var id = build.GetProperty("id").GetInt32();
                var channel = build.TryGetProperty("channel", out var ch) ? ch.GetString() ?? "STABLE" : "STABLE";

                if (channel == "STABLE")
                    result.Add(id.ToString());
                else
                    result.Add($"{id} (ALPHA)");
            }

            var versions = result.ToArray();
            return versions.Length > 0 ? versions : null;
        }
        catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Logger.Warning($"Paper does not support MC {mcVersion} (404 Not Found)", "McVersionsApi");
            return null;
        }
        catch (OperationCanceledException)
        {
            Logger.Warning($"Timeout getting Paper versions for MC {mcVersion}", "McVersionsApi");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to get Paper versions for MC {mcVersion}: {ex.Message}", "McVersionsApi");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_fileCacheLoadTask != null)
        {
            try { await _fileCacheLoadTask; }
            catch { /* Ignore cache load errors during dispose */ }
        }

        _http.Dispose();
        _cacheLock.Dispose();
        _fileLock.Dispose();
    }

    void IDisposable.Dispose() => _ = DisposeAsync().AsTask();

    /// <summary>
    /// Получение строки с поддержкой gzip/deflate
    /// </summary>
    public async Task<string> GetStringWithDecompressionAsync(string url, CancellationToken ct = default)
    {
        return await _http.GetStringAsync(url, ct);
    }

    // ─── Quilt Supported Versions (кэшируется в singleton) ─────────

    /// <summary>
    /// Возвращает список версий Minecraft, которые поддерживает Quilt.
    /// Результат кэшируется в памяти (McVersionsApi — singleton).
    /// Запросы выполняются параллельно (до 5 одновременных).
    /// </summary>
    public async Task<HashSet<string>> GetQuiltSupportedVersionsAsync(CancellationToken ct = default)
    {
        if (_quiltSupportedTask != null)
            return await _quiltSupportedTask;

        _quiltSupportedTask = FetchQuiltSupportedVersionsAsync(ct);
        return await _quiltSupportedTask;
    }

    private async Task<HashSet<string>> FetchQuiltSupportedVersionsAsync(CancellationToken ct)
    {
        try
        {
            var versions = await GetMcVersions(ct);
            var recentVersions = versions
                .Where(v => McVersionHelper.TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))
                .Take(20)
                .ToArray();

            var supported = new HashSet<string>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 5,
                CancellationToken = ct
            };

            var lockObj = new object();

            await Parallel.ForEachAsync(recentVersions, parallelOptions, async (version, token) =>
            {
                try
                {
                    var url = $"{QuiltVersionsLoader}/{version}";
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token);
                    if (response.IsSuccessStatusCode)
                    {
                        await using var stream = await response.Content.ReadAsStreamAsync(token);
                        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                        if (doc.RootElement.EnumerateArray().Any())
                        {
                            lock (lockObj) { supported.Add(version); }
                        }
                    }
                }
                catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // 404 — не поддерживается
                }
                catch (OperationCanceledException)
                {
                    // Отмена
                }
                catch
                {
                    // Игнорируем
                }
            });

            var result = supported.Count > 0 ? supported : [.. versions];
            Logger.Info($"Loaded {result.Count} Quilt-supported MC versions", "McVersionsApi");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load Quilt supported versions: {ex.Message}", "McVersionsApi");
            var fallback = await GetMcVersions(ct);
            return [.. fallback];
        }
    }

    // ─── Paper API Versions (кэшируется в singleton) ──────────────

    /// <summary>
    /// Возвращает список версий Minecraft, доступных для Paper.
    /// Результат кэшируется в памяти (McVersionsApi — singleton).
    /// </summary>
    public async Task<HashSet<string>> GetPaperApiVersionsAsync(CancellationToken ct = default)
    {
        if (_paperApiVersionsTask != null)
            return await _paperApiVersionsTask;

        _paperApiVersionsTask = FetchPaperApiVersionsAsync(ct);
        return await _paperApiVersionsTask;
    }

    private async Task<HashSet<string>> FetchPaperApiVersionsAsync(CancellationToken ct)
    {
        try
        {
            var response = await _http.GetStringAsync(PaperApi + "/projects/paper", ct);
            using var doc = JsonDocument.Parse(response);
            var versions = doc.RootElement.GetProperty("versions")
                .EnumerateObject()
                .SelectMany(g => g.Value.EnumerateArray())
                .Select(v => v.GetString()!);
            return [.. versions];
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load Paper API versions: {ex.Message}", "McVersionsApi");
            var fallback = await GetMcVersions(ct);
            return [.. fallback];
        }
    }

    /// <summary>
    /// Универсальный метод для получения версий загрузчиков с кэшированием и fallback.
    /// Проверяет файловый кэш, при промахе вызывает fetcher'ы по порядку (null пропускаются),
    /// сохраняет результат в кэш и возвращает fallbackResult если всё остальное не удалось.
    /// </summary>
    private async Task<string[]> FetchLoaderVersionsAsync(
        string cacheKey,
        string mcVersion,
        string[] fallbackResult,
        CancellationToken ct,
        params Func<CancellationToken, Task<string[]?>>?[] fetchers)
    {
        // 1. Проверяем файловый кэш
        var cachedVersions = GetFromFileCache(cacheKey, mcVersion);
        if (cachedVersions != null)
        {
            Logger.Info($"Returning cached {cacheKey} versions for MC {mcVersion}: {cachedVersions.Length}", "McVersionsApi");
            return cachedVersions;
        }

        // 2. Пробуем fetcher'ы по порядку
        foreach (var fetcher in fetchers)
        {
            if (fetcher == null) continue;

            var result = await fetcher(ct);
            if (result != null && result.Length > 0)
            {
                SaveToFileCache(cacheKey, mcVersion, result);
                return result;
            }
        }

        // 3. Все источники не сработали — fallback
        Logger.Warning($"All sources failed for {cacheKey} MC {mcVersion}, returning fallback", "McVersionsApi");
        return fallbackResult;
    }

    #region File Cache

    private async Task LoadFileCacheAsync()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
                return;

            var json = await File.ReadAllTextAsync(_cacheFilePath);
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
            var dir = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_fileCache);
            await File.WriteAllTextAsync(_cacheFilePath, json);
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
        // Если кэш ещё не загружен — не ждём, просто возвращаем null.
        // Сетевой источник будет использован, а кэш сохранится при следующем успешном запросе.
        if (_fileCacheLoadTask?.IsCompleted == false)
            return null;

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
            "paper" => _fileCache.Paper,
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
        // Если кэш ещё не загружен — не ждём, возвращаем null.
        if (_fileCacheLoadTask?.IsCompleted == false)
            return null;
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
            "paper" => _fileCache.Paper ??= new Dictionary<string, string[]>(),
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

        [JsonPropertyName("paper")]
        public Dictionary<string, string[]>? Paper { get; set; }

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }
    }

    #endregion
}
