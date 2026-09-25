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
    private const int HashBatchSize = 250;
    private const int HashParallelism = 6;
    private const int MaxFallbackProjects = 40;

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
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? precomputedHashes = null)
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
        var hashToPath = await ComputeHashesAsync(jarPaths, progress, ct, precomputedHashes).ConfigureAwait(false);
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

        await ApplyProjectFallbackAsync(hashToPath, currentVersions, latestVersions, loaders, gameVersion, versionTypes, result, ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Fallback для модов, по которым /version_files/update не вернул кандидата,
    /// хотя локальная версия в репозитории известна. Строгий серверный фильтр
    /// иногда не находит совместимую версию (например, версия Minecraft указана
    /// как 1.20.x, а мод помечен 1.20). Сверяемся по всем версиям проекта
    /// с мягким матчингом major.minor.
    /// </summary>
    private async Task ApplyProjectFallbackAsync(
        Dictionary<string, string> hashToPath,
        Dictionary<string, ModrinthVersion> currentVersions,
        Dictionary<string, ModrinthVersion> latestVersions,
        IReadOnlyCollection<string> loaders,
        string gameVersion,
        IReadOnlyCollection<string> versionTypes,
        Dictionary<string, ModUpdateInfo> result,
        CancellationToken ct)
    {
        var pendingHashes = hashToPath.Keys
            .Where(hash => !latestVersions.ContainsKey(hash)
                           && currentVersions.TryGetValue(hash, out var local)
                           && !string.IsNullOrWhiteSpace(local.ProjectId))
            .ToArray();
        if (pendingHashes.Length == 0)
            return;

        var projectIds = pendingHashes
            .Select(hash => currentVersions[hash].ProjectId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFallbackProjects)
            .ToArray();

        var byProject = new Dictionary<string, IReadOnlyList<ModrinthVersion>>(StringComparer.OrdinalIgnoreCase);
        foreach (var projectId in projectIds)
        {
            ct.ThrowIfCancellationRequested();

            var versions = await _repository
                .GetProjectVersionsAsync(projectId, loaders, [gameVersion], versionTypes, ct)
                .ConfigureAwait(false);
            if (versions is null)
                continue;

            byProject[projectId] = versions;
        }

        foreach (var hash in pendingHashes)
        {
            ct.ThrowIfCancellationRequested();

            var local = currentVersions[hash];
            if (!byProject.TryGetValue(local.ProjectId, out var versions))
                continue;

            var candidate = versions
                .Select(v => (Version: v, File: v.PrimaryFile))
                .Where(x => x.File != null
                            && !string.IsNullOrWhiteSpace(x.File.Hashes.Sha512)
                            && !string.IsNullOrWhiteSpace(x.File.Url)
                            && !string.Equals(x.File.Hashes.Sha512, hash, StringComparison.OrdinalIgnoreCase)
                            && x.Version.DatePublished > local.DatePublished
                            && MatchesGameVersion(x.Version, gameVersion))
                .OrderByDescending(x => x.Version.DatePublished)
                .Select(x => x.Version)
                .FirstOrDefault();

            if (candidate is null)
                continue;

            var file = candidate.PrimaryFile!;
            var path = hashToPath[hash];
            result[path] = new ModUpdateInfo
            {
                FilePath = path,
                CurrentHash = hash,
                CurrentVersion = local.VersionNumber,
                VersionId = candidate.Id,
                ProjectId = candidate.ProjectId,
                LatestVersion = candidate.VersionNumber,
                DownloadUrl = file.Url,
                DownloadFileName = file.FileName,
                LatestHash = file.Hashes.Sha512!
            };
        }
    }

    private static bool MatchesGameVersion(ModrinthVersion version, string gameVersion)
    {
        if (version.GameVersions.Count == 0)
            return false;

        if (version.GameVersions.Any(g => string.Equals(g, gameVersion, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (McVersionHelper.TryParseMcVersion(gameVersion, out var major, out var minor))
            return version.GameVersions.Any(g =>
                McVersionHelper.TryParseMcVersion(g, out var gm, out var gn)
                && gm == major && gn == minor);

        return false;
    }

    private static async Task<Dictionary<string, string>> ComputeHashesAsync(
        IReadOnlyCollection<string> jarPaths,
        IProgress<string>? progress,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? precomputedHashes = null)
    {
        var hashToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pathsToHash = new List<string>();

        foreach (var path in jarPaths)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            if (precomputedHashes != null
                && precomputedHashes.TryGetValue(path, out var pre)
                && !string.IsNullOrWhiteSpace(pre))
            {
                hashToPath[pre] = path;
                continue;
            }

            pathsToHash.Add(path);
        }

        if (pathsToHash.Count > 0)
        {
            await Parallel.ForEachAsync(
                pathsToHash,
                new ParallelOptions { MaxDegreeOfParallelism = HashParallelism, CancellationToken = ct },
                async (path, token) =>
                {
                    try
                    {
                        progress?.Report(Path.GetFileName(path));
                        var hash = await FileHash.Sha512Async(path, token).ConfigureAwait(false);
                        lock (hashToPath)
                        {
                            hashToPath[hash] = path;
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Не удалось вычислить SHA-512 для {path}: {ex.Message}", "ModUpdateService");
                    }
                }).ConfigureAwait(false);
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
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? precomputedHashes = null)
    {
        var result = new Dictionary<string, ModMetadata>(StringComparer.OrdinalIgnoreCase);

        var hashToPath = await ComputeHashesAsync(jarPaths, progress, ct, precomputedHashes).ConfigureAwait(false);
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
