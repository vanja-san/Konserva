using System.Text.Json.Serialization;

namespace Konserva.Models;

/// <summary>
/// Версия проекта Modrinth (ответ /v2/version_files и /v2/version_files/update).
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

    [JsonPropertyName("date_published")]
    public DateTimeOffset DatePublished { get; set; }

    [JsonPropertyName("files")]
    public List<ModrinthFile> Files { get; set; } = [];

    /// <summary>
    /// Основной файл версии (primary). Если primary не задан — первый в списке.
    /// </summary>
    public ModrinthFile? PrimaryFile =>
        Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

/// <summary>
/// Файл версии Modrinth.
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
/// Хеши файла Modrinth.
/// </summary>
public class ModrinthHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

/// <summary>
/// Проект Modrinth (ответ /v2/projects).
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
/// Реальные название и версия мода из репозитория (Modrinth).
/// </summary>
public class ModMetadata
{
    public string Title { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}
