using System.Text.Json.Serialization;

namespace Konserva.Models;

/// <summary>
/// Modrinth project version (response of /v2/version_files and /v2/version_files/update).
/// </summary>
public class ModrinthVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("version_number")]
    public string VersionNumber { get; set; } = string.Empty;

    /// <summary>release / beta / alpha</summary>
    [JsonPropertyName("version_type")]
    public string VersionType { get; set; } = string.Empty;

    /// <summary>Compatible Minecraft versions (for example "1.20.1", "1.20").</summary>
    [JsonPropertyName("game_versions")]
    public List<string> GameVersions { get; set; } = [];

    [JsonPropertyName("date_published")]
    public DateTimeOffset DatePublished { get; set; }

    [JsonPropertyName("files")]
    public List<ModrinthFile> Files { get; set; } = [];

    /// <summary>
    /// Primary file of the version (primary). If primary is not set — the first one in the list.
    /// </summary>
    public ModrinthFile? PrimaryFile =>
        Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

/// <summary>
/// Modrinth version file.
/// </summary>
public class ModrinthFile
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("hashes")]
    public ModrinthHashes Hashes { get; set; } = new();
}

/// <summary>
/// Modrinth file hashes.
/// </summary>
public class ModrinthHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

/// <summary>
/// Modrinth project (response of /v2/projects).
/// </summary>
public class ModrinthProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

/// <summary>
/// Actual mod name and version from the repository (Modrinth).
/// </summary>
public class ModMetadata
{
    public string Title { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}
