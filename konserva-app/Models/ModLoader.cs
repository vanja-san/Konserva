namespace Konserva.Models;

/// <summary>
/// Mod loader type
/// </summary>
public enum ModLoaderType
{
    Vanilla,
    Forge,
    NeoForge,
    Fabric,
    Quilt,
    Paper
}

/// <summary>
/// Mod loader configuration
/// </summary>
public class ModLoader
{
    public ModLoaderType Type { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? LoaderVersion { get; set; }

    /// <summary>
    /// Check: whether a custom mod loader is used (not Vanilla)
    /// </summary>
    public bool IsModded => Type != ModLoaderType.Vanilla;

    /// <summary>
    /// Full mod loader name
    /// </summary>
    public string FullName => IsModded ? $"{Type} {LoaderVersion ?? Version}" : "Vanilla";

    /// <summary>
    /// Clones the mod loader
    /// </summary>
    public ModLoader Clone() => new()
    {
        Type = Type,
        Version = Version,
        LoaderVersion = LoaderVersion
    };
}
