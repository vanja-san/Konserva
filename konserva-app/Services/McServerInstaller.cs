using Konserva.Localization;
using Konserva.Models;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Konserva.Services;

/// <summary>
/// Сервис автоматической установки серверов Minecraft
/// </summary>
public partial class McServerInstaller : IServerInstaller, IDisposable
{
    [GeneratedRegex(@"forge-(\d+\.\d+\.\d+-\d+\.\d+\.\d+)")]
    private partial Regex ForgeVersionRegex();
    private readonly HttpClient _http;
    private IConfigService? _configService;
    private bool _disposed;

    public McServerInstaller(HttpClient httpClient, IConfigService? configService = null)
    {
        _http = httpClient;
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.Add("User-Agent", "Konserva/1.0");
        _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        _configService = configService;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // HttpClient получен из IHttpClientFactory — не диспозим, фабрика управляет его жизнью
    }

    private HttpClient GetHttpClient()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _http;
    }

    /// <summary>
    /// Информация о версии Minecraft
    /// </summary>
    public class VersionInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Получить манифест версий
    /// </summary>
    private async Task<List<VersionInfo>> GetVersionManifest(CancellationToken ct = default)
    {
        var url = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
        Logger.Info($"Fetching version manifest from: {url}", "McServerInstaller");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await GetHttpClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var contentStream = await response.Content.ReadAsStreamAsync(ct);
        var decompressedStream = StreamUtilities.GetDecompressedStream(contentStream, response.Content.Headers);

        using var reader = new StreamReader(decompressedStream);
        var responseText = await reader.ReadToEndAsync(ct);

        Logger.Info($"Got manifest response: {responseText.Length} bytes", "McServerInstaller");

        using var doc = JsonDocument.Parse(responseText);
        var versions = doc.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(v => new VersionInfo
            {
                Id = v.GetProperty("id").GetString()!,
                Type = v.GetProperty("type").GetString()!,
                Url = v.GetProperty("url").GetString()!
            })
            .ToList();

        Logger.Info($"Found {versions.Count} versions in manifest", "McServerInstaller");
        return versions;
    }

    /// <summary>
    /// Получить URL для скачивания vanilla сервера
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

            using var request = new HttpRequestMessage(HttpMethod.Get, versionEntry.Url);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

            using var response = await GetHttpClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var decompressedStream = StreamUtilities.GetDecompressedStream(contentStream, response.Content.Headers);
            using var reader = new StreamReader(decompressedStream);
            var responseText = await reader.ReadToEndAsync(ct);

            using var doc = JsonDocument.Parse(responseText);

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
    /// Скачать файл по URL с прогрессом
    /// </summary>
    private async Task<(bool success, string? error)> DownloadFile(string url, string destinationPath, string fileName,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            progress?.Report(string.Format(LocalizationManager.Get("Installer_Downloading"), fileName));
            Directory.CreateDirectory(destinationPath);
            var filePath = Path.Combine(destinationPath, fileName);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var decompressedStream = StreamUtilities.GetDecompressedStream(contentStream, response.Content.Headers);
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await decompressedStream.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;
            }

            await fileStream.FlushAsync(ct);
            return (true, null);
        }
        catch (OperationCanceledException)
        {
            return (false, "Загрузка отменена");
        }
        catch (Exception ex)
        {
            Logger.Error($"Download failed ({fileName}): {ex.Message}", ex, "McServerInstaller");
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Скачать vanilla сервер
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

    #region Fabric

    /// <summary>
    /// Получить информацию о последней версии Fabric installer
    /// </summary>
    public async Task<(string url, string version)?> GetFabricInstallerInfo(CancellationToken ct = default)
    {
        // Пробуем 3 раза с задержкой
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Logger.Info($"Fetching Fabric installer info (attempt {attempt}/3): https://meta.fabricmc.net/v2/versions/installer");

                using var request = new HttpRequestMessage(HttpMethod.Get, "https://meta.fabricmc.net/v2/versions/installer");
                var response = await GetHttpClient().SendAsync(request, ct);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync(ct);
                Logger.Info($"Fabric API response: {responseContent[..Math.Min(Constants.LogTruncationLength, responseContent.Length)]}...");

                using var doc = JsonDocument.Parse(responseContent);
                var array = doc.RootElement.EnumerateArray();

                if (!array.MoveNext())
                {
                    Logger.Error("Fabric API returned empty array");
                    return null;
                }

                var latest = array.Current;
                var url = latest.GetProperty("url").GetString()!;
                var version = latest.GetProperty("version").GetString()!;

                Logger.Info($"Found Fabric installer: version={version}, url={url}");
                return (url, version);
            }
            catch (OperationCanceledException) when (attempt < 3)
            {
                Logger.Info($"Fabric API request canceled (attempt {attempt}), retrying in 2 seconds...");
                await Task.Delay(2000, ct);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to get Fabric installer info (attempt {attempt}): {ex.GetType().Name} - {ex.Message}");
                if (attempt >= 3)
                    return null;

                await Task.Delay(2000, ct);
            }
        }

        return null;
    }

    /// <summary>
    /// Установить Fabric сервер автоматически
    /// </summary>
    public async Task<bool> InstallFabricServer(string mcVersion, string loaderVersion,
        string destinationPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Logger.Info($"Installing Fabric server: MC={mcVersion}, Loader={loaderVersion}, Path={destinationPath}");

        try
        {
            Directory.CreateDirectory(destinationPath);
            Logger.Info($"Created directory: {destinationPath}");

            // Версия загрузчика приходит из UI — используем напрямую
            var selectedLoaderVersion = loaderVersion;
            if (string.IsNullOrEmpty(selectedLoaderVersion) || selectedLoaderVersion == "latest")
            {
                // Если не передали конкретную версию, получаем последнюю
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

            // Получаем версию installer Fabric
            Logger.Info("Getting Fabric installer version...");
            string? installerVersion = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    installerVersion = await GetFabricInstallerVersion(ct);
                    if (!string.IsNullOrEmpty(installerVersion))
                    {
                        Logger.Info($"Fabric installer version: {installerVersion}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Fabric installer version attempt {attempt}/3 failed: {ex.Message}");
                }
            }

            // Fallback, если не удалось получить версию installer
            if (string.IsNullOrEmpty(installerVersion))
            {
                Logger.Warning("Failed to get Fabric installer version, using fallback version 1.1.1");
                installerVersion = "1.1.1";
            }

            // URL с installer version обязателен — meta.fabricmc.net требует его
            var primaryUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{selectedLoaderVersion}/{installerVersion}/server/jar";
            var fallbackUrl = $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{mcVersion}/{selectedLoaderVersion}/{installerVersion}/server/jar";

            // Пробуем официальный API, при ошибке — BMCLAPI
            var urlsToTry = new[] { primaryUrl, fallbackUrl };

            foreach (var url in urlsToTry)
            {
                Logger.Info($"Downloading Fabric server from: {url}");
                progress?.Report(LocalizationManager.Get("Installer_DownloadingServer"));

                var (success, error) = await DownloadFile(url, destinationPath, "fabric-server-launch.jar", progress, ct);

                if (success)
                {
                    Logger.Info("Fabric server downloaded successfully");

                    // Создаем eula.txt
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
    /// Получить версии Fabric loader для версии Minecraft
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

            // Распаковываем GZip если нужно
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

            // Логируем структуру для отладки
            var array = doc.RootElement.EnumerateArray();
            if (array.MoveNext())
            {
                var firstItem = array.Current;
                Logger.Info($"First item JSON: {firstItem.GetRawText()[..Math.Min(Constants.LogTruncationLength, firstItem.GetRawText().Length)]}...");

                // Fabric API возвращает структуру: { "loader": { "version": "x.y.z" }, ... }
                // Возвращаем все версии
                array = doc.RootElement.EnumerateArray();
                var versions = array.Select(v =>
                {
                    // Пробуем получить loader.version
                    if (v.TryGetProperty("loader", out var loaderObj) &&
                        loaderObj.TryGetProperty("version", out var versionProp))
                    {
                        return versionProp.GetString()!;
                    }
                    // Fallback на version
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
    /// Получить версию Fabric installer
    /// </summary>
    private async Task<string?> GetFabricInstallerVersion(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://meta.fabricmc.net/v2/versions/installer");
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            using var response = await GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            var responseContent = Encoding.UTF8.GetString(bytes);

            // Check for GZip magic bytes
            if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                using var gzipStream = new System.IO.Compression.GZipStream(
                    new MemoryStream(bytes),
                    System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(gzipStream, Encoding.UTF8);
                responseContent = await reader.ReadToEndAsync(ct);
            }

            using var doc = JsonDocument.Parse(responseContent);
            var array = doc.RootElement.EnumerateArray();

            if (!array.MoveNext())
                return null;

            var latest = array.Current;
            return latest.GetProperty("version").GetString();
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

    #endregion

    #region Forge

    /// <summary>
    /// РџРѕР»СѓС‡РёС‚СЊ URL Forge installer
    /// </summary>
    public async Task<string?> GetForgeInstallerUrl(string mcVersion, string forgeVersion, CancellationToken ct = default)
    {
        try
        {
            // Forge использует формат: forge-{mc_version}-{forge_version}-installer.jar
            // URL: https://maven.minecraftforge.net/net/minecraftforge/forge/{mc_version}-{forge_version}/forge-{mc_version}-{forge_version}-installer.jar

            // Если версия "latest" или "recommended", нужно получить конкретную версию
            var actualVersion = forgeVersion;
            if (forgeVersion == "latest" || forgeVersion == "recommended")
            {
                Logger.Info($"Fetching Forge version list for {mcVersion} to resolve '{forgeVersion}'", "McServerInstaller");

                try
                {
                    var manifestUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
                    var manifest = await GetHttpClient().GetStringAsync(manifestUrl, ct);

                    using var doc = JsonDocument.Parse(manifest);
                    var promos = doc.RootElement.GetProperty("promos");

                    // Получаем версию для {mc_version}-latest или {mc_version}-recommended
                    var promoKey = $"{mcVersion}-{forgeVersion}";
                    if (promos.TryGetProperty(promoKey, out var promoVersion))
                    {
                        actualVersion = promoVersion.GetString()!;
                        Logger.Info($"Resolved '{forgeVersion}' to {actualVersion}", "McServerInstaller");
                    }
                    else
                    {
                        Logger.Warning($"Promo '{promoKey}' not found, using version list", "McServerInstaller");
                        // Fallback - парсим HTML
                        var htmlUrl = $"https://files.minecraftforge.net/net/minecraftforge/forge/index_{mcVersion}.html";
                        var html = await GetHttpClient().GetStringAsync(htmlUrl, ct);
                        var matches = ForgeVersionRegex().Matches(html);
                        if (matches.Count > 0)
                        {
                            actualVersion = matches[0].Groups[1].Value;
                            Logger.Info($"Found Forge version {actualVersion} from HTML", "McServerInstaller");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to resolve Forge version: {ex.Message}", "McServerInstaller");
                    // Используем последнюю известную версию для популярных MC
                    actualVersion = mcVersion switch
                    {
                        "1.21.1" => "52.1.9",
                        "1.21" => "51.0.29",
                        "1.20.4" => "49.0.50",
                        "1.20.1" => "47.2.0",
                        "1.19.2" => "43.3.0",
                        "1.18.2" => "40.2.0",
                        "1.16.5" => "36.2.39",
                        _ => forgeVersion
                    };
                }
            }

            // Формируем правильный Maven путь: {mc_version}-{forge_version}
            // forgeVersion уже содержит полную версию (например "52.1.9"), не нужно дублировать mcVersion
            var mavenPath = $"{mcVersion}-{actualVersion}";

            // Проверяем, не начинается ли actualVersion с mcVersion (чтобы избежать дублирования)
            if (actualVersion.StartsWith(mcVersion + "-"))
            {
                mavenPath = actualVersion;
            }

            var urls = new[]
            {
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mavenPath}/forge-{mavenPath}-installer.jar",
                $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{mavenPath}/forge-{mavenPath}-installer.jar"
            };

            foreach (var url in urls)
            {
                Logger.Info($"Checking Forge URL: {url}", "McServerInstaller");

                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await GetHttpClient().SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    Logger.Info($"Forge installer found: {url}", "McServerInstaller");
                    return url;
                }
                else
                {
                    Logger.Warning($"Forge URL check failed: {response.StatusCode}", "McServerInstaller");
                }
            }

            Logger.Error("All Forge URLs failed", null, "McServerInstaller");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get Forge installer URL: {ex.GetType().Name} - {ex.Message}", ex, "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Установить Forge сервер автоматически
    /// </summary>
    public async Task<bool> InstallForgeServer(string mcVersion, string forgeVersion,
        string destinationPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(destinationPath);

            var installerUrl = await GetForgeInstallerUrl(mcVersion, forgeVersion, ct);
            if (string.IsNullOrEmpty(installerUrl))
                return false;

            // 1. Скачиваем installer
            var downloadsDir = Utilities.Constants.DownloadsPath;
            Directory.CreateDirectory(downloadsDir);
            var installerPath = Path.Combine(downloadsDir, "forge-installer.jar");
            progress?.Report(LocalizationManager.Get("Installer_DownloadingInstaller"));
            var downloadResult = await DownloadFile(installerUrl, downloadsDir, "forge-installer.jar", progress, ct);
            if (!downloadResult.success)
                return false;

            // 2. Запускаем installer
            progress?.Report(string.Format(LocalizationManager.Get("Installer_RunningInstaller"), "Forge"));
            var success = await RunForgeInstaller(installerPath, destinationPath, ct, progress);

            // 3. Удаляем installer
            if (success && File.Exists(installerPath))
            {
                try { File.Delete(installerPath); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McServerInstaller] Cleanup delete failed: {ex.Message}"); }
            }

            if (success)
            {

                // Ждём пока завершатся все файловые операции
                Logger.Info("Waiting for file operations to complete...", "McServerInstaller");
                progress?.Report(LocalizationManager.Get("Installer_Finishing"));

                var waitStartTime = SystemTime.Now;
                var maxWaitTime = TimeSpan.FromSeconds(60);
                var minWait = TimeSpan.FromSeconds(3); // Минимум 3 сек, чтобы не завершиться до начала распаковки
                var hasMinWaitElapsed = false;

                while ((SystemTime.Now - waitStartTime) < maxWaitTime)
                {
                    var hasForgeJarInRoot = Directory.GetFiles(destinationPath, "forge-*.jar").Any() ||
                                           Directory.GetFiles(destinationPath, "neoforge-*.jar").Any();
                    var librariesPathCheck = Path.Combine(destinationPath, "libraries");
                    var hasUniversalInLibraries = false;
                    if (Directory.Exists(librariesPathCheck))
                    {
                        hasUniversalInLibraries = Directory.GetFiles(librariesPathCheck, "forge-*-universal.jar", SearchOption.AllDirectories).Any() ||
                                                  Directory.GetFiles(librariesPathCheck, "neoforge-*-universal.jar", SearchOption.AllDirectories).Any();
                    }
                    var hasLibraries = Directory.Exists(librariesPathCheck);
                    var hasRunBat = File.Exists(Path.Combine(destinationPath, "run.bat"));

                    if ((!hasForgeJarInRoot && !hasUniversalInLibraries) || !hasLibraries)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    // Ждём минимум 3 секунды прежде чем проверять стабильность
                    if (!hasMinWaitElapsed && (SystemTime.Now - waitStartTime) < minWait)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }
                    hasMinWaitElapsed = true;

                    // Проверяем что ключевые файлы не заблокированы
                    var keyFiles = Directory.GetFiles(destinationPath, "*.jar")
                        .Concat(Directory.GetFiles(Path.Combine(destinationPath, "libraries"), "*.jar", SearchOption.AllDirectories))
                        .Take(50)
                        .ToArray();

                    if (keyFiles.Length == 0)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    var allUnlocked = keyFiles.All(IsFileUnlocked);

                    if (allUnlocked && hasRunBat)
                    {
                        await Task.Delay(1000, ct);
                        Logger.Info($"Forge files unlocked and stable ({keyFiles.Length} checked, run.bat present)", "McServerInstaller");
                        break;
                    }
                    else if (allUnlocked)
                    {
                        // run.bat может не быть для старых версий, но если все файлы разблокированы — ок
                        Logger.Info($"Forge files unlocked but no run.bat, waiting for more files...", "McServerInstaller");
                        await Task.Delay(1000, ct);
                        // Если прошло уже 10+ секунд и всё разблокировано — выходим
                        if ((SystemTime.Now - waitStartTime) > TimeSpan.FromSeconds(10))
                        {
                            Logger.Info("Exiting Forge stability wait (10s elapsed, files unlocked)", "McServerInstaller");
                            break;
                        }
                    }

                    await Task.Delay(500, ct);
                }

                // Копируем forge universal jar из libraries в корень (новые версии Forge не создают jar в корне)
                try
                {
                    var librariesPath = Path.Combine(destinationPath, "libraries");
                    if (Directory.Exists(librariesPath))
                    {
                        var forgeJars = Directory.GetFiles(librariesPath, "forge-*-universal.jar", SearchOption.AllDirectories);
                        if (forgeJars.Length > 0)
                        {
                            var srcJar = forgeJars[0];
                            var jarName = $"forge-{forgeVersion}.jar";
                            var dstJar = Path.Combine(destinationPath, jarName);
                            if (!File.Exists(dstJar))
                            {
                                File.Copy(srcJar, dstJar);
                                Logger.Info($"Copied universal jar to {jarName}", "McServerInstaller");
                            }
                        }
                        else
                        {
                            // Fallback: ищем любой forge-*.jar
                            var anyForgeJars = Directory.GetFiles(librariesPath, "forge-*.jar", SearchOption.AllDirectories);
                            if (anyForgeJars.Length > 0)
                            {
                                var srcJar = anyForgeJars[0];
                                var jarName = $"forge-{forgeVersion}.jar";
                                var dstJar = Path.Combine(destinationPath, jarName);
                                if (!File.Exists(dstJar))
                                {
                                    File.Copy(srcJar, dstJar);
                                    Logger.Info($"Copied jar to {jarName} (fallback)", "McServerInstaller");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to copy Forge universal jar: {ex.Message}", "McServerInstaller");
                }

                Logger.Info("File operations completed", "McServerInstaller");
            }

            return success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Запустить Forge installer
    /// </summary>
    private async Task<bool> RunForgeInstaller(string installerPath, string destinationPath, CancellationToken ct, IProgress<string>? progress = null)
    {
        Logger.Info($"Running Forge/NeoForge installer: {installerPath}");

        var javaPath = FindJavaPath();
        if (string.IsNullOrEmpty(javaPath))
        {
            Logger.Error("Java not found for Forge/NeoForge installer");
            return false;
        }

        Logger.Info($"Using Java: {javaPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-Dfile.encoding=UTF-8 -jar \"{installerPath}\" --installServer",
            WorkingDirectory = destinationPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Logger.Error("Failed to start Forge/NeoForge installer process");
                return false;
            }

            Logger.Info($"Installer process started, PID: {process.Id}");

            // Читаем вывод ПОСТРОЧНО СИНХРОННО — это гарантирует что мы не пропустим
            // момент завершения. WaitForExit ненадёжен на Windows.
            var outputLines = new List<string>();
            var errorLines = new List<string>();
            var startTime = SystemTime.Now;
            bool installedSuccessfully = false;
            int patchCount = 0;
            int libraryCount = 0;

            // Читаем stdout построчно — блокирует пока есть вывод
            await Task.Run(() =>
            {
                string? line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    lock (outputLines) outputLines.Add(line);
                    Logger.Info($"[Forge] {line}", "McServerInstaller");

                    // Считаем для статуса
                    if (line.Contains("Considering library"))
                        libraryCount++;
                    if (line.Contains("Patching "))
                        patchCount++;
                    if (line.Contains("installed successfully"))
                        installedSuccessfully = true;

                    // Передаём реальный вывод процесса в прогресс (показываем пользователю)
                    progress?.Report(line);
                }
            }, ct);

            // Читаем stderr
            await Task.Run(() =>
            {
                string? line;
                while ((line = process.StandardError.ReadLine()) != null)
                {
                    lock (errorLines) errorLines.Add(line);
                    Logger.Info($"[Forge] {line}", "McServerInstaller");

                    // stderr тоже передаём — там могут быть предупреждения
                    progress?.Report(line);
                }
            }, ct);

            // Ждём завершения процесса
            process.WaitForExit();

            var elapsed = SystemTime.Now - startTime;
            Logger.Info($"Installer exited with code: {process.ExitCode} after {elapsed.TotalSeconds:F1}s", "McServerInstaller");
            Logger.Info($"Forge stats: {libraryCount} libraries, {patchCount} patches, installedSuccessfully={installedSuccessfully}", "McServerInstaller");

            // Проверяем успешность
            var hasForgeJar = Directory.GetFiles(destinationPath, "forge-*.jar").Length > 0;
            var hasNeoForgeJar = Directory.GetFiles(destinationPath, "neoforge-*.jar").Length > 0;
            var hasRunBat = File.Exists(Path.Combine(destinationPath, "run.bat"));
            var hasLibraries = Directory.Exists(Path.Combine(destinationPath, "libraries"));

            var success = installedSuccessfully || process.ExitCode == 0 || hasForgeJar || hasNeoForgeJar || hasRunBat || hasLibraries;

            if (!success)
            {
                Logger.Error($"Forge installer failed. ExitCode={process.ExitCode}", null, "McServerInstaller");
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error($"Forge/NeoForge installer exception: {ex.Message}", ex, "McServerInstaller");
            return false;
        }
    }

    #endregion

    #region NeoForge

    /// <summary>
    /// Получить URL NeoForge installer
    /// </summary>
    public async Task<string?> GetNeoForgeInstallerUrl(string mcVersion, string neoforgeVersion, CancellationToken ct = default)
    {
        try
        {
            // Если версия "latest", нужно получить конкретную версию
            var actualVersion = neoforgeVersion;
            if (neoforgeVersion == "latest" || neoforgeVersion == "recommended")
            {
                Logger.Info($"Fetching NeoForge version list for {mcVersion} to resolve '{neoforgeVersion}'", "McServerInstaller");

                // Пробуем получить последнюю версию из Maven metadata
                // NeoForge promotions API (promotions_slim.json) был удалён после разделения Forge и NeoForge
                actualVersion = await GetLatestNeoForgeVersionFromMetadata(mcVersion, ct) ?? neoforgeVersion;
            }

            // Формируем URL с версией NeoForge
            // Для MC < 1.21 используется neoforged/forge/, для MC >= 1.21 — neoforged/neoforge/
            var isNewNeoForge = IsNewNeoForgePath(mcVersion);
            var baseGroup = isNewNeoForge ? "neoforge" : "forge";
            var prefix = isNewNeoForge ? "neoforge-" : "forge-";
            string mavenPath = actualVersion;

            // Проверяем несколько форматов URL
            var urls = new List<string>
            {
                // Формат 1: Maven классический (новый путь neoforged/neoforge/ или старый neoforged/forge/)
                $"https://maven.neoforged.net/releases/net/neoforged/{baseGroup}/{mavenPath}/{prefix}{mavenPath}-installer.jar",
                // Формат 2: Maven с mc версией
                $"https://maven.neoforged.net/releases/net/neoforged/{baseGroup}/{mcVersion}-{mavenPath}/{prefix}{mcVersion}-{mavenPath}-installer.jar",
                // Формат 3: Прямой download
                $"https://maven.neoforged.net/api/v1/installer/{mcVersion}-{mavenPath}"
            };

            // Если не уверены в пути, пробуем оба варианта
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

            // Если все URL не работали, пробуем получить версию из Maven metadata (fallback)
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
    /// Определить, используется новый путь NeoForge (neoforged/neoforge/) для данной MC версии
    /// </summary>
    private static bool IsNewNeoForgePath(string mcVersion)
    {
        // NeoForge перешёл на neoforged/neoforge/ начиная с MC 1.21
        if (Version.TryParse(mcVersion, out var ver))
        {
            return ver.Major >= 1 && ver.Minor >= 21;
        }
        return false;
    }

    /// <summary>
    /// Получить последнюю версию NeoForge из Maven metadata, соответствующую MC версии
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

                    // Ищем все версии
                    var versionMatches = Regex.Matches(metadata, @"<version>([^<]+)</version>");
                    var versions = versionMatches
                        .Select(m => m.Groups[1].Value)
                        .Where(v => v.StartsWith(neoforgePrefix) || v.StartsWith($"{mcVersion}-{neoforgePrefix}"))
                        .ToList();

                    if (versions.Count > 0)
                    {
                        // Сортируем по убыванию (последняя версия)
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

            // Fallback на известные версии
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
    /// Установить NeoForge сервер автоматически
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

            // 1. Скачиваем installer
            var downloadsDir = Utilities.Constants.DownloadsPath;
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

            // 2. Запускаем installer
            progress?.Report(string.Format(LocalizationManager.Get("Installer_RunningInstaller"), "NeoForge"));
            var success = await RunForgeInstaller(installerPath, destinationPath, ct, progress);

            if (success && File.Exists(installerPath))
            {
                try { File.Delete(installerPath); } catch { /* Suppress cleanup errors */ }
            }

            if (success)
            {
                // Ждём стабилизации файлов
                Logger.Info("Waiting for NeoForge file operations to complete...", "McServerInstaller");
                progress?.Report(LocalizationManager.Get("Installer_Finishing"));

                var waitStartTime = SystemTime.Now;
                var maxWaitTime = TimeSpan.FromSeconds(60);
                var librariesPath = Path.Combine(destinationPath, "libraries");
                var minWait = TimeSpan.FromSeconds(3);
                var hasMinWaitElapsed = false;

                while ((SystemTime.Now - waitStartTime) < maxWaitTime)
                {
                    // Новые NeoForge не создают jar в корне — проверяем и libraries
                    var hasForgeJarInRoot = Directory.GetFiles(destinationPath, "forge-*.jar").Any() ||
                                           Directory.GetFiles(destinationPath, "neoforge-*.jar").Any();
                    var hasUniversalInLibraries = false;
                    if (Directory.Exists(librariesPath))
                    {
                        hasUniversalInLibraries = Directory.GetFiles(librariesPath, "neoforge-*-universal.jar", SearchOption.AllDirectories).Any() ||
                                                  Directory.GetFiles(librariesPath, "forge-*-universal.jar", SearchOption.AllDirectories).Any();
                    }
                    var hasLibraries = Directory.Exists(librariesPath);
                    var hasRunBat = File.Exists(Path.Combine(destinationPath, "run.bat"));

                    if ((!hasForgeJarInRoot && !hasUniversalInLibraries) || !hasLibraries)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    // Ждём минимум 3 секунды
                    if (!hasMinWaitElapsed && (SystemTime.Now - waitStartTime) < minWait)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }
                    hasMinWaitElapsed = true;

                    var keyFiles = Directory.GetFiles(destinationPath, "*.jar")
                        .Concat(Directory.GetFiles(librariesPath, "*.jar", SearchOption.AllDirectories))
                        .Take(50)
                        .ToArray();

                    if (keyFiles.Length == 0)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    var allUnlocked = keyFiles.All(IsFileUnlocked);

                    if (allUnlocked && hasRunBat)
                    {
                        await Task.Delay(1000, ct);
                        Logger.Info($"NeoForge files unlocked and stable ({keyFiles.Length} checked, run.bat present)", "McServerInstaller");
                        break;
                    }
                    else if (allUnlocked)
                    {
                        Logger.Info($"NeoForge files unlocked but no run.bat yet, waiting...", "McServerInstaller");
                        await Task.Delay(1000, ct);
                        if ((SystemTime.Now - waitStartTime) > TimeSpan.FromSeconds(10))
                        {
                            Logger.Info("Exiting NeoForge stability wait (10s elapsed, files unlocked)", "McServerInstaller");
                            break;
                        }
                    }

                    await Task.Delay(500, ct);
                }

                // Сохраняем конфигурацию запуска NeoForge (classpath + main class)
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

    #endregion

    #region Quilt

    /// <summary>
    /// Получить информацию о Quilt installer
    /// API: https://meta.quiltmc.org/v3/versions/installer (возвращает массив)
    /// </summary>
    public async Task<(string url, string version)?> GetQuiltInstallerInfo(CancellationToken ct = default)
    {
        try
        {
            var response = await GetHttpClient().GetStringAsync(
                "https://meta.quiltmc.org/v3/versions/installer", ct);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Quilt API возвращает просто массив (не объект с value)
            var array = root.EnumerateArray();
            if (!array.MoveNext())
                return null;

            var latest = array.Current;
            var url = latest.GetProperty("url").GetString()!;
            var version = latest.GetProperty("version").GetString()!;
            return (url, version);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to get Quilt installer info: {ex.Message}", ex, "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Установить Quilt сервер автоматически
    /// </summary>
    public async Task<bool> InstallQuiltServer(string mcVersion, string loaderVersion,
        string destinationPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Logger.Info($"Installing Quilt server MC {mcVersion} loader {loaderVersion}", "McServerInstaller");
            Directory.CreateDirectory(destinationPath);

            // 1. Скачиваем Quilt installer
            var installerInfo = await GetQuiltInstallerInfo(ct);
            if (installerInfo == null)
            {
                Logger.Error("Failed to get Quilt installer info", null, "McServerInstaller");
                return false;
            }

            Logger.Info($"Got Quilt installer: {installerInfo.Value.version}", "McServerInstaller");

            var downloadsDir = Utilities.Constants.DownloadsPath;
            Directory.CreateDirectory(downloadsDir);
            var installerPath = Path.Combine(downloadsDir, "quilt-installer.jar");
            progress?.Report(LocalizationManager.Get("Installer_DownloadingInstaller"));
            var downloadResult = await DownloadFile(installerInfo.Value.url, downloadsDir, "quilt-installer.jar", progress, ct);
            if (!downloadResult.success)
            {
                Logger.Error($"Failed to download Quilt installer: {downloadResult.error}", null, "McServerInstaller");
                return false;
            }

            Logger.Info("Downloaded Quilt installer, running...", "McServerInstaller");

            // 2. Запускаем installer с флагом --download-server (30-90%)
            // Quilt installer сам скачивает server.jar и установит loader
            progress?.Report(string.Format(LocalizationManager.Get("Installer_RunningInstaller"), "Quilt"));
            var success = await RunQuiltInstaller(installerPath, mcVersion, destinationPath, ct, progress);

            // 3. Если Quilt создал подпапку "server" - перемещаем файлы в корень
            if (success)
            {
                var serverSubfolder = Path.Combine(destinationPath, "server");
                if (Directory.Exists(serverSubfolder))
                {
                    try
                    {
                        // Перемещаем все файлы из подпапки в корень
                        foreach (var file in Directory.GetFiles(serverSubfolder))
                        {
                            var fileName = Path.GetFileName(file);
                            var destPath = Path.Combine(destinationPath, fileName);
                            if (!File.Exists(destPath))
                            {
                                File.Move(file, destPath);
                            }
                        }

                        // Перемещаем все папки
                        foreach (var dir in Directory.GetDirectories(serverSubfolder))
                        {
                            var dirName = Path.GetFileName(dir);
                            var destDir = Path.Combine(destinationPath, dirName);
                            if (!Directory.Exists(destDir))
                            {
                                Directory.Move(dir, destDir);
                            }
                        }

                        // Удаляем пустую папку server
                        try { Directory.Delete(serverSubfolder, true); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McServerInstaller] Cleanup directory delete failed: {ex.Message}"); }

                        Logger.Info("Moved Quilt server files from subfolder to root", "McServerInstaller");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to move Quilt files: {ex.Message}", "McServerInstaller");
                    }
                }
            }

            // Удаляем установщик в любом случае (успех или ошибка)
            try
            {
                if (File.Exists(installerPath))
                    File.Delete(installerPath);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[McServerInstaller] Cleanup delete failed: {ex.Message}"); }

            if (success)
            {
                // Ждём стабилизации файлов (аналогично Forge)
                Logger.Info("Waiting for Quilt file operations to complete...", "McServerInstaller");

                var waitStartTime = SystemTime.Now;
                var maxWaitTime = TimeSpan.FromSeconds(30);
                var librariesPath = Path.Combine(destinationPath, "libraries");

                while ((SystemTime.Now - waitStartTime) < maxWaitTime)
                {
                    var hasServerJar = Directory.GetFiles(destinationPath, "quilt-server-*.jar").Length > 0;
                    if (!hasServerJar)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    var keyFiles = Directory.GetFiles(destinationPath, "*.jar")
                        .Concat(Directory.GetFiles(librariesPath, "*.jar", SearchOption.AllDirectories))
                        .Take(50)
                        .ToArray();

                    if (keyFiles.Length == 0)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    var allUnlocked = keyFiles.All(IsFileUnlocked);

                    if (allUnlocked)
                    {
                        await Task.Delay(1000, ct);
                        Logger.Info($"Quilt files unlocked and stable ({keyFiles.Length} checked)", "McServerInstaller");
                        break;
                    }

                    await Task.Delay(500, ct);
                }

                progress?.Report(LocalizationManager.Get("Installer_Finishing"));
                Logger.Info("Quilt file operations completed", "McServerInstaller");
            }

            return success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Запустить Quilt installer
    /// Официальная команда: java -jar quilt-installer.jar install server MINECRAFT_VERSION --download-server
    /// </summary>
    private async Task<bool> RunQuiltInstaller(string installerPath, string mcVersion,
        string destinationPath, CancellationToken ct, IProgress<string>? progress = null)
    {
        // Определяем Java версию на основе версии Minecraft
        // Quilt требует Java 21 для MC 1.20.5+, Java 17 для MC 1.18-1.20.4, Java 8 для старых версий
        var javaPath = FindJavaPathForVersion(mcVersion);
        if (string.IsNullOrEmpty(javaPath))
        {
            Logger.Error($"Java not found for Minecraft {mcVersion}", null, "McServerInstaller");
            return false;
        }

        Logger.Info($"Using Java: {javaPath} for Quilt installer", "McServerInstaller");

        // Официальная команда Quilt: install server MINECRAFT_VERSION --download-server
        // Версия loader выбирается автоматически (последняя)
        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-Dfile.encoding=UTF-8 -jar \"{installerPath}\" install server {mcVersion} --download-server",
            WorkingDirectory = destinationPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Logger.Error("Failed to start Quilt installer process", null, "McServerInstaller");
                return false;
            }

            progress?.Report(LocalizationManager.Get("Installer_Running"));

            // Читаем вывод синхронно построчно (как в Forge) — надёжнее, чем BeginOutputReadLine
            var output = new List<string>();
            var error = new List<string>();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));
            var combinedToken = timeoutCts.Token;

            try
            {
                // Читаем stdout в фоновом потоке
                var stdoutTask = Task.Run(() =>
                {
                    string? line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        lock (output) output.Add(line);
                        Logger.Info($"[Quilt] {line}", "McServerInstaller");
                        progress?.Report(line);
                    }
                }, combinedToken);

                // Читаем stderr в фоновом потоке
                var stderrTask = Task.Run(() =>
                {
                    string? line;
                    while ((line = process.StandardError.ReadLine()) != null)
                    {
                        lock (error) error.Add(line);
                        Logger.Info($"[Quilt/stderr] {line}", "McServerInstaller");
                        progress?.Report(line);
                    }
                }, combinedToken);

                // Ждём завершения процесса
                await process.WaitForExitAsync(combinedToken);

                // Ждём завершения чтения вывода
                await Task.WhenAll(stdoutTask, stderrTask);

                // Синхронный WaitForExit для гарантии сброса буферов
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                // Принудительно убиваем процесс (true = убить всё дерево дочерних процессов)
                try { process.Kill(true); } catch { }

                if (ct.IsCancellationRequested)
                {
                    Logger.Info("Quilt installer cancelled by user", "McServerInstaller");
                }
                else
                {
                    Logger.Warning("Quilt installer timeout after 5 minutes", "McServerInstaller");
                }
                return false;
            }

            // Логируем вывод для отладки
            if (output.Count > 0)
                Logger.Info($"Quilt installer output: {string.Join("\n", output.Take(20))}", "McServerInstaller");
            if (error.Count > 0)
                Logger.Warning($"Quilt installer errors: {string.Join("\n", error.Take(20))}", "McServerInstaller");

            // Проверяем наличие JAR файла
            var jarFiles = Directory.GetFiles(destinationPath, "quilt-server-*.jar");
            var success = process.ExitCode == 0 || jarFiles.Length > 0;

            if (!success)
            {
                Logger.Error($"Quilt installer failed with exit code: {process.ExitCode}", null, "McServerInstaller");
            }
            else
            {
                Logger.Info($"Quilt installer completed successfully (exit code: {process.ExitCode})", "McServerInstaller");
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error($"Exception running Quilt installer: {ex.Message}", ex, "McServerInstaller");
            return false;
        }
    }

    #endregion

    #region Paper

    /// <summary>
    /// Скачать Paper сервер
    /// </summary>
    public async Task<InstallResult> DownloadPaperServer(string version, string destinationPath,
        string? loaderVersion = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new InstallResult();

        try
        {
            // PaperMC Downloads Service v3 (Cloudflare) — официальный API
            var apiUrl = $"https://fill.papermc.io/v3/projects/paper/versions/{version}/builds";
            Logger.Info($"Fetching Paper builds for {version}: {apiUrl}", "McServerInstaller");

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

            using var response = await GetHttpClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var decompressedStream = StreamUtilities.GetDecompressedStream(contentStream, response.Content.Headers);
            using var reader = new StreamReader(decompressedStream);
            var responseText = await reader.ReadToEndAsync(ct);

            Logger.Info($"Paper API response: {responseText.Length} bytes", "McServerInstaller");

            using var doc = JsonDocument.Parse(responseText);
            // v3 возвращает JSON-массив build'ов в порядке убывания (новейший — первый)
            var builds = doc.RootElement.EnumerateArray().ToArray();

            JsonElement selectedBuild = default;

            // Если указан конкретный номер сборки — ищем её
            if (!string.IsNullOrEmpty(loaderVersion))
            {
                // Парсим номер сборки: если строка содержит " (ALPHA)", отрезаем
                var buildNumberStr = loaderVersion;
                var alphaIdx = loaderVersion.IndexOf(" (ALPHA)", StringComparison.OrdinalIgnoreCase);
                if (alphaIdx > 0)
                    buildNumberStr = loaderVersion[..alphaIdx];

                if (int.TryParse(buildNumberStr, out var targetBuild))
                {
                    selectedBuild = builds.FirstOrDefault(b =>
                        b.TryGetProperty("id", out var id) && id.GetInt32() == targetBuild);

                    if (selectedBuild.ValueKind != JsonValueKind.Undefined)
                    {
                        Logger.Info($"Using specified Paper build #{targetBuild}", "McServerInstaller");
                    }
                    else
                    {
                        Logger.Warning($"Specified Paper build #{targetBuild} not found, falling back to latest", "McServerInstaller");
                    }
                }
            }

            // Если сборка не найдена или не указана — ищем автоматически
            if (selectedBuild.ValueKind == JsonValueKind.Undefined)
            {
                // Ищем первый STABLE билд, иначе — самый новый (ALPHA)
                selectedBuild = builds.FirstOrDefault(b =>
                    b.TryGetProperty("channel", out var ch) && ch.GetString() == "STABLE");

                // Если STABLE нет, берём самый новый билд любого канала
                if (selectedBuild.ValueKind == JsonValueKind.Undefined && builds.Length > 0)
                {
                    selectedBuild = builds[0];
                    var channel = selectedBuild.GetProperty("channel").GetString();
                    var buildId = selectedBuild.GetProperty("id").GetInt32();
                    Logger.Info($"No STABLE Paper build found, using latest {channel} build #{buildId}", "McServerInstaller");
                }
            }

            if (selectedBuild.ValueKind == JsonValueKind.Undefined)
            {
                Logger.Warning($"No Paper builds found for {version}", "McServerInstaller");
                result.Success = false;
                result.Error = "No builds found";
                return result;
            }

            var buildNumber = selectedBuild.GetProperty("id").GetInt32();
            var downloadInfo = selectedBuild.GetProperty("downloads").GetProperty("server:default");
            var downloadUrl = downloadInfo.GetProperty("url").GetString()!;
            var fileName = downloadInfo.GetProperty("name").GetString()!;

            Logger.Info($"Paper build for {version}: #{buildNumber}", "McServerInstaller");
            Logger.Info($"Downloading Paper from: {downloadUrl}", "McServerInstaller");

            var (success, error) = await DownloadFile(downloadUrl, destinationPath, "server.jar", progress, ct);

            if (success)
            {
                Logger.Info("Paper server downloaded successfully", "McServerInstaller");
                result.Success = true;
                result.BuildNumber = buildNumber;  // Сохраняем номер сборки
            }
            else
            {
                Logger.Error($"Paper download failed: {error}", null, "McServerInstaller");
                result.Success = false;
                result.Error = error;
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"Paper installation failed: {ex.Message}", ex, "McServerInstaller");
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
    }

    #endregion

    #region Общие методы

    /// <summary>
    /// Проверить что файл не заблокирован другим процессом
    /// </summary>
    private bool IsFileUnlocked(string filePath)
    {
        try
        {
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                return true; // Файл доступен — не заблокирован
            }
        }
        catch (IOException)
        {
            return false; // Файл заблокирован
        }
        catch (UnauthorizedAccessException)
        {
            return false; // Нет доступа
        }
    }

    /// <summary>
    /// Создать eula.txt
    /// </summary>
    public void CreateEula(string serverPath)
    {
        var eulaPath = Path.Combine(serverPath, "eula.txt");
        var content = $"""
            #By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).
            #Generated by Konserva on {SystemTime.Now:yyyy-MM-dd}
            eula=true
            """;

        File.WriteAllText(eulaPath, content);
    }

    /// <summary>
    /// Найти jar файл для запуска сервера
    /// </summary>
    public string FindServerJar(string serverPath)
    {
        var priorityNames = new[]
        {
            "server.jar",
            "fabric-server-launch.jar",
            "quilt-server-launch.jar",
                    "paper.jar"
        };

        foreach (var priority in priorityNames)
        {
            var found = Directory.GetFiles(serverPath, priority)
                .FirstOrDefault();
            if (found != null)
                return found;
        }

        // Ищем forge jar (может быть forge-*.jar или *-shim.jar)
        var forgeJars = Directory.GetFiles(serverPath, "forge-*.jar");
        if (forgeJars.Length > 0)
            return forgeJars[0];

        // Ищем shim jar (новый формат Forge)
        var shimJars = Directory.GetFiles(serverPath, "*-shim.jar");
        if (shimJars.Length > 0)
            return shimJars[0];

        // Ищем neoforge jar
        var neoforgeJars = Directory.GetFiles(serverPath, "neoforge-*.jar");
        if (neoforgeJars.Length > 0)
            return neoforgeJars[0];

        // NeoForge 21.x+ без jar в корне — ищем в libraries Minecraft server jar
        if (File.Exists(Path.Combine(serverPath, ".neoforge-launch.json")))
        {
            var serverJars = Directory.GetFiles(
                Path.Combine(serverPath, "libraries", "net", "minecraft", "server"),
                "server-*.jar", SearchOption.AllDirectories);
            if (serverJars.Length > 0)
                return serverJars[0];
        }

        // Любой jar
        return Directory.GetFiles(serverPath, "*.jar").FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Получить тип запуска сервера
    /// </summary>
    public ServerLaunchType GetServerLaunchType(string serverPath)
    {
        if (File.Exists(Path.Combine(serverPath, "fabric-server-launch.jar")))
            return ServerLaunchType.Fabric;

        // Quilt может иметь разные имена файлов
        var quiltJars = Directory.GetFiles(serverPath, "quilt-server-*.jar");
        if (quiltJars.Length > 0)
            return ServerLaunchType.Quilt;

        // Forge (старый формат forge-*.jar или новый *-shim.jar)
        if (Directory.GetFiles(serverPath, "forge-*.jar").Length > 0)
            return ServerLaunchType.Forge;

        if (Directory.GetFiles(serverPath, "*-shim.jar").Length > 0)
            return ServerLaunchType.Forge;

        if (Directory.GetFiles(serverPath, "neoforge-*.jar").Length > 0)
            return ServerLaunchType.NeoForge;

        // NeoForge 21.x+ может не иметь jar в корне — проверяем маркер конфига запуска
        if (File.Exists(Path.Combine(serverPath, ".neoforge-launch.json")))
            return ServerLaunchType.NeoForge;

        // Vanilla, Paper используют стандартный запуск
        return ServerLaunchType.Standard;
    }

    /// <summary>
    /// Найти win_args.txt или unix_args.txt в папке версии NeoForge в libraries/.
    /// </summary>
    private static string? FindNeoForgeArgsFile(string serverPath)
    {
        var librariesDir = Path.Combine(serverPath, "libraries");
        if (!Directory.Exists(librariesDir))
            return null;

        try
        {
            foreach (var group in new[] { "neoforge", "forge" })
            {
                var neoforgeDir = Path.Combine(librariesDir, "net", "neoforged", group);
                if (!Directory.Exists(neoforgeDir)) continue;

                var versionDirs = Directory.GetDirectories(neoforgeDir);
                if (versionDirs.Length == 0) continue;

                var versionDir = versionDirs.OrderByDescending(d => d).First();
                var winArgs = Path.Combine(versionDir, "win_args.txt");
                if (File.Exists(winArgs)) return winArgs;

                var unixArgs = Path.Combine(versionDir, "unix_args.txt");
                if (File.Exists(unixArgs)) return unixArgs;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to find NeoForge args file: {ex.Message}", "McServerInstaller");
        }

        return null;
    }

    /// <summary>
    /// Построить аргументы Java для запуска
    /// </summary>
    /// <param name="jarPath">Путь к jar файлу сервера</param>
    /// <param name="settings">Настройки сервера</param>
    /// <param name="launchType">Тип модлоадера (зарезервировано для future специфичных аргументов)</param>
    public string BuildLaunchArgs(string jarPath, ServerSettings settings, ServerLaunchType launchType = ServerLaunchType.Standard, int javaMajorVersion = 0, string? serverPath = null)
    {
        var args = new StringBuilder();

        // RAM настройки
        args.Append($"-Xms{settings.RamMin}M -Xmx{settings.RamMax}M ");

        // Принудительно UTF-8 для всего вывода (чтобы русский текст не кракозябрился)
        args.Append("-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 ");

        // Подавляем "Advanced terminal features are not available in this environment"
        // Сервер запущен без реального терминала (через GUI), это ожидаемо
        // ANSI выключен, чтобы escape-последовательности не засоряли лог
        args.Append("-Dterminal.jline=false -Dterminal.ansi=false ");

        // Пользовательские JVM аргументы (GC оптимизации и т.п. — настраивается в настройках сервера)
        foreach (var arg in settings.JavaArgs)
        {
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            // ParallelRefProcEnabled deprecated в Java 26+, пропускаем для JDK >= 26
            if (javaMajorVersion >= 26 && arg.Contains("ParallelRefProcEnabled", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"Skipping deprecated arg '{arg}' for Java {javaMajorVersion}", "McServerInstaller");
                continue;
            }

            args.Append($"{arg} ");
        }

        // NeoForge (21.x+) не использует -jar, нужен classpath/module-path + bootstrap main class
        if (launchType == ServerLaunchType.NeoForge && !string.IsNullOrEmpty(serverPath))
        {
            var launchConfig = LoadNeoForgeLaunchConfig(serverPath);
            if (launchConfig != null)
            {
                if (!string.IsNullOrEmpty(launchConfig.ArgsFile))
                {
                    // Есть win_args.txt/unix_args.txt — используем @ файл.
                    // Файл уже содержит -classpath, main class (net.neoforged.fml.startup.Server),
                    // --add-opens, --add-exports и аргументы FML (--fml.mcVersion и т.д.)
                    args.Append($"@{launchConfig.ClassPath} nogui");
                }
                else if (FindNeoForgeArgsFile(serverPath) is string argsFile)
                {
                    // Миграция: конфиг от старой версии (без ArgsFile), но win_args.txt есть
                    var relativePath = argsFile.Substring(serverPath.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    args.Append($"@{relativePath} nogui");
                }
                else
                {
                    // Нет args файла — используем classpath из отсканированных jar'ов
                    args.Append($"-cp \"{launchConfig.ClassPath}\" {launchConfig.MainClass} nogui");
                }
                return args.ToString();
            }
            // Если конфиг не найден — падаем вниз и пытаемся через -jar
        }

        // Jar файл и nogui
        // Для jar из папки libraries — используем относительный путь от serverPath,
        // иначе — только имя файла (jar лежит в корне сервера)
        string jarName;
        if (!string.IsNullOrEmpty(serverPath) && jarPath.StartsWith(serverPath, StringComparison.OrdinalIgnoreCase))
        {
            // Делаем путь относительным от serverPath
            jarName = jarPath.Substring(serverPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        else
        {
            // Просто имя файла (jar в корне сервера или тесты)
            jarName = Path.GetFileName(jarPath);
        }
        args.Append($"-jar \"{jarName}\" nogui");

        return args.ToString();
    }

    /// <summary>
    /// Сохранить конфигурацию запуска NeoForge (classpath + main class).
    /// Сначала пробует прочитать win_args.txt/unix_args.txt (содержат точный classpath от установщика),
    /// иначе сканирует libraries/.
    /// </summary>
    public async Task SaveNeoForgeLaunchConfigAsync(string serverPath, CancellationToken ct = default)
    {
        var librariesDir = Path.Combine(serverPath, "libraries");
        if (!Directory.Exists(librariesDir))
        {
            Logger.Warning("No libraries directory found for NeoForge launch config", "McServerInstaller");
            return;
        }

        var mainClass = "net.neoforged.bootstrap.Bootstrap";
        string? classPath = null;
        string? argsFilePath = null;

        // Шаг 1: ищем win_args.txt/unix_args.txt через общий helper
        argsFilePath = FindNeoForgeArgsFile(serverPath);
        if (argsFilePath != null)
        {
            Logger.Info($"Found NeoForge args file: {argsFilePath}", "McServerInstaller");
            // Делаем путь относительным от serverPath для использования с @ в Java команде
            classPath = argsFilePath.Substring(serverPath.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        else
        {
            Logger.Info("No NeoForge args file found, scanning libraries for classpath...", "McServerInstaller");

            // Собираем все jar-файлы из libraries (исключая sources, javadoc)
            var allJars = Directory.GetFiles(librariesDir, "*.jar", SearchOption.AllDirectories)
                .Where(j => !j.EndsWith("-sources.jar", StringComparison.OrdinalIgnoreCase) &&
                            !j.EndsWith("-javadoc.jar", StringComparison.OrdinalIgnoreCase))
                .Order()
                .ToList();

            if (allJars.Count == 0)
            {
                Logger.Warning("No jars found in libraries for NeoForge", "McServerInstaller");
                return;
            }

            var classPathParts = new List<string>();
            foreach (var jar in allJars)
            {
                var relativePath = jar.Substring(serverPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                classPathParts.Add(relativePath);
            }
            classPath = string.Join(";", classPathParts);

            Logger.Info($"Built classpath from {allJars.Count} jars", "McServerInstaller");
        }

        // Сохраняем конфиг
        var config = new NeoForgeLaunchConfig
        {
            ClassPath = classPath,
            MainClass = mainClass,
            ArgsFile = argsFilePath != null ? Path.GetFileName(argsFilePath) : null,
            SavedAt = SystemTime.UtcNow
        };

        var json = System.Text.Json.JsonSerializer.Serialize(config);
        await File.WriteAllTextAsync(Path.Combine(serverPath, ".neoforge-launch.json"), json, ct);

        Logger.Info($"Saved NeoForge launch config: cp={classPath.Length} chars, main={mainClass}, hasArgsFile={argsFilePath != null}", "McServerInstaller");
    }

    /// <summary>
    /// Загрузить конфигурацию запуска NeoForge
    /// </summary>
    public NeoForgeLaunchConfig? LoadNeoForgeLaunchConfig(string serverPath)
    {
        var configPath = Path.Combine(serverPath, ".neoforge-launch.json");
        if (!File.Exists(configPath))
            return null;

        try
        {
            var json = File.ReadAllText(configPath);
            return System.Text.Json.JsonSerializer.Deserialize<NeoForgeLaunchConfig>(json);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load NeoForge launch config: {ex.Message}", "McServerInstaller");
            return null;
        }
    }

    /// <summary>
    /// Полная установка сервера
    /// </summary>
    public async Task<InstallResult> InstallServer(
        ModLoaderType modLoaderType,
        string mcVersion,
        string loaderVersion,
        string serverPath,
        int port = Constants.DefaultServerPort,
        int ramMin = 1024,
        int ramMax = 4096,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new InstallResult();

        try
        {
            progress?.Report(LocalizationManager.Get("Installer_Preparing"));
            result.Status = InstallStatus.Installing;

            // Для Paper используем InstallResult, для остальных — bool
            InstallResult installResult;
            if (modLoaderType == ModLoaderType.Paper)
            {
                installResult = await DownloadPaperServer(mcVersion, serverPath, loaderVersion, progress, ct);
            }
            else
            {
                var success = modLoaderType switch
                {
                    ModLoaderType.Vanilla => await DownloadVanillaServer(mcVersion, serverPath, progress, ct),
                    ModLoaderType.Fabric => await InstallFabricServer(mcVersion, loaderVersion, serverPath, progress, ct),
                    ModLoaderType.Forge => await InstallForgeServer(mcVersion, loaderVersion, serverPath, progress, ct),
                    ModLoaderType.NeoForge => await InstallNeoForgeServer(mcVersion, loaderVersion, serverPath, progress, ct),
                    ModLoaderType.Quilt => await InstallQuiltServer(mcVersion, loaderVersion, serverPath, progress, ct),
                    _ => await DownloadVanillaServer(mcVersion, serverPath, progress, ct)
                };
                installResult = success ? new InstallResult { Success = true } : new InstallResult { Success = false, Error = "Installation failed" };
            }

            if (!installResult.Success)
            {
                result.Success = false;
                result.Error = installResult.Error ?? $"Не удалось установить сервер для {modLoaderType}";
                result.Status = InstallStatus.Failed;
                return result;
            }

            // Сохраняем номер сборки для Paper
            if (installResult.BuildNumber.HasValue)
            {
                result.BuildNumber = installResult.BuildNumber;
            }

            // Создание конфигурационных файлов (server.properties создастся при запуске)
            progress?.Report(LocalizationManager.Get("Installer_Configuring"));
            result.Status = InstallStatus.Configuring;
            CreateEula(serverPath);
            // server.properties создаётся автоматически при первом запуске сервера

            progress?.Report(LocalizationManager.Get("Installer_Success"));
            result.Success = true;
            result.Status = InstallStatus.Completed;
            return result;
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Error = "Установка отменена";
            result.Status = InstallStatus.Failed;
            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"InstallServer failed: {ex.Message}", ex, "McServerInstaller");
            result.Success = false;
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            result.Status = InstallStatus.Failed;
            return result;
        }
    }

    /// <summary>
    /// Результат установки
    /// </summary>
    public class InstallResult
    {
        public bool Success { get; set; }
        public InstallStatus Status { get; set; }
        public string? Error { get; set; }
        public int? BuildNumber { get; set; }  // Номер сборки для Paper
    }

    public enum InstallStatus
    {
        NotStarted,
        Installing,
        Configuring,
        Completed,
        Failed
    }

    /// <summary>
    /// Конфигурация запуска NeoForge (classpath + main class вместо -jar)
    /// </summary>
    public class NeoForgeLaunchConfig
    {
        public string ClassPath { get; set; } = "";
        public string MainClass { get; set; } = "net.neoforged.bootstrap.Bootstrap";
        public string? ArgsFile { get; set; }
        public DateTime SavedAt { get; set; }
    }

    /// <summary>
    /// Найти путь к Java
    /// </summary>
    private string? FindJavaPath()
    {
        Logger.Info("Finding Java path...");

        // 1. Проверяем Java из конфигурации приложения
        try
        {
            var config = _configService?.GetConfig();
            if (config != null)
            {
                Logger.Info($"Config loaded. Java installations: {config.JavaInstallations.Count}");

                var defaultJava = config.GetDefaultJava();
                if (defaultJava != null && defaultJava.Exists)
                {
                    Logger.Info($"Found default Java: {defaultJava.DisplayName} at {defaultJava.Path}");
                    return defaultJava.Path;
                }

                // Если нет Java по умолчанию, берём первую доступную
                var firstJava = config.JavaInstallations.FirstOrDefault(j => j.Exists);
                if (firstJava != null)
                {
                    Logger.Info($"Found first available Java: {firstJava.DisplayName} at {firstJava.Path}");
                    return firstJava.Path;
                }

                Logger.Info("No Java found in config - Java installations exist but none have Exists=true");
                foreach (var java in config.JavaInstallations)
                {
                    Logger.Info($"  Java: {java.DisplayName} at {java.Path}, Exists={java.Exists}");
                }
            }
            else
            {
                Logger.Info("Config is null");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error checking config for Java: {ex.Message}");
        }

        // 2. Проверяем JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var javaExe = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(javaExe))
            {
                Logger.Info($"Found Java via JAVA_HOME: {javaExe}");
                return javaExe;
            }
            Logger.Info($"JAVA_HOME set but java.exe not found at: {javaExe}");
        }
        else
        {
            Logger.Info("JAVA_HOME not set");
        }

        // 3. Проверяем PATH
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "java",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(Constants.JavaPathCheckTimeoutMs);
                Logger.Info($"'where java' output: {output}");

                var paths = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var path in paths)
                {
                    var trimmedPath = path.Trim();
                    if (File.Exists(trimmedPath))
                    {
                        Logger.Info($"Found Java in PATH: {trimmedPath}");
                        return trimmedPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error searching PATH for Java: {ex.Message}");
        }

        Logger.Error("Java not found anywhere!");
        return null;
    }

    /// <summary>
    /// Найти Java путь для конкретной версии Minecraft
    /// Требования Java:
    /// - MC 1.20.5+ в†’ Java 21
    /// - MC 1.18-1.20.4 в†’ Java 17
    /// - MC 1.17 в†’ Java 16
    /// - MC 1.16 Рё РЅРёР¶Рµ в†’ Java 8
    /// </summary>
    private string? FindJavaPathForVersion(string mcVersion)
    {
        Logger.Info($"Finding Java for Minecraft version {mcVersion}", "McServerInstaller");

        // Парсим версию Minecraft
        if (!TryParseMcVersion(mcVersion, out var major, out var minor))
        {
            Logger.Warning($"Failed to parse MC version {mcVersion}, using default Java", "McServerInstaller");
            return FindJavaPath();
        }

        // Определяем требуемую версию Java
        int requiredJavaVersion = (major, minor) switch
        {
            ( >= 1, >= 20) when minor >= 5 => 21,      // 1.20.5+
            ( >= 1, >= 18) => 17,                       // 1.18-1.20.4
            (1, 17) => 16,                             // 1.17
            _ => 8                                     // 1.16 и ниже
        };

        Logger.Info($"Minecraft {mcVersion} requires Java {requiredJavaVersion}", "McServerInstaller");

        // Пытаемся найти Java нужной версии в конфигурации
        try
        {
            var config = _configService?.GetConfig();
            if (config != null)
            {
                // Ищем Java с точным совпадением версии
                var matchingJava = config.JavaInstallations
                    .FirstOrDefault(j => j.Exists && j.Version == requiredJavaVersion.ToString());

                if (matchingJava != null)
                {
                    Logger.Info($"Found Java {requiredJavaVersion} at {matchingJava.Path}", "McServerInstaller");
                    return matchingJava.Path;
                }

                // Если не нашли точное совпадение, пробуем найти Java с большей версией
                var newerJava = config.JavaInstallations
                    .FirstOrDefault(j => j.Exists && int.TryParse(j.Version, out var v) && v >= requiredJavaVersion);

                if (newerJava != null)
                {
                    Logger.Info($"Using newer Java {newerJava.Version} (required {requiredJavaVersion}) at {newerJava.Path}", "McServerInstaller");
                    return newerJava.Path;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error checking config for Java version: {ex.Message}", ex, "McServerInstaller");
        }

        // Fallback на обычный поиск Java
        Logger.Warning($"Java {requiredJavaVersion} not found, using any available Java", "McServerInstaller");
        return FindJavaPath();
    }

    /// <summary>
    /// Распарсить версию Minecraft на major и minor компоненты.
    /// Делегирует в <see cref="McVersionHelper.TryParseMcVersion"/>.
    /// </summary>
    public bool TryParseMcVersion(string version, out int major, out int minor)
        => McVersionHelper.TryParseMcVersion(version, out major, out minor);

    /// <summary>
    /// Конвертировать версию Minecraft в формат NeoForge
    /// 1.21.10 -> 21.10, 1.20.4 -> 20.4, 1.18.2 -> 18.2
    /// </summary>
    private string ConvertMcVersionToNeoForgeFormat(string mcVersion)
    {
        if (TryParseMcVersion(mcVersion, out var major, out var minor))
        {
            // Для версий 1.X.Y возвращаем X.Y
            if (major == 1)
                return $"{minor}.{GetPatchVersion(mcVersion)}";
            // Для версий 2.X+ возвращаем X.Y
            return $"{major}.{minor}";
        }
        return mcVersion;
    }

    /// <summary>
    /// Получить patch версию из строки (третий компонент)
    /// </summary>
    private int GetPatchVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[2], out var patch))
            return patch;
        return 0;
    }

    #endregion
}
