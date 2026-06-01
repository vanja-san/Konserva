namespace Konserva.Models;

public class ApiEndpoints
{
    public string MojangManifest { get; set; } = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
    public string FabricMeta { get; set; } = "https://meta.fabricmc.net/v2";
    public string FabricInstaller { get; set; } = "https://meta.fabricmc.net/v2/versions/installer";
    public string ForgeMaven { get; set; } = "https://maven.minecraftforge.net";
    public string NeoForgeMaven { get; set; } = "https://maven.neoforged.net";
    public string NeoForgeApi { get; set; } = "https://maven.neoforged.net/api/v1/installer";
    public string QuiltMeta { get; set; } = "https://meta.quiltmc.org/v3";
    public string QuiltInstaller { get; set; } = "https://meta.quiltmc.org/v3/versions/installer";
    public string PaperApi { get; set; } = "https://api.papermc.io/v2";
    public string Adoptium { get; set; } = "https://adoptium.net";
}
