using System.Text.Json.Serialization;

namespace Konserva.Models;

/// <summary>
/// Манифест версии из version.json (раздаётся через raw.githubusercontent.com).
/// Не использует GitHub API — нет rate limit.
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
/// Информация о файле для скачивания.
/// </summary>
public class VersionDownload
{
  [JsonPropertyName("url")]
  public string Url { get; set; } = string.Empty;

  [JsonPropertyName("sizeBytes")]
  public long SizeBytes { get; set; }

  [JsonPropertyName("assetName")]
  public string AssetName { get; set; } = string.Empty;
}
