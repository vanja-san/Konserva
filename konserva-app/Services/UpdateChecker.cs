using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Konserva.Models;
using Konserva.Utilities;
using static Konserva.Models.ApiUrls;

namespace Konserva.Services
{
    /// <summary>
    /// Проверяет наличие обновлений через GitHub Releases API.
    /// </summary>
    public static class UpdateChecker
    {
        private static HttpClient? _client;

        /// <summary>
        /// Инициализация с HttpClient из DI (вызывается при старте приложения).
        /// </summary>
        public static void Initialize(HttpClient httpClient)
        {
            _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Проверяет наличие обновления. Тип сборки определяется автоматически по размеру exe.
        /// </summary>
        public static async Task<UpdateInfo> CheckAsync()
        {
            var currentVersion = GetCurrentVersion();
            var buildType = DetectBuildType();
            var updateInfo = new UpdateInfo { CurrentVersion = currentVersion };

            try
            {
                var client = _client ?? new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"Konserva/{currentVersion}");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var response = await client.GetAsync(GitHubReleasesLatest, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"Update check failed with status {response.StatusCode}", "UpdateChecker");
                    return updateInfo;
                }

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonDocument.Parse(json).RootElement;

                var tagName = release.GetProperty("tag_name").GetString();
                if (string.IsNullOrEmpty(tagName))
                    return updateInfo;

                // Убираем префикс 'v' если есть
                var newVersion = tagName.TrimStart('v');

                if (!IsNewerVersion(currentVersion, newVersion))
                    return updateInfo;

                // Ищем нужный ассет по buildType
                var (assetName, downloadUrl, sizeBytes) = FindAsset(release, buildType);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Logger.Warning($"No matching asset found for build type '{buildType}'", "UpdateChecker");
                    return updateInfo;
                }

                var body = release.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : string.Empty;
                var htmlUrl = release.TryGetProperty("html_url", out var htmlProp) ? htmlProp.GetString() : string.Empty;

                updateInfo.IsAvailable = true;
                updateInfo.NewVersion = newVersion;
                updateInfo.AssetName = assetName;
                updateInfo.DownloadUrl = downloadUrl;
                updateInfo.SizeBytes = sizeBytes;
                updateInfo.ReleaseNotes = body ?? string.Empty;
                updateInfo.ChangelogUrl = htmlUrl ?? string.Empty;

                Logger.Info($"Update available: {newVersion} ({assetName})", "UpdateChecker");
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
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
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

        private static string GetCurrentVersion()
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

        private static (string name, string url, long size) FindAsset(JsonElement release, string buildType)
        {
            if (!release.TryGetProperty("assets", out var assets))
                return (string.Empty, string.Empty, 0);

            var suffix = buildType.Equals("full", StringComparison.OrdinalIgnoreCase) ? "-full" : "-deps";

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : string.Empty;
                if (string.IsNullOrEmpty(name))
                    continue;

                // Ищем .zip с нужным суффиксом
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;

                var lowerName = name.ToLowerInvariant();
                if (lowerName.Contains(suffix.ToLowerInvariant()))
                {
                    var url = asset.TryGetProperty("browser_download_url", out var urlProp)
                        ? urlProp.GetString()
                        : string.Empty;
                    var size = asset.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0L;

                    return (name, url ?? string.Empty, size);
                }
            }

            return (string.Empty, string.Empty, 0);
        }
    }
}
