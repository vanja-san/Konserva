using Konserva.Models;
using Konserva.Utilities;
using System.IO;

namespace Konserva.Services;

/// <summary>
/// Проверка обновлений модов по SHA-512 хешам через Modrinth.
/// Алгоритм повторяет Mod Menu: сначала узнаём локальные версии (для даты),
/// затем — последние совместимые, и отбрасываем даунгрейды.
/// </summary>
public sealed class ModUpdateService : IModUpdateService
{
    private const int HashBatchSize = 1000;

    private readonly IModRepositoryApi _repository;
    private readonly IModFileDownloader _downloader;

    public ModUpdateService(IModRepositoryApi repository, IModFileDownloader downloader)
    {
        _repository = repository;
        _downloader = downloader;
    }

    public async Task<IReadOnlyDictionary<string, ModUpdateInfo>> CheckForUpdatesAsync(
        IReadOnlyCollection<string> jarPaths,
        ModLoaderType loader,
        string gameVersion,
        ModUpdateChannel channel,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, ModUpdateInfo>(StringComparer.OrdinalIgnoreCase);

        var loaders = ModrinthLoaderMap.ModLoaders(loader);
        if (loaders.Count == 0)
        {
            Logger.Info($"Лоадер {loader}: моды не поддерживаются, проверка пропущена", "ModUpdateService");
            return result;
        }

        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            Logger.Warning("Не указана версия Minecraft — проверка обновлений модов невозможна", "ModUpdateService");
            return result;
        }

        var versionTypes = ModUpdateChannels.ToVersionTypes(channel);
        var hashToPath = await ComputeHashesAsync(jarPaths, progress, ct).ConfigureAwait(false);
        if (hashToPath.Count == 0)
            return result;

        var allHashes = hashToPath.Keys.ToArray();
        var currentVersions = new Dictionary<string, ModrinthVersion>();
        var latestVersions = new Dictionary<string, ModrinthVersion>();

        foreach (var chunk in Chunk(allHashes, HashBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var current = await _repository.GetCurrentVersionsAsync(chunk, ct).ConfigureAwait(false);
            foreach (var (hash, version) in current)
                currentVersions[hash] = version;

            var latest = await _repository
                .GetLatestVersionsAsync(chunk, loaders, gameVersion, versionTypes, ct)
                .ConfigureAwait(false);
            foreach (var (hash, version) in latest)
                latestVersions[hash] = version;
        }

        foreach (var (hash, path) in hashToPath)
        {
            if (!latestVersions.TryGetValue(hash, out var latest))
                continue;

            var file = latest.PrimaryFile;
            var latestHash = file?.Hashes.Sha512;
            if (file is null || string.IsNullOrEmpty(latestHash))
                continue;

            // Файл не изменился — обновлять нечего
            if (string.Equals(latestHash, hash, StringComparison.OrdinalIgnoreCase))
                continue;

            // Защита от даунгрейда: последняя совместимая версия не новее локальной
            if (currentVersions.TryGetValue(hash, out var local) &&
                latest.DatePublished <= local.DatePublished)
            {
                continue;
            }

            result[path] = new ModUpdateInfo
            {
                FilePath = path,
                CurrentHash = hash,
                CurrentVersion = currentVersions.TryGetValue(hash, out var lv) ? lv.VersionNumber : null,
                VersionId = latest.Id,
                ProjectId = latest.ProjectId,
                LatestVersion = latest.VersionNumber,
                DownloadUrl = file.Url,
                DownloadFileName = file.FileName,
                LatestHash = latestHash
            };
        }

        return result;
    }

    private static async Task<Dictionary<string, string>> ComputeHashesAsync(
        IReadOnlyCollection<string> jarPaths,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var hashToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in jarPaths)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            try
            {
                progress?.Report(Path.GetFileName(path));
                var hash = await FileHash.Sha512Async(path, ct).ConfigureAwait(false);
                hashToPath[hash] = path;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Не удалось вычислить SHA-512 для {path}: {ex.Message}", "ModUpdateService");
            }
        }

        return hashToPath;
    }

    private static IEnumerable<IReadOnlyCollection<string>> Chunk(IReadOnlyList<string> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
            yield return source.Skip(i).Take(size).ToArray();
    }

    public async Task<IReadOnlyDictionary<string, ModMetadata>> ResolveModMetadataAsync(
        IReadOnlyCollection<string> jarPaths,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, ModMetadata>(StringComparer.OrdinalIgnoreCase);

        var hashToPath = await ComputeHashesAsync(jarPaths, progress, ct).ConfigureAwait(false);
        if (hashToPath.Count == 0)
            return result;

        var currentVersions = new Dictionary<string, ModrinthVersion>();

        foreach (var chunk in Chunk(hashToPath.Keys.ToArray(), HashBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var current = await _repository.GetCurrentVersionsAsync(chunk, ct).ConfigureAwait(false);
            foreach (var (hash, version) in current)
                currentVersions[hash] = version;
        }

        if (currentVersions.Count == 0)
            return result;

        var projectIds = currentVersions.Values
            .Select(v => v.ProjectId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var projects = new Dictionary<string, ModrinthProject>(StringComparer.OrdinalIgnoreCase);
        if (projectIds.Length > 0)
        {
            var fetchedProjects = await _repository.GetProjectsAsync(projectIds, ct).ConfigureAwait(false);
            foreach (var (id, project) in fetchedProjects)
                projects[id] = project;
        }

        foreach (var (hash, path) in hashToPath)
        {
            if (!currentVersions.TryGetValue(hash, out var local))
                continue;

            var title = projects.TryGetValue(local.ProjectId, out var project)
                ? project.Title
                : string.Empty;

            result[path] = new ModMetadata
            {
                Title = title,
                Version = local.VersionNumber
            };
        }

        return result;
    }

    public async Task<ModUpdateApplyResult> ApplyUpdateAsync(
        ModUpdateInfo update,
        string backupDirectory,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var oldPath = update.FilePath;
        var directory = Path.GetDirectoryName(oldPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrWhiteSpace(oldPath))
            return ModUpdateApplyResult.Fail("Не указан путь к файлу мода");

        var isDisabled = oldPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        var newFileName = isDisabled
            ? $"{update.DownloadFileName}.disabled"
            : update.DownloadFileName;
        if (string.IsNullOrWhiteSpace(newFileName))
            return ModUpdateApplyResult.Fail("Не указано новое имя файла мода");

        var targetPath = Path.Combine(directory, newFileName);
        var tempPath = Path.Combine(directory, $".{newFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            progress?.Report($"Скачивание {update.DownloadFileName}");
            await _downloader.DownloadAsync(update.DownloadUrl, tempPath, ct).ConfigureAwait(false);

            progress?.Report("Проверка целостности");
            var actualHash = await FileHash.Sha512Async(tempPath, ct).ConfigureAwait(false);
            if (!string.Equals(actualHash, update.LatestHash, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warning($"SHA-512 не совпал для {update.DownloadFileName}", "ModUpdateService");
                return ModUpdateApplyResult.Fail("Скачанный файл повреждён (SHA-512 не совпадает)");
            }

            string? backupPath = null;
            if (File.Exists(oldPath) && !string.IsNullOrWhiteSpace(backupDirectory))
            {
                progress?.Report("Резервное копирование");
                Directory.CreateDirectory(backupDirectory);
                backupPath = Path.Combine(backupDirectory, Path.GetFileName(oldPath));
                File.Copy(oldPath, backupPath, overwrite: true);
            }

            progress?.Report("Замена файла");
            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(tempPath, targetPath);

            if (!string.Equals(oldPath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                File.Delete(oldPath);

            Logger.Info($"Мод обновлён: {Path.GetFileName(oldPath)} → {newFileName} ({update.LatestVersion})", "ModUpdateService");
            return ModUpdateApplyResult.Ok(backupPath, newFileName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"Не удалось обновить {Path.GetFileName(targetPath)}", ex, "ModUpdateService");
            return ModUpdateApplyResult.Fail(ex.Message);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { /* ignored */ }
    }
}
