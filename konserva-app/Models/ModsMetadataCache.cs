namespace Konserva.Models;

/// <summary>
/// Cache of mod metadata (names, versions and updates) to avoid going
/// online every time the tab is opened and after application restart.
/// </summary>
public class ModsMetadataCacheData
{
    /// <summary>Time of the last actual update check (UTC).</summary>
    public DateTime LastUpdatesCheckUtc { get; set; }

    /// <summary>Mod names/versions: file name → metadata.</summary>
    public Dictionary<string, ModMetadataCacheEntry> Titles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>File inventory as of the last check: file name → size.</summary>
    public Dictionary<string, long> ModFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Found updates: file name → update data (without versions — "Updates" holds those).</summary>
    public Dictionary<string, ModUpdateCacheEntry> Updates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Cached mod name and version, valid for a file of the specified size.
/// </summary>
public class ModMetadataCacheEntry
{
    public long FileSize { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// SHA-512 of the local file (lowercase hex, Modrinth format).
    /// Allows skipping hash recalculation every time the tab is opened.
    /// </summary>
    public string Sha512 { get; set; } = string.Empty;
}

/// <summary>
/// Cached mod update, valid for a file of the specified size.
/// </summary>
public class ModUpdateCacheEntry
{
    public long FileSize { get; set; }

    public ModUpdateInfo Update { get; set; } = new();
}