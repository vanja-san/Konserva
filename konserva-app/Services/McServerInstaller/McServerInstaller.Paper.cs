using Konserva.Utilities;
using System.Text.Json;

namespace Konserva.Services;

/// <summary>
/// Скачивание Paper сервера через PaperMC Downloads Service v3
/// </summary>
public partial class McServerInstaller
{
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

            using var doc = await FetchJsonAsync(apiUrl, ct);
            Logger.Info($"Paper API response: doc loaded", "McServerInstaller");
            // v3 возвращает JSON-массив build'ов в порядке убывания (новейший — первый)
            var builds = doc.RootElement.EnumerateArray().ToArray();

            JsonElement selectedBuild = default;

            // Если указан конкретный номер сборки — ищем её
            if (!string.IsNullOrEmpty(loaderVersion))
            {
                var buildNumberStr = loaderVersion;
                var alphaIdx = loaderVersion.IndexOf(" (ALPHA)", StringComparison.OrdinalIgnoreCase);
                if (alphaIdx > 0)
                    buildNumberStr = loaderVersion[..alphaIdx];

                if (int.TryParse(buildNumberStr, out var targetBuild))
                {
                    selectedBuild = builds.FirstOrDefault(b =>
                        b.TryGetProperty("id", out var id) && id.GetInt32() == targetBuild);

                    if (selectedBuild.ValueKind != JsonValueKind.Undefined)
                        Logger.Info($"Using specified Paper build #{targetBuild}", "McServerInstaller");
                    else
                        Logger.Warning($"Specified Paper build #{targetBuild} not found, falling back to latest", "McServerInstaller");
                }
            }

            // Если сборка не найдена или не указана — ищем автоматически
            if (selectedBuild.ValueKind == JsonValueKind.Undefined)
            {
                selectedBuild = builds.FirstOrDefault(b =>
                    b.TryGetProperty("channel", out var ch) && ch.GetString() == "STABLE");

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

            Logger.Info($"Paper build for {version}: #{buildNumber}", "McServerInstaller");
            Logger.Info($"Downloading Paper from: {downloadUrl}", "McServerInstaller");

            var (success, error) = await DownloadFile(downloadUrl, destinationPath, "server.jar", progress, ct);

            if (success)
            {
                Logger.Info("Paper server downloaded successfully", "McServerInstaller");
                result.Success = true;
                result.BuildNumber = buildNumber;
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
}
