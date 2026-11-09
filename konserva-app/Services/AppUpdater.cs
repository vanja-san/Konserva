using Konserva.Models;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;

namespace Konserva.Services
{
    /// <summary>
    /// Downloads the update, unpacks it and replaces the files via a batch script.
    /// </summary>
    public sealed class AppUpdater : IAppUpdater
    {
        private readonly HttpClient _client;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private static readonly string UpdateLogPath = Path.Combine(AppContext.BaseDirectory, "Logs", "Update.log");
        private const int MaxRedirects = 5;

        public AppUpdater(HttpClient httpClient)
        {
            _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Performs the update: downloads the ZIP, unpacks it, launches the batch script, closes the application.
        /// </summary>
        public async Task<bool> ApplyAsync(UpdateInfo updateInfo, IProgress<double>? progress = null)
        {
            if (!await _lock.WaitAsync(0))
            {
                UpdateLog("Update already in progress", "WARNING");
                return false;
            }

            try
            {
                // Step 0: Make sure the manifest actually contains a checksum.
                // Without it we cannot tell a tampered asset apart from a corrupted one,
                // so such an update must not be applied.
                if (!IsValidSha256(updateInfo.Sha256))
                {
                    const string reason =
                        "version.json does not contain a valid 'sha256' for the selected build; refusing to install an unverified update";
                    Logger.Error($"App update aborted: {reason}", null, "AppUpdater");
                    UpdateLog($"Update aborted: {reason}", "ERROR");
                    return false;
                }

                var downloadDir = Path.Combine(AppContext.BaseDirectory, "downloads");
                var tempDir = Path.Combine(downloadDir, "KonservaUpdate");
                var zipPath = Path.Combine(tempDir, "update.zip");

                // Step 1: Preparation
                progress?.Report(10);
                UpdateLog($"Starting update to {updateInfo.NewVersion}");

                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                // Step 2: Downloading
                progress?.Report(20);
                UpdateLog($"Downloading {updateInfo.AssetName} ({FormatSize(updateInfo.SizeBytes)})");

                await DownloadFileAsync(updateInfo.DownloadUrl, zipPath, updateInfo.CurrentVersion, progress);

                // Step 2.5: Mandatory integrity check of the downloaded archive
                UpdateLog("Verifying SHA-256...");
                await VerifySha256Async(zipPath, updateInfo.Sha256);

                if (updateInfo.SizeBytes > 0)
                {
                    var actualSize = new FileInfo(zipPath).Length;
                    if (actualSize != updateInfo.SizeBytes)
                    {
                        var reason = $"size mismatch: expected {updateInfo.SizeBytes} bytes, got {actualSize}";
                        UpdateLog($"Update aborted: {reason}", "ERROR");
                        Logger.Error($"App update aborted: {reason}", null, "AppUpdater");
                        TryDeleteFile(zipPath);
                        return false;
                    }
                }

                // Step 3: Unpacking
                progress?.Report(90);
                UpdateLog("Extracting archive...");

                var extractDir = Path.Combine(tempDir, "extracted");
                await ZipFile.ExtractToDirectoryAsync(zipPath, extractDir, CancellationToken.None);

                // Step 4: Creating the batch script (outside tempDir so the whole folder can be deleted)
                UpdateLog("Creating update script...");

                var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var batchPath = Path.Combine(downloadDir, $"KonservaUpdate_{Guid.NewGuid():N}.bat");
                CreateUpdateScript(tempDir, appDir, batchPath);

                // Step 5: Launching the batch script and shutting down
                progress?.Report(100);
                UpdateLog("Launching update script and restarting...");

                Process.Start(new ProcessStartInfo
                {
                    FileName = batchPath,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"App update failed: {ex.Message}", ex, "AppUpdater");
                UpdateLog($"Update failed: {ex.Message}", "ERROR");
                UpdateLog($"Stack trace: {ex.StackTrace}", "ERROR");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Creates a batch script that replaces the files.
        /// Paths are escaped from batch metacharacters (&, |, %, ^, ! and others).
        /// The batch script is created outside tempDir so the entire temporary folder can be deleted.
        /// </summary>
        private void CreateUpdateScript(string tempDir, string appDir, string batchPath)
        {
            var extractedDir = Path.Combine(tempDir, "extracted");

            // Sanitize paths for batch scripts — escape &, |, %, ^, ! and others.
            var extractedEscaped = PathValidator.SanitizeForBatch(extractedDir);
            var tempDirEscaped = PathValidator.SanitizeForBatch(tempDir);
            var appEscaped = PathValidator.SanitizeForBatch(appDir);

            var batchContent = $@"@echo off
setlocal enabledelayedexpansion

REM Wait for app to close
timeout /t 2 /nobreak >nul

REM Delete i18n folder (old translations may have changed keys)
if exist ""{appEscaped}\i18n"" rd /s /q ""{appEscaped}\i18n""

REM Copy all files and folders except Servers and config.json
for /D %%D in (""{extractedEscaped}\*"") do (
    set ""folderName=%%~nxD""
    if /i not ""!folderName!""==""Servers"" (
        xcopy ""%%D"" ""{appEscaped}\%%~nxD\"" /E /Y /I /Q >nul
    )
)

REM Copy individual files (skip config.json)
for %%F in (""{extractedEscaped}\*.*"") do (
    set ""fileName=%%~nxF""
    if /i not ""!fileName!""==""config.json"" (
        copy /y ""%%F"" ""{appEscaped}\"" >nul
    )
)

REM Wait for copy to complete
timeout /t 1 /nobreak >nul

REM Delete entire temp update folder (zip + extracted + leftovers)
if exist ""{tempDirEscaped}"" rd /s /q ""{tempDirEscaped}""

REM Restart application
start """" ""{appEscaped}\Konserva.exe""

REM Self-delete this batch file
(goto) 2>nul & del ""%~f0""

endlocal
exit
";

            File.WriteAllText(batchPath, batchContent, new System.Text.UTF8Encoding(true));
        }

        /// <summary>
        /// Validates the checksum format: exactly 64 hex characters.
        /// </summary>
        private static bool IsValidSha256(string? sha256)
        {
            if (string.IsNullOrWhiteSpace(sha256))
                return false;

            var trimmed = sha256.Trim();
            if (trimmed.Length != 64)
                return false;

            foreach (var c in trimmed)
            {
                var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
                if (!isHex)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Computes the SHA-256 of a file and compares it with the expected value (case-insensitive).
        /// On mismatch the archive is deleted and an <see cref="InvalidDataException"/> is thrown.
        /// </summary>
        private static async Task VerifySha256Async(string filePath, string expectedSha256)
        {
            byte[] actual;

            await using (var stream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                actual = await SHA256.HashDataAsync(stream);
            }

            var actualHex = Convert.ToHexString(actual);
            var expectedHex = expectedSha256.Trim().ToUpperInvariant();

            if (string.Equals(actualHex, expectedHex, StringComparison.Ordinal))
            {
                UpdateLog($"SHA-256 OK: {actualHex}");
                return;
            }

            TryDeleteFile(filePath);
            throw new InvalidDataException(
                $"SHA-256 mismatch. Expected {expectedHex}, got {actualHex}");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppUpdater] Failed to delete {path}: {ex.Message}");
            }
        }

        /// <summary>
        /// Downloads a file from the given URL, following HTTPS redirects. All resources
        /// are released before returning.
        /// </summary>
        private async Task DownloadFileAsync(string url, string destPath, string currentVersion, IProgress<double>? progress)
        {
            // The URL arrives from version.json. Only HTTPS is accepted: the reference SHA-256
            // travels in the same unsigned document, so an http:// URL would let an on-path
            // attacker serve both the payload and a matching checksum.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException($"Refusing to download the update over a non-HTTPS URL: {url}");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

            // GitHub Releases answers with 302 to its asset CDN, so redirects are followed
            // manually: the handler refuses them automatically, which keeps every hop
            // visible and lets us reject an https -> http downgrade.
            using var response = await SendWithRedirectsAsync(uri, currentVersion, cts.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var canReportProgress = totalBytes != -1;

            using (var contentStream = await response.Content.ReadAsStreamAsync(cts.Token))
            using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cts.Token);
                    totalRead += bytesRead;

                    if (canReportProgress)
                    {
                        var downloadProgress = 20 + (totalRead * 70 / totalBytes);
                        progress?.Report((int)downloadProgress);
                    }
                }

                await fileStream.FlushAsync(cts.Token);
            }
        }

        /// <summary>
        /// Sends the request and follows at most <see cref="MaxRedirects"/> redirects,
        /// requiring every hop to stay on HTTPS.
        /// </summary>
        private async Task<HttpResponseMessage> SendWithRedirectsAsync(Uri uri, string currentVersion, CancellationToken ct)
        {
            for (var redirects = 0; ; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd($"Konserva/{currentVersion}");

                var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!IsRedirect(response.StatusCode))
                    return response;

                if (redirects >= MaxRedirects)
                {
                    response.Dispose();
                    throw new HttpRequestException($"Too many redirects while downloading the update (limit {MaxRedirects})");
                }

                var location = response.Headers.Location;
                if (location == null)
                {
                    response.Dispose();
                    throw new HttpRequestException("Redirect response has no Location header");
                }

                var nextUri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (nextUri.Scheme != Uri.UriSchemeHttps)
                {
                    response.Dispose();
                    throw new InvalidOperationException($"Refusing to follow a non-HTTPS update redirect: {nextUri}");
                }

                response.Dispose();
                uri = nextUri;
            }
        }

        private static bool IsRedirect(HttpStatusCode statusCode) =>
            statusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;

        /// <summary>
        /// Logs a message to Update.log.
        /// </summary>
        private static void UpdateLog(string message, string level = "INFO")
        {
            try
            {
                var dir = Path.GetDirectoryName(UpdateLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var line = $"[{SystemTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{level}] {message}";
                File.AppendAllText(UpdateLogPath, line + Environment.NewLine, new System.Text.UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppUpdater] UpdateLog error: {ex.Message}");
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
