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

        using var doc = await FetchJsonAsync(url, ct);
        Logger.Info($"Got manifest response", "McServerInstaller");
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
    /// Скачать файл по URL с прогрессом
    /// </summary>
    private async Task<(bool success, string? error)> DownloadFile(string url, string destinationPath, string fileName,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var downloadingStatus = string.Format(LocalizationManager.Get("Installer_Downloading"), fileName);
            progress?.Report(downloadingStatus);
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
            var lastReportedPercent = -1;

            while ((bytesRead = await decompressedStream.ReadAsync(buffer, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                downloadedBytes += bytesRead;

                // Сообщаем прогресс при каждом новом проценте
                if (totalBytes > 0)
                {
                    var percent = (int)(downloadedBytes * 100 / totalBytes);
                    if (percent != lastReportedPercent)
                    {
                        lastReportedPercent = percent;
                        progress?.Report($"{downloadingStatus} {percent}%");
                    }
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
            Logger.Error($"Download failed ({fileName}): {ex.Message}", ex, "McServerInstaller");
            return (false, $"{ex.GetType().Name}: {ex.Message}");
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

    #region Общие методы

    /// <summary>
    /// Выполнить HTTP GET и прочитать ответ как строку с decompression.
    /// </summary>
    private async Task<string> FetchStringAsync(string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await GetHttpClient().SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var decompressed = StreamUtilities.GetDecompressedStream(contentStream, response.Content.Headers);
        using var reader = new StreamReader(decompressed);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>
    /// Выполнить HTTP GET и распарсить JSON-ответ.
    /// </summary>
    private async Task<JsonDocument> FetchJsonAsync(string url, CancellationToken ct = default)
    {
        var text = await FetchStringAsync(url, ct);
        return JsonDocument.Parse(text);
    }

    /// <summary>
    /// Ожидание стабилизации файлов после установки (Forge / NeoForge / Quilt).
    /// </summary>
    private async Task WaitForFileStabilityAsync(string destinationPath, string loaderPrefix, CancellationToken ct,
        IProgress<string>? progress = null, int maxWaitSeconds = 60, bool checkRunBat = true)
    {
        Logger.Info($"Waiting for {loaderPrefix} file operations to complete...", "McServerInstaller");
        progress?.Report(LocalizationManager.Get("Installer_Finishing"));

        var waitStartTime = SystemTime.Now;
        var maxWait = TimeSpan.FromSeconds(maxWaitSeconds);
        var minWait = TimeSpan.FromSeconds(3);
        var hasMinWaitElapsed = false;
        var librariesPath = Path.Combine(destinationPath, "libraries");
        var serverJarPattern = $"{loaderPrefix}-*.jar";
        var universalPattern = $"{loaderPrefix}-*-universal.jar";

        while ((SystemTime.Now - waitStartTime) < maxWait)
        {
            var hasLoaderJarInRoot = Directory.GetFiles(destinationPath, serverJarPattern).Any();
            var hasUniversalInLibraries = false;
            if (Directory.Exists(librariesPath))
            {
                hasUniversalInLibraries = Directory.GetFiles(librariesPath, universalPattern, SearchOption.AllDirectories).Any() ||
                                          Directory.GetFiles(librariesPath, "forge-*-universal.jar", SearchOption.AllDirectories).Any();
            }
            var hasLibraries = Directory.Exists(librariesPath);
            var hasRunBat = !checkRunBat || File.Exists(Path.Combine(destinationPath, "run.bat"));

            if ((!hasLoaderJarInRoot && !hasUniversalInLibraries) || !hasLibraries)
            {
                await Task.Delay(500, ct);
                continue;
            }

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
                Logger.Info($"{loaderPrefix} files unlocked and stable ({keyFiles.Length} checked)", "McServerInstaller");
                return;
            }
            else if (allUnlocked)
            {
                Logger.Info($"{loaderPrefix} files unlocked but run.bat not ready, waiting...", "McServerInstaller");
                await Task.Delay(1000, ct);
                if ((SystemTime.Now - waitStartTime) > TimeSpan.FromSeconds(10))
                {
                    Logger.Info($"Exiting {loaderPrefix} stability wait (10s elapsed, files unlocked)", "McServerInstaller");
                    return;
                }
            }

            await Task.Delay(500, ct);
        }
    }

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
    /// Найти Java путь для конкретной версии Minecraft.
    /// Использует централизованный <see cref="JavaVersionParser.GetRequiredJavaVersion"/>.
    /// </summary>
    private string? FindJavaPathForVersion(string mcVersion)
    {
        Logger.Info($"Finding Java for Minecraft version {mcVersion}", "McServerInstaller");

        // Определяем требуемую версию Java через централизованный парсер
        int requiredJavaVersion = JavaVersionParser.GetRequiredJavaVersion(mcVersion, ServerLaunchType.Standard);

        Logger.Info($"Minecraft {mcVersion} requires Java {requiredJavaVersion}", "McServerInstaller");

        // Пытаемся найти Java нужной версии в конфигурации
        try
        {
            var config = _configService?.GetConfig();
            if (config != null)
            {
                // Ищем Java с точным совпадением major-версии (используем MajorVersion, а не строку Version)
                var matchingJava = config.JavaInstallations
                    .FirstOrDefault(j => j.Exists && j.MajorVersion == requiredJavaVersion);

                if (matchingJava != null)
                {
                    Logger.Info($"Found Java {requiredJavaVersion} at {matchingJava.Path}", "McServerInstaller");
                    return matchingJava.Path;
                }

                // Если не нашли точное совпадение, пробуем найти Java с большей версией
                var newerJava = config.JavaInstallations
                    .FirstOrDefault(j => j.Exists && j.MajorVersion >= requiredJavaVersion);

                if (newerJava != null)
                {
                    Logger.Info($"Using newer Java {newerJava.MajorVersion} (required {requiredJavaVersion}) at {newerJava.Path}", "McServerInstaller");
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
}
