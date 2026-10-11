using System.Collections.ObjectModel;
using System.IO;

namespace Konserva.Models;

/// <summary>
/// Application configuration
/// </summary>
public class AppConfig
{
    // JavaInstallations is an ObservableCollection because the settings UI binds to it
    // directly, but it is also read from the server-start thread (Java selection) while
    // the UI thread mutates it during a Java rescan. Enumerating a collection mid-mutation
    // throws InvalidOperationException, which surfaced as "Collection was modified" and
    // aborted the server start with a misleading critical-error dialog.
    private readonly Lock _javaLock = new();

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
    /// Returns a stable snapshot of the installed Java list.
    /// <para>
    /// Safe to enumerate from any thread: the copy is taken under the lock, so a
    /// concurrent rescan on the UI thread cannot invalidate the enumeration.
    /// </para>
    /// </summary>
    public IReadOnlyList<JavaInstallation> GetJavaInstallationsSnapshot()
    {
        lock (_javaLock)
        {
            return [.. JavaInstallations];
        }
    }

    /// <summary>
    /// Mutates the Java list under the lock.
    /// The action must only touch the list it is given.
    /// </summary>
    public void MutateJavaInstallations(Action<ObservableCollection<JavaInstallation>> mutate)
    {
        lock (_javaLock)
        {
            mutate(JavaInstallations);
        }
    }

    /// <summary>
    /// Gets the default Java installation
    /// </summary>
    public JavaInstallation? GetDefaultJava()
    {
        var installations = GetJavaInstallationsSnapshot();

        if (string.IsNullOrEmpty(DefaultJavaId))
        {
            return installations.FirstOrDefault(j => j.IsDefault)
                ?? installations.FirstOrDefault();
        }

        return installations.FirstOrDefault(j => j.Id == DefaultJavaId);
    }

    /// <summary>
    /// Gets the path to the default Java executable
    /// </summary>
    public string GetDefaultJavaPath() => GetDefaultJava()?.Path ?? DefaultJavaPath;
}