using Konserva.Models;

namespace Konserva.Services;

/// <summary>
/// Repository of mod versions. v1 — Modrinth only (public endpoints, no API key).
/// </summary>
public interface IModRepositoryApi
{
    /// <summary>
    /// Current (local) versions by file SHA-512 hashes.
    /// Needed for date_published to tell a real update apart from a downgrade.
    /// The dictionary key is the hash that was passed in.
    /// </summary>
    Task<IReadOnlyDictionary<string, ModrinthVersion>> GetCurrentVersionsAsync(
        IReadOnlyCollection<string> hashes,
        CancellationToken ct = default);

    /// <summary>
    /// Latest versions compatible with the given loaders, game version and channels.
    /// Files without a compatible version are not included in the response. The dictionary key is the hash that was passed in.
    /// </summary>
    Task<IReadOnlyDictionary<string, ModrinthVersion>> GetLatestVersionsAsync(
        IReadOnlyCollection<string> hashes,
        IReadOnlyCollection<string> loaders,
        string gameVersion,
        IReadOnlyCollection<string> versionTypes,
        CancellationToken ct = default);

    /// <summary>
    /// Modrinth projects by their id (GET /v2/projects?ids=[...]).
    /// Returns a dictionary: project id → project (needed to resolve the real mod names).
    /// </summary>
    Task<IReadOnlyDictionary<string, ModrinthProject>> GetProjectsAsync(
        IReadOnlyCollection<string> projectIds,
        CancellationToken ct = default);

    /// <summary>
    /// All versions of a project with the given filters (GET /v2/project/{id}/version).
    /// Used as a fallback when /version_files/update returns no candidate
    /// because of strict compatibility matching. Newer versions usually come first.
    /// </summary>
    Task<IReadOnlyList<ModrinthVersion>> GetProjectVersionsAsync(
        string projectId,
        IReadOnlyCollection<string> loaders,
        IReadOnlyCollection<string> gameVersions,
        IReadOnlyCollection<string> versionTypes,
        CancellationToken ct = default);
}
