using Konserva.Utilities;

namespace Konserva.Models;

/// <summary>
/// Information about an installed Java
/// </summary>
public class JavaInstallation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int MajorVersion { get; set; }
    public bool IsDefault { get; set; }
    public DateTime? AddedDate { get; set; } = SystemTime.Now;

    /// <summary>
    /// Display name
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(Name)
        ? $"Java {Version}"
        : $"{Name} {Version}";

    /// <summary>
    /// Check: whether the Java file exists
    /// </summary>
    public bool Exists => System.IO.File.Exists(Path) || System.IO.File.Exists(Path + ".exe");

    /// <summary>
    /// Creates a copy
    /// </summary>
    public JavaInstallation Clone() => new()
    {
        Name = Name,
        Path = Path,
        Version = Version,
        MajorVersion = MajorVersion,
        IsDefault = IsDefault,
        AddedDate = AddedDate
    };
}