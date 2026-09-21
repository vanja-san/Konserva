using Konserva.Models;

namespace Konserva.Utilities;

/// <summary>
/// Маппинг лоадеров Konserva на имена лоадеров Modrinth.
/// </summary>
public static class ModrinthLoaderMap
{
    /// <summary>Лоадеры, под которые подходят моды из папки mods/.</summary>
    public static IReadOnlyList<string> ModLoaders(ModLoaderType loader) => loader switch
    {
        ModLoaderType.Fabric => ["fabric"],
        // Quilt умеет грузить и fabric-моды
        ModLoaderType.Quilt => ["quilt", "fabric"],
        ModLoaderType.Forge => ["forge"],
        ModLoaderType.NeoForge => ["neoforge"],
        _ => []
    };

    /// <summary>Лоадеры, под которые подходят плагины из папки plugins/.</summary>
    public static IReadOnlyList<string> PluginLoaders(ModLoaderType loader) => loader switch
    {
        ModLoaderType.Paper => ["paper", "bukkit", "spigot"],
        _ => []
    };

    /// <summary>true, если для лоадера вообще есть смысл проверять моды.</summary>
    public static bool SupportsModUpdates(ModLoaderType loader) => ModLoaders(loader).Count > 0;
}
