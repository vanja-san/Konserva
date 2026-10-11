using Konserva.Utilities;
using System.ComponentModel.DataAnnotations;
using ObservableObject = CommunityToolkit.Mvvm.ComponentModel.ObservableObject;

namespace Konserva.Models;

/// <summary>
/// Server settings
/// </summary>
public class ServerSettings : ObservableObject
{
    private int _ramMin = Constants.DefaultRamMinMb;
    private int _ramMax = Constants.DefaultRamMaxMb;
    private int _cpuCores = Environment.ProcessorCount;

    /// <summary>
    /// Minimum RAM (MB). Minimum: 256, Maximum: RamMax
    /// </summary>
    [Range(Constants.MinRamMb, int.MaxValue)]
    public int RamMin
    {
        get => _ramMin;
        set
        {
            var clamped = Math.Clamp(value, Constants.MinRamMb, RamMax);
            if (SetProperty(ref _ramMin, clamped))
                OnPropertyChanged(nameof(RamMax));
        }
    }

    /// <summary>
    /// Maximum RAM (MB). Minimum: RamMin, Maximum: 65536
    /// </summary>
    [Range(1, Constants.MaxRamMb)]
    public int RamMax
    {
        get => _ramMax;
        set
        {
            var clamped = Math.Clamp(value, RamMin, Constants.MaxRamMb);
            if (SetProperty(ref _ramMax, clamped))
                OnPropertyChanged(nameof(RamMin));
        }
    }

    /// <summary>
    /// Number of CPU cores. Minimum: 1, Maximum: number of available cores
    /// </summary>
    [Range(1, 128)]
    public int CpuCores
    {
        get => _cpuCores;
        set
        {
            var clamped = Math.Clamp(value, 1, Environment.ProcessorCount);
            SetProperty(ref _cpuCores, clamped);
        }
    }

    /// <summary>
    /// ID of the selected Java version (if null, the default Java is used)
    /// </summary>
    public string? JavaId { get; set; }

    /// <summary>
    /// Automatic Java version selection based on the Minecraft version
    /// </summary>
    public bool JavaAutoSelect { get; set; } = true;

    /// <summary>
    /// Custom Java launch arguments (safe for the user to change/remove)
    /// </summary>
    public List<string> JavaArgs { get; set; } = DefaultJvmArgs;

    /// <summary>
    /// Default JVM optimization arguments (safe for the user to remove).
    /// Critical arguments (encoding, terminal, -jar) stay in the code.
    /// </summary>
    public static List<string> DefaultJvmArgs { get; } =
    [
        "-XX:+UseG1GC",
        "-XX:MaxGCPauseMillis=200",
        "-XX:+ParallelRefProcEnabled",
        "-XX:+UnlockExperimentalVMOptions",
        "-XX:+DisableExplicitGC",
        "-XX:+AlwaysPreTouch",
        "-XX:G1NewSizePercent=30",
        "-XX:G1MaxNewSizePercent=40",
        "-XX:G1HeapRegionSize=8M",
        "-XX:G1ReservePercent=20",
        "-XX:G1HeapWastePercent=5",
        "-XX:G1MixedGCCountTarget=4",
        "-XX:InitiatingHeapOccupancyPercent=15"
    ];

    /// <summary>
    /// Text representation of the JVM arguments (one argument per line) for the UI.
    /// </summary>
    public string JvmArgsText
    {
        get => string.Join(Environment.NewLine, JavaArgs);
        set => JavaArgs = [.. (value ?? "")
            .Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
            .Select(a => a.Trim())
            .Where(a => !string.IsNullOrWhiteSpace(a))];
    }

    /// <summary>
    /// Automatic restart on stop
    /// </summary>
    public bool AutoRestart { get; set; }

    /// <summary>
    /// Delay before the automatic restart (sec). Minimum: 0, Maximum: 3600
    /// </summary>
    [Range(0, Constants.MaxAutoRestartDelaySec)]
    public int AutoRestartDelay { get; set; } = 5;

    /// <summary>
    /// Automatically forward the port via UPnP when the server starts.
    /// </summary>
    public bool EnableUpnp { get; set; }

    /// <summary>
    /// Show a notification when a loader update is available.
    /// </summary>
    public bool EnableUpdateNotification { get; set; } = true;

    /// <summary>
    /// Create a backup of the mods before updating.
    /// </summary>
    public bool ModBackupEnabled { get; set; } = true;

    /// <summary>
    /// Mod update channel: which versions (stable/beta/alpha) to offer.
    /// </summary>
    public ModUpdateChannel UpdateChannel { get; set; } = ModUpdateChannel.Release;

    /// <summary>
    /// Clones the settings
    /// </summary>
    public ServerSettings Clone() => new()
    {
        RamMin = RamMin,
        RamMax = RamMax,
        CpuCores = CpuCores,
        JavaId = JavaId,
        JavaAutoSelect = JavaAutoSelect,
        JavaArgs = [.. JavaArgs],
AutoRestart = AutoRestart,
        AutoRestartDelay = AutoRestartDelay,
        EnableUpnp = EnableUpnp,
        EnableUpdateNotification = EnableUpdateNotification,
        ModBackupEnabled = ModBackupEnabled,
        UpdateChannel = UpdateChannel
    };

    /// <summary>
    /// Settings validation
    /// </summary>
    public bool Validate() => RamMin >= Constants.MinRamMb && RamMax >= RamMin && RamMax <= Constants.MaxRamMb &&
                               CpuCores >= 1 && CpuCores <= 128 &&
                               AutoRestartDelay >= 0 && AutoRestartDelay <= Constants.MaxAutoRestartDelaySec;
}
