using System.Text.Json.Serialization;

namespace Konserva.Models;

/// <summary>
/// Version manifest from version.json (hosted on raw.githubusercontent.com).
/// Does not use the GitHub API — no rate limit.
/// </summary>
public class VersionManifest
{
    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = string.Empty;

    [JsonPropertyName("minRequiredVersion")]
    public string MinRequiredVersion { get; set; } = string.Empty;

    [JsonPropertyName("downloads")]
    public Dictionary<string, VersionDownload>? Downloads { get; set; }

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("changelogUrl")]
    public string ChangelogUrl { get; set; } = string.Empty;
}

/// <summary>
/// Information about a file for download.
/// </summary>
public class VersionDownload
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("assetName")]
    public string AssetName { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the asset as 64 hex characters. Required: without it
    /// <see cref="Services.AppUpdater"/> refuses to apply the update.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
