namespace Konserva.Models;

/// <summary>
/// Централизованное хранение всех URL внешних API и ресурсов.
/// </summary>
public static class ApiUrls
{
    // --- GitHub ---
    public const string GitHubApi = "https://api.github.com/repos/vanja-san/Konserva";
    public const string GitHubReleasesLatest = GitHubApi + "/releases/latest";
    public const string MinecraftEula = "https://aka.ms/MinecraftEULA";

    // --- Mojang ---
    public const string MojangManifest = "https://launchermeta.mojang.com/mc/game/version_manifest.json";

    // --- Fabric ---
    public const string FabricMeta = "https://meta.fabricmc.net/v2";
    public const string FabricVersionsLoader = FabricMeta + "/versions/loader";
    public const string FabricInstaller = FabricMeta + "/versions/installer";

    // --- Forge ---
    public const string ForgeMaven = "https://maven.minecraftforge.net";
    public const string ForgePromotions = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    public const string ForgeIndex = "https://files.minecraftforge.net/net/minecraftforge/forge/index";
    public const string ForgeMavenAlt = "https://files.minecraftforge.net/maven/net/minecraftforge/forge";

    // --- NeoForge ---
    // BMCLAPI — JSON API для списка версий по MC версии (альтернатива Maven XML)
    public const string NeoForgeBmclapiList = "https://bmclapi2.bangbang93.com/neoforge/list";
    // Maven (оставлен как fallback через maven-metadata.xml)
    public const string NeoForgeMaven = "https://maven.neoforged.net";
    public const string NeoForgeReleases = NeoForgeMaven + "/releases/net/neoforged/forge";
    public const string NeoForgeApi = NeoForgeMaven + "/api/v1/installer";
    public const string NeoForgePromotions = NeoForgeMaven + "/releases/net/neoforged/forge/promotions_slim.json";
    public const string NeoForgeMetadata = NeoForgeMaven + "/releases/net/neoforged/neoforge/maven-metadata.xml";

    // --- Quilt ---
    public const string QuiltMeta = "https://meta.quiltmc.org/v3";
    public const string QuiltVersionsLoader = QuiltMeta + "/versions/loader";
    public const string QuiltInstaller = QuiltMeta + "/versions/installer";

    // --- Paper (Downloads Service v3 на Cloudflare) ---
    public const string PaperApi = "https://fill.papermc.io/v3";
    public const string PaperVersions = PaperApi + "/projects/paper/versions";

    // --- Adoptium (Java) ---
    public const string Adoptium = "https://adoptium.net";
}
