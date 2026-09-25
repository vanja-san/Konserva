using Konserva.Models;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;

namespace Konserva.Services
{
    /// <summary>
    /// Скачивает обновление, распаковывает и заменяет файлы через батник.
    /// </summary>
    public sealed class AppUpdater : IAppUpdater
    {
        private readonly HttpClient _client;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private static readonly string UpdateLogPath = Path.Combine(AppContext.BaseDirectory, "Logs", "Update.log");

        public AppUpdater(HttpClient httpClient)
        {
            _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Выполняет обновление: скачивает ZIP, распаковывает, запускает батник, закрывает приложение.
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
                // Шаг 0: Проверяем, что манифест вообще содержит контрольную сумму.
                // Без неё мы не можем отличить подменённый ассет от повреждённого,
                // поэтому применять такой апдейт нельзя.
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

                // Шаг 1: Подготовка
                progress?.Report(10);
                UpdateLog($"Starting update to {updateInfo.NewVersion}");

                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                // Шаг 2: Скачивание
                progress?.Report(20);
                UpdateLog($"Downloading {updateInfo.AssetName} ({FormatSize(updateInfo.SizeBytes)})");

                await DownloadFileAsync(updateInfo.DownloadUrl, zipPath, updateInfo.CurrentVersion, progress);

                // Шаг 2.5: Обязательная проверка целостности скачанного архива
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

                // Шаг 3: Распаковка
                progress?.Report(90);
                UpdateLog("Extracting archive...");

                var extractDir = Path.Combine(tempDir, "extracted");
                await ZipFile.ExtractToDirectoryAsync(zipPath, extractDir, CancellationToken.None);

                // Шаг 4: Создание батника (вне tempDir, чтобы можно было удалить всю папку)
                UpdateLog("Creating update script...");

                var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var batchPath = Path.Combine(downloadDir, $"KonservaUpdate_{Guid.NewGuid():N}.bat");
                CreateUpdateScript(tempDir, appDir, batchPath);

                // Шаг 5: Запуск батника и закрытие
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
        /// Создаёт batch-скрипт для замены файлов.
        /// Пути экранируются от batch-метасимволов (&, |, %, ^, ! и др.).
        /// Батник создаётся вне tempDir, чтобы можно было удалить всю временную папку целиком.
        /// </summary>
        private void CreateUpdateScript(string tempDir, string appDir, string batchPath)
        {
            var extractedDir = Path.Combine(tempDir, "extracted");

            // Санитизируем пути для batch-скриптов — экранируем &, |, %, ^, ! и др.
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
        /// Валидирует формат контрольной суммы: ровно 64 hex-символа.
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
        /// Считает SHA-256 файла и сравнивает с ожидаемым (регистронезависимо).
        /// При несовпадении архив удаляется и выбрасывается <see cref="InvalidDataException"/>.
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
        /// Скачивает файл по указанному URL. Все ресурсы освобождаются до возврата.
        /// </summary>
        private async Task DownloadFileAsync(string url, string destPath, string currentVersion, IProgress<double>? progress)
        {
            _client.DefaultRequestHeaders.UserAgent.ParseAdd($"Konserva/{currentVersion}");

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var canReportProgress = totalBytes != -1;

            // Копируем в отдельный scope — stream закроется до выхода из метода
            using (var contentStream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalRead += bytesRead;

                    if (canReportProgress)
                    {
                        var downloadProgress = 20 + (totalRead * 70 / totalBytes);
                        progress?.Report((int)downloadProgress);
                    }
                }

                await fileStream.FlushAsync();
            }
        }

        /// <summary>
        /// Логирует сообщение в Update.log.
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
