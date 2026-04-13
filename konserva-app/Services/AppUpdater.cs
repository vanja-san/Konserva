using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Konserva.Models;
using Konserva.Utilities;

namespace Konserva.Services
{
    /// <summary>
    /// Скачивает обновление, распаковывает и заменяет файлы через батник.
    /// </summary>
    public static class AppUpdater
    {
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private static readonly string UpdateLogPath = Path.Combine(AppContext.BaseDirectory, "Logs", "Update.log");

        /// <summary>
        /// Выполняет обновление: скачивает ZIP, распаковывает, запускает батник, закрывает приложение.
        /// </summary>
        public static async Task<bool> ApplyAsync(UpdateInfo updateInfo, IProgress<double>? progress = null)
        {
            if (!await _lock.WaitAsync(0))
            {
                UpdateLog("Update already in progress", "WARNING");
                return false;
            }

            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "KonservaUpdate");
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

                // Шаг 3: Распаковка
                progress?.Report(90);
                UpdateLog("Extracting archive...");

                var extractDir = Path.Combine(tempDir, "extracted");
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                // Шаг 4: Создание батника
                UpdateLog("Creating update script...");

                var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                var batchPath = CreateUpdateScript(tempDir, appDir);

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
        /// Создаёт update.bat для замены файлов.
        /// Пути экранируются от batch-метасимволов (&, |, %, ^, ! и др.).
        /// </summary>
        private static string CreateUpdateScript(string tempDir, string appDir)
        {
            var extractedDir = Path.Combine(tempDir, "extracted");
            var batchPath = Path.Combine(tempDir, "update.bat");

            // Санитизируем пути для batch-скриптов — экранируем &, |, %, ^, ! и др.
            var tempEscaped = PathValidator.SanitizeForBatch(extractedDir);
            var appEscaped = PathValidator.SanitizeForBatch(appDir);

            var batchContent = $@"@echo off
setlocal enabledelayedexpansion

REM Wait for app to close
timeout /t 2 /nobreak >nul

REM Delete i18n folder (old translations may have changed keys)
if exist ""{appEscaped}\i18n"" rd /s /q ""{appEscaped}\i18n""

REM Copy all files and folders except Servers and config.json
for /D %%D in (""{tempEscaped}\*"") do (
    set ""folderName=%%~nxD""
    if /i not ""!folderName!""==""Servers"" (
        xcopy ""%%D"" ""{appEscaped}\%%~nxD\"" /E /Y /I /Q >nul
    )
)

REM Copy individual files (skip config.json)
for %%F in (""{tempEscaped}\*.*"") do (
    set ""fileName=%%~nxF""
    if /i not ""!fileName!""==""config.json"" (
        copy /y ""%%F"" ""{appEscaped}\"" >nul
    )
)

REM Clean up temp
rd /s /q ""{tempEscaped}""

REM Wait for copy to complete
timeout /t 1 /nobreak >nul

REM Restart application
start """" ""{appEscaped}\Konserva.exe""

endlocal
exit
";

            File.WriteAllText(batchPath, batchContent, new System.Text.UTF8Encoding(true));
            return batchPath;
        }

        /// <summary>
        /// Скачивает файл по указанному URL. Все ресурсы освобождаются до возврата.
        /// </summary>
        private static async Task DownloadFileAsync(string url, string destPath, string currentVersion, IProgress<double>? progress)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"Konserva/{currentVersion}");

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
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

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}][{level}] {message}";
                File.AppendAllText(UpdateLogPath, line + Environment.NewLine, new System.Text.UTF8Encoding(true));
            }
            catch
            {
                // Не критично — основной лог всё равно запишет
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
