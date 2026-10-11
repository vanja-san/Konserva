namespace Konserva.Models;

/// <summary>
/// An update for a specific mod file found in the repository.
/// </summary>
public class ModUpdateInfo
{
    /// <summary>Path to the local jar file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>SHA-512 of the local file.</summary>
    public string CurrentHash { get; set; } = string.Empty;

    /// <summary>Version matching the local file (if found in the repository).</summary>
    public string? CurrentVersion { get; set; }

    /// <summary>Version identifier in the repository.</summary>
    public string VersionId { get; set; } = string.Empty;

    /// <summary>Project identifier in the repository.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Number of the new version.</summary>
    public string LatestVersion { get; set; } = string.Empty;

    /// <summary>URL for downloading the new version.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>File name of the new version.</summary>
    public string DownloadFileName { get; set; } = string.Empty;

    /// <summary>SHA-512 of the new version (for verification after download).</summary>
    public string LatestHash { get; set; } = string.Empty;

    /// <summary>Link to the version page (to open it in a browser).</summary>
    public string VersionPageUrl => $"https://modrinth.com/project/{ProjectId}/version/{VersionId}";
}
