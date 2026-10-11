using Konserva.Models;

namespace Konserva.Utilities;

/// <summary>
/// Mapping of Konserva loaders to Modrinth loader names.
/// </summary>
public static class ModrinthLoaderMap
{
    /// <summary>Loaders for which mods from the mods/ folder are suitable.</summary>
    public static IReadOnlyList<string> ModLoaders(ModLoaderType loader) => loader switch
    {
        ModLoaderType.Fabric => ["fabric"],
        // Quilt can also load fabric mods
        ModLoaderType.Quilt => ["quilt", "fabric"],
        ModLoaderType.Forge => ["forge"],
        ModLoaderType.NeoForge => ["neoforge"],
        _ => []
    };

    /// <summary>Loaders for which plugins from the plugins/ folder are suitable.</summary>
    public static IReadOnlyList<string> PluginLoaders(ModLoaderType loader) => loader switch
    {
        ModLoaderType.Paper => ["paper", "bukkit", "spigot"],
        _ => []
    };

    /// <summary>true, if the mod loader supports mods.</summary>
    public static bool SupportsMods(ModLoaderType loader) => ModLoaders(loader).Count > 0;

    /// <summary>true, if the mod loader supports plugins (Bukkit API).</summary>
    public static bool SupportsPlugins(ModLoaderType loader) => PluginLoaders(loader).Count > 0;

    /// <summary>true, if checking for mod updates makes sense at all for the loader.</summary>
    public static bool SupportsModUpdates(ModLoaderType loader) => SupportsMods(loader);
}
