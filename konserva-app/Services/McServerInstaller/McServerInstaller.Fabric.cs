using Konserva.Localization;
using Konserva.Utilities;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Konserva.Services;

/// <summary>
/// Установка Fabric сервера: получение информации, загрузка и установка
/// </summary>
public partial class McServerInstaller
{
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
}
