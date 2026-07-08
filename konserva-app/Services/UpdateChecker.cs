using Konserva.Models;
using Konserva.Utilities;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using static Konserva.Models.ApiUrls;

namespace Konserva.Services
{
    /// <summary>
    /// Проверяет наличие обновлений через version.json в корне репозитория.
    /// Файл раздаётся через raw.githubusercontent.com (CDN, без rate limit).
    /// </summary>
    public sealed class UpdateChecker : IUpdateChecker
    {
        private readonly HttpClient _client;

        public UpdateChecker(HttpClient httpClient)
        {
            _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Проверяет наличие обновления через version.json на raw.githubusercontent.com.
        /// </summary>
        public async Task<UpdateInfo> CheckAsync()
        {
            var currentVersion = GetCurrentVersion();
            var buildType = DetectBuildType();
            var updateInfo = new UpdateInfo { CurrentVersion = currentVersion };

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                var json = await _client.GetStringAsync(VersionManifestUrl, cts.Token);

                var manifest = JsonSerializer.Deserialize<VersionManifest>(json);
                if (manifest == null || string.IsNullOrEmpty(manifest.LatestVersion))
                {
                    Logger.Warning("Version manifest is empty or invalid", "UpdateChecker");
                    return updateInfo;
                }

                updateInfo.IsCheckSuccessful = true;

                var newVersion = manifest.LatestVersion.TrimStart('v');

                if (!IsNewerVersion(currentVersion, newVersion))
                {
                    Logger.Info($"No update — current {currentVersion} is up to date", "UpdateChecker");
                    return updateInfo;
                }

                // Ищем ассет под наш тип сборки
                var download = manifest.Downloads?.GetValueOrDefault(buildType);
                if (download == null || string.IsNullOrEmpty(download.Url))
                {
                    Logger.Warning($"No download found for build type '{buildType}' in version manifest", "UpdateChecker");
                    return updateInfo;
                }

                updateInfo.IsAvailable = true;
                updateInfo.NewVersion = newVersion;
                updateInfo.AssetName = download.AssetName;
                updateInfo.DownloadUrl = download.Url;
                updateInfo.SizeBytes = download.SizeBytes;
                updateInfo.ReleaseNotes = manifest.ReleaseNotes ?? string.Empty;
                updateInfo.ChangelogUrl = manifest.ChangelogUrl ?? string.Empty;

                Logger.Info($"Update available: {newVersion} ({download.AssetName})", "UpdateChecker");
            }
            catch (JsonException ex)
            {
                Logger.Error($"Version manifest parse failed: {ex.Message}", ex, "UpdateChecker");
            }
            catch (HttpRequestException ex)
            {
                Logger.Error($"Update check failed (network): {ex.Message}", ex, "UpdateChecker");
            }
            catch (TaskCanceledException)
            {
                Logger.Warning("Update check timed out", "UpdateChecker");
            }
            catch (Exception ex)
            {
                Logger.Error($"Update check failed: {ex.Message}", ex, "UpdateChecker");
            }

            return updateInfo;
        }

        /// <summary>
        /// Определяет тип сборки по размеру exe (порог 30 МБ).
        /// Full ≈ 60 МБ, Deps ≈ 9 МБ.
        /// </summary>
        public static string DetectBuildType()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    return "deps"; // fallback

                var size = new FileInfo(exePath).Length;
                return size > 30_000_000 ? "full" : "deps";
            }
            catch (Exception ex)
            {
                Logger.Warning($"DetectBuildType failed: {ex.Message}", "UpdateChecker");
                return "deps";
            }
        }

        /// <summary>
        /// Публичный доступ к текущей версии (без вызова API).
        /// </summary>
        public string GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
        }

        private static bool IsNewerVersion(string current, string latest)
        {
            if (!Version.TryParse(current, out var currentVer))
                return false;
            if (!Version.TryParse(latest, out var latestVer))
                return false;

            return latestVer > currentVer;
        }
    }
}
