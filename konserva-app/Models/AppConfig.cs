using System.Collections.ObjectModel;
using System.IO;

namespace Konserva.Models;

/// <summary>
/// Application configuration
/// </summary>
public class AppConfig
{
    public string Theme { get; set; } = "System";
    public string Language { get; set; } = "System";

    /// <summary>
    /// List of installed Java installations
    /// </summary>
    public ObservableCollection<JavaInstallation> JavaInstallations { get; set; } = [];

    /// <summary>
    /// ID of the default Java installation
    /// </summary>
    public string? DefaultJavaId { get; set; }

    /// <summary>
    /// Path to the default Java executable (for backward compatibility)
    /// </summary>
    public string DefaultJavaPath
    {
        get => string.IsNullOrEmpty(field) ? "java" : field;
        set => field = value;
    }

    public string ServersDirectory
    {
        get => string.IsNullOrEmpty(field) ? Path.Combine(AppContext.BaseDirectory, "Servers") : field;
        set => field = value;
    }

    public int DefaultRamMin { get; set; } = 1024;
    public int DefaultRamMax { get; set; } = 4096;
    public bool CheckUpdates { get; set; } = false;

    /// <summary>
    /// Update check interval in hours (1–168, default 24).
    /// </summary>
    public int UpdateCheckIntervalHours { get; set; } = 24;

    /// <summary>
    /// Minimize-to-tray mode: None, OnClose, OnMinimize, Always
    /// </summary>
    public string MinimizeToTrayMode { get; set; } = "None";

    /// <summary>
    /// Download source: VanillaApi (official) or BMCLAPI (mirror)
    /// </summary>
    public string DownloadSource { get; set; } = "VanillaApi";

    /// <summary>
    /// Auto-scroll the console to the last line when new log lines appear
    /// </summary>
    public bool ConsoleAutoScroll { get; set; } = true;

    /// <summary>
    /// Word wrap in the server console
    /// </summary>
    public bool ConsoleWordWrap { get; set; }

    /// <summary>
    /// Last update check (UTC).
    /// </summary>
    public DateTime? LastUpdateCheck { get; set; }

    public List<string> RecentServers { get; set; } = [];

    /// <summary>
    /// Path to the configuration directory (next to the exe)
    /// </summary>
    public static string ConfigDirectory
    {
        get
        {
            return AppContext.BaseDirectory;
        }
    }

    /// <summary>
    /// Gets the default Java installation
    /// </summary>
    public JavaInstallation? GetDefaultJava()
    {
        if (string.IsNullOrEmpty(DefaultJavaId))
        {
            return JavaInstallations.FirstOrDefault(j => j.IsDefault)
                ?? JavaInstallations.FirstOrDefault();
        }

        return JavaInstallations.FirstOrDefault(j => j.Id == DefaultJavaId);
    }

    /// <summary>
    /// Gets the path to the default Java executable
    /// </summary>
    public string GetDefaultJavaPath() => GetDefaultJava()?.Path ?? DefaultJavaPath;
}