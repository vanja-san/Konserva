using CommunityToolkit.Mvvm.ComponentModel;

namespace Konserva.Models;

/// <summary>
/// Information about a plugin
/// </summary>
public partial class PluginItem : ObservableObject, IItemEntry
{
    /// <summary>
    /// Plugin name (without extension)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Plugin version
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// File name (for example "Essentials.jar")
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
    /// Whether the plugin is enabled (.jar) or disabled (.jar.disabled)
    /// </summary>
    [ObservableProperty]
    private bool _enabled = true;
}
