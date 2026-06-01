using Konserva.Models;
using Xunit;

namespace Konserva.Tests.Models;

/// <summary>
/// Тесты для ApiUrls — проверка, что константы заданы корректно.
/// </summary>
public class ApiUrlsTests
{
    [Fact]
    public void GitHubApi_IsCorrect()
    {
        Assert.Equal("https://api.github.com/repos/vanja-san/Konserva", ApiUrls.GitHubApi);
    }

    [Fact]
    public void GitHubReleasesLatest_BuildsFromGitHubApi()
    {
        Assert.Equal(ApiUrls.GitHubApi + "/releases/latest", ApiUrls.GitHubReleasesLatest);
    }

    [Fact]
    public void MojangManifest_IsCorrect()
    {
        Assert.Equal("https://launchermeta.mojang.com/mc/game/version_manifest.json", ApiUrls.MojangManifest);
    }

    [Fact]
    public void FabricUrls_BuildCorrectly()
    {
        Assert.Equal("https://meta.fabricmc.net/v2", ApiUrls.FabricMeta);
        Assert.Equal(ApiUrls.FabricMeta + "/versions/loader", ApiUrls.FabricVersionsLoader);
        Assert.Equal(ApiUrls.FabricMeta + "/versions/installer", ApiUrls.FabricInstaller);
    }

    [Fact]
    public void ForgeUrls_AreCorrect()
    {
        Assert.Equal("https://maven.minecraftforge.net", ApiUrls.ForgeMaven);
        Assert.Equal("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ApiUrls.ForgePromotions);
        Assert.Equal("https://files.minecraftforge.net/net/minecraftforge/forge/index", ApiUrls.ForgeIndex);
        Assert.Equal("https://files.minecraftforge.net/maven/net/minecraftforge/forge", ApiUrls.ForgeMavenAlt);
    }

    [Fact]
    public void NeoForgeUrls_BuildCorrectly()
    {
        Assert.Equal("https://maven.neoforged.net", ApiUrls.NeoForgeMaven);
        Assert.Equal(ApiUrls.NeoForgeMaven + "/releases/net/neoforged/forge", ApiUrls.NeoForgeReleases);
        Assert.Equal(ApiUrls.NeoForgeMaven + "/api/v1/installer", ApiUrls.NeoForgeApi);
        Assert.Equal(ApiUrls.NeoForgeMaven + "/releases/net/neoforged/forge/promotions_slim.json", ApiUrls.NeoForgePromotions);
        Assert.Equal(ApiUrls.NeoForgeMaven + "/releases/net/neoforged/neoforge/maven-metadata.xml", ApiUrls.NeoForgeMetadata);
    }

    [Fact]
    public void QuiltUrls_BuildCorrectly()
    {
        Assert.Equal("https://meta.quiltmc.org/v3", ApiUrls.QuiltMeta);
        Assert.Equal(ApiUrls.QuiltMeta + "/versions/loader", ApiUrls.QuiltVersionsLoader);
        Assert.Equal(ApiUrls.QuiltMeta + "/versions/installer", ApiUrls.QuiltInstaller);
    }

    [Fact]
    public void PaperUrls_AreCorrect()
    {
        Assert.Equal("https://fill.papermc.io/v3", ApiUrls.PaperApi);
        Assert.Equal(ApiUrls.PaperApi + "/projects/paper/versions", ApiUrls.PaperVersions);
    }

    [Fact]
    public void AdoptiumUrl_IsCorrect()
    {
        Assert.Equal("https://adoptium.net", ApiUrls.Adoptium);
    }

    [Fact]
    public void MinecraftEulaUrl_IsCorrect()
    {
        Assert.Equal("https://aka.ms/MinecraftEULA", ApiUrls.MinecraftEula);
    }
}
