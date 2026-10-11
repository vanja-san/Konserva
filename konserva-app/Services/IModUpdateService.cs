using Konserva.Models;

namespace Konserva.Services;

/// <summary>
/// Mod update checking through the repository (v1 — Modrinth).
/// </summary>
public interface IModUpdateService
{
    /// <summary>
    /// Checks for updates for the given jar files.
    /// Returns a dictionary: local file path → found update.
    /// Files without an available update are not included in the result.
    /// </summary>
    /// <param name="jarPaths">Paths to local mod jar files.</param>
    /// <param name="loader">Server loader.</param>
    /// <param name="gameVersion">Minecraft server version.</param>
    /// <param name="channel">Update channel (release/beta/alpha).</param>
    /// <param name="progress">Progress report (name of the file being processed).</param>
    /// <param name="precomputedHashes">Already computed SHA-512 hashes: path → hash. Avoids hashing the files again.</param>
    Task<IReadOnlyDictionary<string, ModUpdateInfo>> CheckForUpdatesAsync(
        IReadOnlyCollection<string> jarPaths,
        ModLoaderType loader,
        string gameVersion,
        ModUpdateChannel channel,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? precomputedHashes = null);

    /// <summary>
    /// Downloads the new version, verifies its SHA-512, makes a backup
    /// of the old file and replaces it in place (the name and .disabled state are preserved).
    /// </summary>
    /// <param name="update">The found update.</param>
    /// <param name="backupDirectory">Folder for backups (usually from ModBackup).</param>
    /// <param name="progress">Progress report (stage text).</param>
    Task<ModUpdateApplyResult> ApplyUpdateAsync(
        ModUpdateInfo update,
        string backupDirectory,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the real name and version for the given jar files from repository data.
    /// Returns a dictionary: local file path → project name and version.
    /// Files not found in the repository are not included in the result.
    /// </summary>
    /// <param name="jarPaths">Paths to local mod jar files.</param>
    /// <param name="progress">Progress report (name of the file being processed).</param>
    /// <param name="precomputedHashes">Already computed SHA-512 hashes: path → hash. Avoids hashing the files again.</param>
    Task<IReadOnlyDictionary<string, ModMetadata>> ResolveModMetadataAsync(
        IReadOnlyCollection<string> jarPaths,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? precomputedHashes = null);
}
