using Konserva.Localization;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Konserva.Services;

/// <summary>
/// Установка Forge сервера: получение URL, загрузка установщика, запуск
/// </summary>
public partial class McServerInstaller
{
    /// <summary>
    /// Получить URL Forge installer
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
            var downloadsDir = Constants.DownloadsPath;
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
                await WaitForForgeStabilityAsync(destinationPath, forgeVersion, ct, progress);
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
            var lastReportedLibraryCount = 0;
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

                    // Сообщаем прогресс раз в 50 библиотек
                    if (libraryCount > 0 && libraryCount - lastReportedLibraryCount >= 50)
                    {
                        lastReportedLibraryCount = libraryCount;
                        progress?.Report($"{LocalizationManager.Get("Installer_Installing")} ({libraryCount} libraries)");
                    }
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

    /// <summary>
    /// Ожидание стабилизации файлов после установки Forge
    /// </summary>
    private async Task WaitForForgeStabilityAsync(string destinationPath, string forgeVersion, CancellationToken ct, IProgress<string>? progress = null)
    {
        Logger.Info("Waiting for file operations to complete...", "McServerInstaller");
        progress?.Report(LocalizationManager.Get("Installer_Finishing"));

        var waitStartTime = SystemTime.Now;
        var maxWaitTime = TimeSpan.FromSeconds(60);
        var minWait = TimeSpan.FromSeconds(3);
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

            if (!hasMinWaitElapsed && (SystemTime.Now - waitStartTime) < minWait)
            {
                await Task.Delay(500, ct);
                continue;
            }
            hasMinWaitElapsed = true;

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
                Logger.Info($"Forge files unlocked but no run.bat, waiting for more files...", "McServerInstaller");
                await Task.Delay(1000, ct);
                if ((SystemTime.Now - waitStartTime) > TimeSpan.FromSeconds(10))
                {
                    Logger.Info("Exiting Forge stability wait (10s elapsed, files unlocked)", "McServerInstaller");
                    break;
                }
            }

            await Task.Delay(500, ct);
        }

        // Копируем forge universal jar из libraries в корень
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
}
