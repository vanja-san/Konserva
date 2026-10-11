using CommunityToolkit.Mvvm.ComponentModel;

namespace Konserva.Models;

/// <summary>
/// Information about a mod
/// </summary>
public partial class ModItem : ObservableObject, IItemEntry
{
    /// <summary>
    /// Mod name (without extension)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersion))]
    private string _name = string.Empty;

    /// <summary>
    /// Mod version
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersion))]
    [NotifyPropertyChangedFor(nameof(HasVersionDisplay))]
    [NotifyPropertyChangedFor(nameof(VersionDisplay))]
    private string _version = string.Empty;

    /// <summary>
    /// Whether to show the version next to the name (not empty)
    /// </summary>
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);

    /// <summary>
    /// Whether to show the version line (current or "current -> new")
    /// </summary>
    public bool HasVersionDisplay => HasVersion || UpdateAvailable;

    /// <summary>
    /// Version to display: the current one, or "current -> new" when an update is available
    /// </summary>
    public string VersionDisplay => UpdateAvailable && LatestVersion != null
        ? (HasVersion ? $"{Version} -> {LatestVersion}" : LatestVersion)
        : Version;

    /// <summary>
    /// File name (for example "OptiFine.jar")
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the file (may be .jar or .jar.disabled)
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Whether the mod is enabled (.jar) or disabled (.jar.disabled)
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>
    /// Whether an update is available for this mod
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    [NotifyPropertyChangedFor(nameof(HasVersionDisplay))]
    [NotifyPropertyChangedFor(nameof(VersionDisplay))]
    private bool _updateAvailable;

    /// <summary>
    /// Whether this mod is currently being updated
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotUpdating))]
    private bool _isUpdating;

    /// <summary>
    /// No update in progress and no successful result (the button is visible)
    /// </summary>
    public bool IsNotUpdating => !IsUpdating && !UpdateSucceeded;

    /// <summary>
    /// Whether the update completed successfully (show the check mark)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotUpdating))]
    private bool _updateSucceeded;

    /// <summary>
    /// Version number of the available new version
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateTooltip))]
    [NotifyPropertyChangedFor(nameof(HasVersionDisplay))]
    [NotifyPropertyChangedFor(nameof(VersionDisplay))]
    private string? _latestVersion;

    /// <summary>
    /// Update button tooltip: "Update to vX.X.X"
    /// </summary>
    public string? UpdateTooltip => LatestVersion != null
        ? $"{Localization.LocalizationManager.Get("ServerDetail_Mods_UpdateTo")} v{LatestVersion}"
        : null;
}
