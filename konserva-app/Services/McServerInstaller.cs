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
public static partial class McServerInstaller
{
    [GeneratedRegex(@"forge-(\d+\.\d+\.\d+-\d+\.\d+\.\d+)")]
    private static partial Regex ForgeVersionRegex();
    private static HttpClient? _http;
    private static IConfigService? _configService;
    private static bool _initialized;

    public static void Initialize(HttpClient httpClient, IConfigService? configService = null)
    {
        if (_initialized) return;

        _http = httpClient;
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.Add("User-Agent", "Konserva/1.0");
        _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        _http.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        _configService = configService;
        _initialized = true;
    }

    private static HttpClient GetHttpClient()
    {
        if (_http == null)
            throw new InvalidOperationException("McServerInstaller not initialized. Call Initialize() first.");
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
    private static async Task<List<VersionInfo>> GetVersionManifest(CancellationToken ct = default)
    {
        var url = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
        Logger.Info($"Fetching version manifest from: {url}", "McServerInstaller");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await GetHttpClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var contentStream = await response.Content.ReadAsStreamAsync(ct);
        var decompressedStream = GetDecompressedStream(contentStream, response.Content.Headers);

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
    public static async Task<string?> GetVanillaServerDownloadUrl(string version, CancellationToken ct = default)
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
            using var decompressedStream = GetDecompressedStream(contentStream, response.Content.Headers);
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
    private static async Task<(bool success, string? error)> DownloadFile(string url, string destinationPath, string fileName,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(destinationPath);
            var filePath = Path.Combine(destinationPath, fileName);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var decompressedStream = GetDecompressedStream(contentStream, response.Content.Headers);
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await decompressedStream.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    var percent = (double)downloadedBytes / totalBytes * 100;
                    progress.Report(percent);
                }
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
            return (false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Stream GetDecompressedStream(Stream compressedStream, HttpContentHeaders headers)
    {
        var contentEncoding = headers.ContentEncoding?.ToString() ?? "";
        if (contentEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            return new System.IO.Compression.GZipStream(compressedStream, System.IO.Compression.CompressionMode.Decompress);
        }
        if (contentEncoding.Contains("deflate", StringComparison.OrdinalIgnoreCase))
        {
            return new System.IO.Compression.DeflateStream(compressedStream, System.IO.Compression.CompressionMode.Decompress);
        }
        return compressedStream;
    }

    /// <summary>
    /// Скачать vanilla сервер
    /// </summary>
    public static async Task<bool> DownloadVanillaServer(string version, string destinationPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
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
    public static async Task<(string url, string version)?> GetFabricInstallerInfo(CancellationToken ct = default)
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
                Logger.Info($"Fabric API response: {responseContent[..Math.Min(200, responseContent.Length)]}...");

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
    public static async Task<bool> InstallFabricServer(string mcVersion, string loaderVersion,
        string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        Logger.Info($"Installing Fabric server: MC={mcVersion}, Loader={loaderVersion}, Path={destinationPath}");

        try
        {
            Directory.CreateDirectory(destinationPath);
            Logger.Info($"Created directory: {destinationPath}");

            // 1. Получаем версии Fabric
            Logger.Info("Getting Fabric versions...");
            var loaderVersions = await GetFabricLoaderVersions(mcVersion, ct);
            if (loaderVersions == null || loaderVersions.Length == 0)
            {
                Logger.Error("No Fabric loader versions found");
                return false;
            }

            // Выбираем последнюю версию loader
            var selectedLoaderVersion = loaderVersion == "latest"
                ? loaderVersions[0]
                : loaderVersions.FirstOrDefault(v => v.Contains(loaderVersion)) ?? loaderVersions[0];

            Logger.Info($"Selected Fabric loader version: {selectedLoaderVersion}");

            // 2. Получаем версию installer с повторными попытками
            string? installerVersion = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Logger.Info($"Getting Fabric installer version (attempt {attempt}/3)...");
                    installerVersion = await GetFabricInstallerVersion(ct);
                    if (!string.IsNullOrEmpty(installerVersion))
                    {
                        Logger.Info($"Fabric installer version: {installerVersion}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Attempt {attempt} failed: {ex.Message}");
                    if (attempt < 3)
                        await Task.Delay(1000 * attempt, ct);
                }
            }

            // Если не получили версию installer, используем известную стабильную версию
            if (string.IsNullOrEmpty(installerVersion))
            {
                Logger.Warning("Failed to get Fabric installer version, using fallback version 0.16.10");
                installerVersion = "0.16.10";
            }

            // 3. Скачиваем сервер напрямую (0-80%)
            var serverJarPath = Path.Combine(destinationPath, "fabric-server-launch.jar");
            var serverUrl = $"https://meta.fabricmc.net/v2/versions/loader/{mcVersion}/{selectedLoaderVersion}/{installerVersion}/server/jar";

            Logger.Info($"Downloading Fabric server from: {serverUrl}");
            var (success, error) = await DownloadFile(serverUrl, destinationPath, "fabric-server-launch.jar", progress, ct);

            if (!success)
            {
                Logger.Error($"Failed to download Fabric server: {error}");
                return false;
            }

            Logger.Info($"Fabric server downloaded successfully");

            // 4. Создаем eula.txt и server.properties
            progress?.Report(90);
            CreateEula(destinationPath);
            CreateServerProperties(destinationPath, 25565);

            Logger.Info("Fabric server installation completed");
            progress?.Report(100);
            return true;
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
    private static async Task<string[]?> GetFabricLoaderVersions(string mcVersion, CancellationToken ct = default)
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
                Logger.Info($"First item JSON: {firstItem.GetRawText()[..Math.Min(200, firstItem.GetRawText().Length)]}...");

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
    private static async Task<string?> GetFabricInstallerVersion(CancellationToken ct = default)
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

    /// <summary>
    /// Запустить Fabric installer
    /// </summary>
    private static async Task<bool> RunFabricInstaller(string installerPath, string mcVersion,
        string loaderVersion, string destinationPath, CancellationToken ct, IProgress<double>? progress = null)
    {
        // Проверяем наличие Java
        var javaPath = FindJavaPath();
        if (string.IsNullOrEmpty(javaPath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-jar \"{installerPath}\" server -mcversion {mcVersion} -loader {loaderVersion} -downloadMinecraft",
            WorkingDirectory = destinationPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // Обновляем прогресс во время ожидания (30% -> 90%)
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromMinutes(5); // Максимум 5 минут на установку

            while (!process.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(500, ct);

                var elapsed = DateTime.Now - startTime;
                if (elapsed < timeout)
                {
                    var installProgress = 30 + (elapsed.TotalMilliseconds / timeout.TotalMilliseconds * 60);
                    progress?.Report(Math.Min(installProgress, 90));
                }
            }

            await process.WaitForExitAsync(ct);

            // Fabric installer может вернуть 0 даже при успехе
            return process.ExitCode == 0 || File.Exists(Path.Combine(destinationPath, "fabric-server-launch.jar"));
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Forge

    /// <summary>
    /// РџРѕР»СѓС‡РёС‚СЊ URL Forge installer
    /// </summary>
    public static async Task<string?> GetForgeInstallerUrl(string mcVersion, string forgeVersion, CancellationToken ct = default)
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
    public static async Task<bool> InstallForgeServer(string mcVersion, string forgeVersion,
        string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(destinationPath);

            var installerUrl = await GetForgeInstallerUrl(mcVersion, forgeVersion, ct);
            if (string.IsNullOrEmpty(installerUrl))
                return false;

            // 1. Скачиваем installer (0-30%)
            var installerPath = Path.Combine(destinationPath, "forge-installer.jar");
            var progressWrapper = new Progress<double>(p => progress?.Report(p * 0.3));
            var downloadResult = await DownloadFile(installerUrl, destinationPath, "forge-installer.jar", progressWrapper, ct);
            if (!downloadResult.success)
                return false;

            // 2. Запускаем installer (30-90%)
            var success = await RunForgeInstaller(installerPath, destinationPath, ct, progress);

            // 3. Удаляем installer
            if (success && File.Exists(installerPath))
            {
                try { File.Delete(installerPath); } catch { }
            }

            if (success)
                progress?.Report(100);

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
    private static async Task<bool> RunForgeInstaller(string installerPath, string destinationPath, CancellationToken ct, IProgress<double>? progress = null)
    {
        Logger.Info($"Running Forge/NeoForge installer: {installerPath}");

        // Проверяем наличие Java
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
            Arguments = $"-jar \"{installerPath}\" --installServer",
            WorkingDirectory = destinationPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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

            // Читаем вывод процесса
            var outputLines = new List<string>();
            var errorLines = new List<string>();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputLines.Add(e.Data);
                    Logger.Info($"[Forge] {e.Data}", "McServerInstaller");
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorLines.Add(e.Data);
                    Logger.Info($"[Forge] {e.Data}", "McServerInstaller");
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Обновляем прогресс во время ожидания (30% -> 90%)
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromMinutes(15); // Увеличенный таймаут для Forge

            while (!process.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(1000, ct); // Проверка каждую секунду

                var elapsed = DateTime.Now - startTime;
                if (elapsed < timeout)
                {
                    var installProgress = 30 + (elapsed.TotalMilliseconds / timeout.TotalMilliseconds * 60);
                    progress?.Report(Math.Min(installProgress, 90));
                }
                else
                {
                    Logger.Warning($"Forge installer timeout after {timeout.TotalMinutes} minutes", "McServerInstaller");
                    break;
                }
            }

            await process.WaitForExitAsync(ct);

            Logger.Info($"Installer exited with code: {process.ExitCode}", "McServerInstaller");

            // Логируем последние строки вывода
            if (outputLines.Count > 0)
            {
                Logger.Info($"Forge installer output ({outputLines.Count} lines):", "McServerInstaller");
                foreach (var line in outputLines.TakeLast(10))
                {
                    Logger.Info($"  {line}", "McServerInstaller");
                }
            }

            // Проверяем успешность по наличию файлов
            var hasForgeJar = Directory.GetFiles(destinationPath, "forge-*.jar").Length > 0;
            var hasNeoForgeJar = Directory.GetFiles(destinationPath, "neoforge-*.jar").Length > 0;
            var hasRunBat = File.Exists(Path.Combine(destinationPath, "run.bat"));
            var hasLibraries = Directory.Exists(Path.Combine(destinationPath, "libraries"));

            Logger.Info($"Check results: forge-*.jar={hasForgeJar}, neoforge-*.jar={hasNeoForgeJar}, run.bat={hasRunBat}, libraries={hasLibraries}", "McServerInstaller");

            var success = process.ExitCode == 0 || hasForgeJar || hasNeoForgeJar || hasRunBat || hasLibraries;

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
    public static async Task<string?> GetNeoForgeInstallerUrl(string mcVersion, string neoforgeVersion, CancellationToken ct = default)
    {
        try
        {
            // Если версия "latest", нужно получить конкретную версию
            var actualVersion = neoforgeVersion;
            if (neoforgeVersion == "latest" || neoforgeVersion == "recommended")
            {
                Logger.Info($"Fetching NeoForge version list for {mcVersion} to resolve '{neoforgeVersion}'", "McServerInstaller");

                try
                {
                    // NeoForge promotions API
                    var manifestUrl = "https://maven.neoforged.net/releases/net/neoforged/forge/promotions_slim.json";
                    var manifest = await GetHttpClient().GetStringAsync(manifestUrl, ct);

                    using var doc = JsonDocument.Parse(manifest);
                    var promos = doc.RootElement.GetProperty("promos");

                    // Конвертируем версию Minecraft в формат NeoForge для promoKey
                    // 1.21.10 -> 21.10, 1.20.4 -> 20.4
                    var neoforgeMcVersion = ConvertMcVersionToNeoForgeFormat(mcVersion);
                    var promoKey = $"{neoforgeMcVersion}-{neoforgeVersion}";
                    if (promos.TryGetProperty(promoKey, out var promoVersion))
                    {
                        actualVersion = promoVersion.GetString()!;
                        Logger.Info($"Resolved '{neoforgeVersion}' to {actualVersion} using key '{promoKey}'", "McServerInstaller");
                    }
                    else
                    {
                        Logger.Warning($"Promo key '{promoKey}' not found in NeoForge promotions", "McServerInstaller");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to resolve NeoForge version: {ex.Message}", "McServerInstaller");
                    // Fallback на известные версии
                    actualVersion = mcVersion switch
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
                        _ => neoforgeVersion
                    };
                    Logger.Info($"Using fallback NeoForge version: {actualVersion}", "McServerInstaller");
                }
            }

            // Формируем URL с версией NeoForge
            // Для latest/recommended используем resolved actualVersion (например, 21.10.64)
            // Для конкретной версии используем только версию NeoForge (например, 21.10.64)
            string mavenPath;
            if (neoforgeVersion == "latest" || neoforgeVersion == "recommended")
            {
                // Используем разрешённую версию (например, 21.10.64 вместо 1.21.10-latest)
                mavenPath = actualVersion;
            }
            else
            {
                // Используем только версию NeoForge (actualVersion уже содержит правильную версию)
                mavenPath = actualVersion;
            }
            
            var urls = new[]
            {
                $"https://maven.neoforged.net/releases/net/neoforged/forge/{mavenPath}/forge-{mavenPath}-installer.jar",
                $"https://maven.neoforged.net/api/v1/installer/{mavenPath}"
            };

            foreach (var url in urls)
            {
                Logger.Info($"Checking NeoForge URL: {url}", "McServerInstaller");

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
    /// Установить NeoForge сервер автоматически
    /// </summary>
    public static async Task<bool> InstallNeoForgeServer(string mcVersion, string neoforgeVersion,
        string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
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

            // 1. Скачиваем installer (0-30%)
            var installerPath = Path.Combine(destinationPath, "neoforge-installer.jar");
            var progressWrapper = new Progress<double>(p => progress?.Report(p * 0.3));

            Logger.Info($"Downloading NeoForge installer from: {installerUrl}");
            var downloadResult = await DownloadFile(installerUrl, destinationPath, "neoforge-installer.jar", progressWrapper, ct);

            if (!downloadResult.success)
            {
                Logger.Error($"Failed to download NeoForge installer: {downloadResult.error}");
                return false;
            }

            Logger.Info("NeoForge installer downloaded, running installer...");

            // 2. Запускаем installer (30-90%)
            var success = await RunForgeInstaller(installerPath, destinationPath, ct, progress);

            if (success && File.Exists(installerPath))
            {
                try { File.Delete(installerPath); } catch { }
            }

            if (success)
            {
                Logger.Info("NeoForge server installed successfully");
                progress?.Report(100);
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
    public static async Task<(string url, string version)?> GetQuiltInstallerInfo(CancellationToken ct = default)
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
    public static async Task<bool> InstallQuiltServer(string mcVersion, string loaderVersion,
        string destinationPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Logger.Info($"Installing Quilt server MC {mcVersion} loader {loaderVersion}", "McServerInstaller");
            Directory.CreateDirectory(destinationPath);

            // 1. Скачиваем Quilt installer (0-30%)
            var installerInfo = await GetQuiltInstallerInfo(ct);
            if (installerInfo == null)
            {
                Logger.Error("Failed to get Quilt installer info", null, "McServerInstaller");
                return false;
            }

            Logger.Info($"Got Quilt installer: {installerInfo.Value.version}", "McServerInstaller");

            var installerPath = Path.Combine(destinationPath, "quilt-installer.jar");
            var progressWrapper = new Progress<double>(p => progress?.Report(p * 0.3));
            var downloadResult = await DownloadFile(installerInfo.Value.url, destinationPath, "quilt-installer.jar", progressWrapper, ct);
            if (!downloadResult.success)
            {
                Logger.Error($"Failed to download Quilt installer: {downloadResult.error}", null, "McServerInstaller");
                return false;
            }

            Logger.Info("Downloaded Quilt installer, running...", "McServerInstaller");

            // 2. Запускаем installer с флагом --download-server (30-90%)
            // Quilt installer сам скачивает server.jar и установит loader
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
                        try { Directory.Delete(serverSubfolder, true); } catch { }

                        Logger.Info("Moved Quilt server files from subfolder to root", "McServerInstaller");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Failed to move Quilt files: {ex.Message}", "McServerInstaller");
                    }
                }
            }

            if (success && File.Exists(installerPath))
            {
                try { File.Delete(installerPath); } catch { }
            }

            if (success)
                progress?.Report(100);

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
    private static async Task<bool> RunQuiltInstaller(string installerPath, string mcVersion,
        string destinationPath, CancellationToken ct, IProgress<double>? progress = null)
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
            Arguments = $"-jar \"{installerPath}\" install server {mcVersion} --download-server",
            WorkingDirectory = destinationPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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

            // Читаем вывод для отладки
            var output = new List<string>();
            var error = new List<string>();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.Add(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.Add(e.Data); };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Обновляем прогресс во время ожидания (30% -> 90%)
            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromMinutes(5);

            while (!process.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(500, ct);

                var elapsed = DateTime.Now - startTime;
                if (elapsed < timeout)
                {
                    var installProgress = 30 + (elapsed.TotalMilliseconds / timeout.TotalMilliseconds * 60);
                    progress?.Report(Math.Min(installProgress, 90));
                }
            }

            await process.WaitForExitAsync(ct);

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
    public static async Task<bool> DownloadPaperServer(string version, string destinationPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var apiUrl = $"https://api.papermc.io/v2/projects/paper/versions/{version}/builds";
            Logger.Info($"Fetching Paper builds for {version}: {apiUrl}", "McServerInstaller");

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

            using var response = await GetHttpClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var decompressedStream = GetDecompressedStream(contentStream, response.Content.Headers);
            using var reader = new StreamReader(decompressedStream);
            var responseText = await reader.ReadToEndAsync(ct);

            Logger.Info($"Paper API response: {responseText.Length} bytes", "McServerInstaller");

            using var doc = JsonDocument.Parse(responseText);
            var builds = doc.RootElement.GetProperty("builds");

            // Р‘РµСЂРµРј РџРћРЎР›Р•Р”РќРР™ build (СЃР°РјС‹Р№ РЅРѕРІС‹Р№), Р° РЅРµ РїРµСЂРІС‹Р№
            var buildArray = builds.EnumerateArray().ToArray();
            if (buildArray.Length == 0)
            {
                Logger.Warning($"No Paper builds found for {version}", "McServerInstaller");
                return false;
            }

            var latestBuild = buildArray.Last();
            Logger.Info($"Latest Paper build: {latestBuild.GetProperty("build")}", "McServerInstaller");

            var buildNumber = latestBuild.GetProperty("build").GetInt32();
            var fileName = latestBuild.GetProperty("downloads")
                .GetProperty("application")
                .GetProperty("name")
                .GetString()!;

            var downloadUrl = $"https://api.papermc.io/v2/projects/paper/versions/{version}/builds/{buildNumber}/downloads/{fileName}";
            Logger.Info($"Downloading Paper from: {downloadUrl}", "McServerInstaller");

            var (success, error) = await DownloadFile(downloadUrl, destinationPath, "server.jar", progress, ct);

            if (success)
            {
                Logger.Info("Paper server downloaded successfully", "McServerInstaller");
            }
            else
            {
                Logger.Error($"Paper download failed: {error}", null, "McServerInstaller");
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error($"Paper installation failed: {ex.Message}", ex, "McServerInstaller");
            return false;
        }
    }

    #endregion

    #region Spigot

    // В РАЗРАБОТКЕ - установка Spigot временно отключена

    #endregion

    #region Purpur

    /// <summary>
    /// Скачать Purpur сервер
    /// </summary>
    public static async Task<bool> DownloadPurpurServer(string version, string destinationPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            // Purpur API v2: https://api.purpurmc.org/v2/purpur/{version}/{build}/download
            // Сначала получаем информацию о последней сборке
            var apiUrl = $"https://api.purpurmc.org/v2/purpur/{version}/latest";
            Logger.Info($"Fetching Purpur latest for {version}: {apiUrl}", "McServerInstaller");

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

            using var response = await GetHttpClient().SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var contentStream = await response.Content.ReadAsStreamAsync(ct);
            using var decompressedStream = GetDecompressedStream(contentStream, response.Content.Headers);
            using var reader = new StreamReader(decompressedStream);
            var responseText = await reader.ReadToEndAsync(ct);

            Logger.Info($"Purpur API response: {responseText.Length} bytes", "McServerInstaller");

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            // Получаем номер сборки
            string? buildNumber = null;
            if (root.TryGetProperty("build", out var buildProp))
            {
                buildNumber = buildProp.GetString();
                Logger.Info($"Latest Purpur build for {version}: {buildNumber}", "McServerInstaller");
            }

            if (string.IsNullOrEmpty(buildNumber))
            {
                Logger.Warning($"Purpur API did not return build number for {version}", "McServerInstaller");
                return false;
            }

            // Формируем URL для скачивания: /{version}/{build}/download
            var downloadUrl = $"https://api.purpurmc.org/v2/purpur/{version}/{buildNumber}/download";
            Logger.Info($"Downloading Purpur from: {downloadUrl}", "McServerInstaller");

            var (success, error) = await DownloadFile(downloadUrl, destinationPath, "server.jar", progress, ct);

            if (success)
            {
                Logger.Info("Purpur server downloaded successfully", "McServerInstaller");
            }
            else
            {
                Logger.Error($"Purpur download failed: {error}", null, "McServerInstaller");
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.Error($"Purpur installation failed: {ex.Message}", ex, "McServerInstaller");
            return false;
        }
    }

    #endregion

    #region Общие методы

    /// <summary>
    /// Создать eula.txt
    /// </summary>
    public static void CreateEula(string serverPath)
    {
        var eulaPath = Path.Combine(serverPath, "eula.txt");
        var content = $"""
            #By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).
            #Generated by Konserva on {DateTime.Now:yyyy-MM-dd}
            eula=true
            """;

        File.WriteAllText(eulaPath, content);
    }

    /// <summary>
    /// Создать server.properties
    /// </summary>
    public static void CreateServerProperties(string serverPath, int port = 25565)
    {
        var propertiesPath = Path.Combine(serverPath, "server.properties");
        var content = $"""
            #Minecraft server properties
            #Generated by Konserva on {DateTime.Now:yyyy-MM-dd}
            
            # Network
            server-port={port}
            server-ip=
            enable-query=false
            query.port={port}
            
            # Game
            gamemode=survival
            difficulty=easy
            hardcore=false
            spawn-protection=16
            max-players=20
            allow-nether=true
            spawn-animals=true
            spawn-monsters=true
            spawn-npcs=true
            
            # World
            level-name=world
            level-seed=
            level-type=minecraft\\normal
            generate-structures=true
            max-world-size=29999984
            
            # Server
            motd=A Minecraft Server powered by Konserva
            white-list=false
            enforce-whitelist=false
            pvp=true
            online-mode=true
            prevent-proxy-connections=false
            
            # Performance
            view-distance=10
            simulation-distance=10
            max-tick-time=60000
            
            # Other
            enable-command-block=false
            enable-rcon=false
            broadcast-rcon-to-ops=true
            broadcast-console-to-ops=true
            enable-status=true
            enforce-secure-profile=true
            sync-chunk-writes=true
            enable-jmx-monitoring=false
            player-idle-timeout=0
            network-compression-threshold=256
            op-permission-level=4
            function-permission-level=2
            """;

        File.WriteAllText(propertiesPath, content);
    }

    /// <summary>
    /// Создать start скрипт
    /// </summary>
    public static void CreateStartScript(string serverPath, int ramMin = 1024, int ramMax = 4096)
    {
        var batPath = Path.Combine(serverPath, "start.bat");
        var batContent = $"""
            @echo off
            java -Xms{ramMin}M -Xmx{ramMax}M -jar server.jar nogui
            pause
            """;
        File.WriteAllText(batPath, batContent);

        var psPath = Path.Combine(serverPath, "start.ps1");
        var psContent = $"""
            #!/usr/bin/env pwsh
            java -Xms{ramMin}M -Xmx{ramMax}M -jar server.jar nogui
            """;
        File.WriteAllText(psPath, psContent);
    }

    /// <summary>
    /// Найти jar файл для запуска сервера
    /// </summary>
    public static string FindServerJar(string serverPath)
    {
        var priorityNames = new[]
        {
            "server.jar",
            "fabric-server-launch.jar",
            "quilt-server-launch.jar",
            "paper.jar",
            "purpur.jar",
            "spigot.jar"
        };

        foreach (var priority in priorityNames)
        {
            var found = Directory.GetFiles(serverPath, priority)
                .FirstOrDefault();
            if (found != null)
                return found;
        }

        // Ищем forge jar
        var forgeJars = Directory.GetFiles(serverPath, "forge-*.jar");
        if (forgeJars.Length > 0)
            return forgeJars[0];

        // Ищем neoforge jar
        var neoforgeJars = Directory.GetFiles(serverPath, "neoforge-*.jar");
        if (neoforgeJars.Length > 0)
            return neoforgeJars[0];

        // Любой jar
        return Directory.GetFiles(serverPath, "*.jar").FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Получить тип запуска сервера
    /// </summary>
    public static ServerLaunchType GetServerLaunchType(string serverPath)
    {
        if (File.Exists(Path.Combine(serverPath, "fabric-server-launch.jar")))
            return ServerLaunchType.Fabric;

        // Quilt может иметь разные имена файлов
        var quiltJars = Directory.GetFiles(serverPath, "quilt-server-*.jar");
        if (quiltJars.Length > 0)
            return ServerLaunchType.Quilt;

        if (Directory.GetFiles(serverPath, "forge-*.jar").Length > 0)
            return ServerLaunchType.Forge;

        if (Directory.GetFiles(serverPath, "neoforge-*.jar").Length > 0)
            return ServerLaunchType.NeoForge;

        // Vanilla, Paper, Purpur, Spigot используют стандартный запуск
        return ServerLaunchType.Standard;
    }

    /// <summary>
    /// Построить аргументы Java для запуска
    /// </summary>
    public static string BuildLaunchArgs(string jarPath, ServerSettings settings, ServerLaunchType _ = ServerLaunchType.Standard)
    {
        var args = new StringBuilder();

        // RAM настройки
        args.Append($"-Xms{settings.RamMin}M -Xmx{settings.RamMax}M ");

        // G1GC оптимизации (для большинства серверов)
        args.Append("-XX:+UseG1GC -XX:+ParallelRefProcEnabled -XX:MaxGCPauseMillis=200 ");
        args.Append("-XX:+UnlockExperimentalVMOptions -XX:+DisableExplicitGC -XX:+AlwaysPreTouch ");
        args.Append("-XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 ");
        args.Append("-XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 ");
        args.Append("-XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 ");
        args.Append("-XX:InitiatingHeapOccupancyPercent=15 ");

        // Пользовательские аргументы
        foreach (var arg in settings.JavaArgs)
        {
            if (!string.IsNullOrWhiteSpace(arg))
                args.Append($"{arg} ");
        }

        // Jar файл и nogui
        args.Append($"-jar \"{Path.GetFileName(jarPath)}\" nogui");

        return args.ToString();
    }

    /// <summary>
    /// Тип запуска сервера
    /// </summary>
    public enum ServerLaunchType
    {
        Standard,  // Vanilla, Paper, Purpur, Spigot
        Fabric,
        Quilt,
        Forge,
        NeoForge
    }

    /// <summary>
    /// Полная установка сервера
    /// </summary>
    public static async Task<InstallResult> InstallServer(
        ModLoaderType modLoaderType,
        string mcVersion,
        string loaderVersion,
        string serverPath,
        int port = 25565,
        int ramMin = 1024,
        int ramMax = 4096,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var result = new InstallResult();

        try
        {
            result.Status = InstallStatus.Installing;

            bool installSuccess = modLoaderType switch
            {
                ModLoaderType.Vanilla => await DownloadVanillaServer(mcVersion, serverPath, progress, ct),
                ModLoaderType.Fabric => await InstallFabricServer(mcVersion, loaderVersion, serverPath, progress, ct),
                ModLoaderType.Forge => await InstallForgeServer(mcVersion, loaderVersion, serverPath, progress, ct),
                ModLoaderType.NeoForge => await InstallNeoForgeServer(mcVersion, loaderVersion, serverPath, progress, ct),
                ModLoaderType.Paper => await DownloadPaperServer(mcVersion, serverPath, progress, ct),
                ModLoaderType.Purpur => await DownloadPurpurServer(mcVersion, serverPath, progress, ct),
                ModLoaderType.Quilt => await InstallQuiltServer(mcVersion, loaderVersion, serverPath, progress, ct),
                _ => await DownloadVanillaServer(mcVersion, serverPath, progress, ct)
            };

            if (!installSuccess)
            {
                result.Success = false;
                result.Error = $"Не удалось установить сервер для {modLoaderType}. Проверьте наличие Java и интернет-соединение.";
                result.Status = InstallStatus.Failed;
                return result;
            }

            // Создание конфигурационных файлов
            result.Status = InstallStatus.Configuring;
            CreateEula(serverPath);
            CreateServerProperties(serverPath, port);
            CreateStartScript(serverPath, ramMin, ramMax);

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
    /// Найти путь к Java
    /// </summary>
    private static string? FindJavaPath()
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
                process.WaitForExit(5000);
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
    private static string? FindJavaPathForVersion(string mcVersion)
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
    /// Распарсить версию Minecraft на major и minor компоненты
    /// </summary>
    public static bool TryParseMcVersion(string version, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        try
        {
            // Формат: "1.XX" или "1.XX.Y"
            var parts = version.Split('.');
            if (parts.Length >= 2)
            {
                major = int.Parse(parts[0]);
                minor = int.Parse(parts[1]);
                return true;
            }
        }
        catch
        {
            // Игнорируем ошибки парсинга
        }

        return false;
    }

    /// <summary>
    /// Конвертировать версию Minecraft в формат NeoForge
    /// 1.21.10 -> 21.10, 1.20.4 -> 20.4, 1.18.2 -> 18.2
    /// </summary>
    private static string ConvertMcVersionToNeoForgeFormat(string mcVersion)
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
    private static int GetPatchVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length >= 3 && int.TryParse(parts[2], out var patch))
            return patch;
        return 0;
    }

    #endregion
}
