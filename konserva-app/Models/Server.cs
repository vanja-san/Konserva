using Konserva.Utilities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ObservableObject = CommunityToolkit.Mvvm.ComponentModel.ObservableObject;

namespace Konserva.Models;

/// <summary>
/// Server status
/// </summary>
public enum ServerStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error
}

/// <summary>
/// Minecraft server model
/// </summary>
public class Server : ObservableObject
{
    private static int _idCounter;

    /// <summary>
    /// Unique server identifier
    /// </summary>
    public string Id { get; init; } = GenerateShortId();

    /// <summary>
    /// Server name (1-{MaxServerNameLength} characters)
    /// </summary>
    [Required]
    [StringLength(Constants.MaxServerNameLength, MinimumLength = 1)]
    public string Name
    {
        get => field ?? string.Empty;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (SetProperty(ref field, trimmed.Length > Constants.MaxServerNameLength
                ? trimmed[..Constants.MaxServerNameLength]
                : trimmed))
            {
                OnPropertyChanged(nameof(Description));
            }
        }
    }

    /// <summary>
    /// Path to the server folder
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Minecraft version
    /// </summary>
    public string McVersion { get; set; } = string.Empty;

    /// <summary>
    /// Mod loader (Vanilla, Forge, NeoForge, Fabric, Quilt)
    /// </summary>
    public ModLoader ModLoader { get; set; } = new();

    /// <summary>
    /// Server settings
    /// </summary>
    public ServerSettings Settings { get; set; } = new();

    /// <summary>
    /// Last played time
    /// </summary>
    public DateTime? LastPlayed { get; set; }

    /// <summary>
    /// Server creation date
    /// </summary>
    public DateTime CreatedAt { get; init; } = SystemTime.Now;

    /// <summary>
    /// Server port (1-65535)
    /// </summary>
    [Range(1, 65535)]
    public int Port
    {
        get => field;
        set => field = Math.Clamp(value, 1, 65535);
    } = Constants.DefaultServerPort;

    /// <summary>
    /// Auto-start the server
    /// </summary>
    public bool AutoStart { get; set; }

    /// <summary>
    /// Server build number (for Paper)
    /// </summary>
    public int? ServerBuild { get; set; }

    // Transient data (not persisted, JsonIgnore)
    [JsonIgnore]
    public ServerStatus Status
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    [JsonIgnore]
    public string LastErrorMessage
    {
        get => field ?? string.Empty;
        set => SetProperty(ref field, value ?? string.Empty);
    }

    private static string GenerateShortId() =>
        $"{SystemTime.Now:yyyyMMdd}-{Interlocked.Increment(ref _idCounter):X4}";

    /// <summary>
    /// Initializes the ID counter when loading existing servers
    /// </summary>
    /// <param name="existingServers">List of loaded servers</param>
    public static void InitializeIdCounter(IEnumerable<Server> existingServers)
    {
        // Find the maximum numeric ID among the existing servers
        var maxId = existingServers
            .Select(s => s.Id)
            .Where(id => id != null)
            .Select(id =>
            {
                // ID format: yyyyMMdd-XXXX (where XXXX is a hexadecimal number)
                var parts = id.Split('-');
                if (parts.Length == 2 && int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var numericId))
                {
                    return numericId;
                }
                return 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        // Set the counter to the maximum value so that new IDs stay unique
        Interlocked.Exchange(ref _idCounter, maxId);
    }

    /// <summary>
    /// Check: whether the server is running
    /// </summary>
    public bool IsRunning => Status is ServerStatus.Running or ServerStatus.Starting;

    /// <summary>
    /// Short server description
    /// </summary>
    public string Description => $"{McVersion} • {ModLoader.Type}{(ServerBuild.HasValue ? $" (build {ServerBuild})" : "")}";

    /// <summary>
    /// Flag: whether the launch error dialog has been shown
    /// </summary>
    public bool ErrorDialogShown { get; set; }

    /// <summary>
    /// Resets the error flag
    /// </summary>
    public void ResetErrorDialog() => ErrorDialogShown = false;

    /// <summary>
    /// Clones the server
    /// </summary>
    public Server Clone() => new()
    {
        Name = Name,
        Path = Path,
        McVersion = McVersion,
        ModLoader = ModLoader.Clone(),  // Create a copy of ModLoader
        Settings = Settings.Clone(),
        LastPlayed = LastPlayed,
        Port = Port,
        AutoStart = AutoStart,
        ServerBuild = ServerBuild
    };

    /// <summary>
    /// Server validation
    /// </summary>
    public bool Validate() => !string.IsNullOrWhiteSpace(Name) &&
                               Port >= 1 && Port <= 65535 &&
                               Settings.Validate();
}
