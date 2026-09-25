using Konserva.Utilities;
using Xunit;

namespace Konserva.Tests;

[Trait("Category", "Unit")]
public class ModIncompatibilityParserTests
{
    // Реальный вывод Fabric Loader при несовместимых модах
    private const string FabricIncompatibleModsError = """
        [01:50:56] [main/ERROR]: Incompatible mods found!
        net.fabricmc.loader.impl.FormattedException: Some of your mods are incompatible with the game or each other!
        A potential solution has been determined, this may resolve your problem:
         - Install fabric-api, version 0.160.0 or later.
         - Install yet_another_config_lib_v3, version 3.9.4+26.2 or later.
         - Install forgeconfigapiport, any version.
         - Install puzzleslib, version 26.2.1 or later.
         - Replace 'Minecraft' (minecraft) 26.3 with any version between 26.2 (inclusive) and 26.3- (exclusive).
        More details:
         - Mod 'Architectury' (architectury) 21.1.9 requires version 0.160.0 or later of fabric-api, which is missing!
         - Mod 'Bridging Mod' (bridgingmod) 2.7.0+26.2 requires any version of fabric-api, which is missing!
         - Mod 'Bridging Mod' (bridgingmod) 2.7.0+26.2 requires version 3.9.4+26.2 or later of yet_another_config_lib_v3, which is missing!
         - Mod 'Easy Anvils' (easyanvils) 26.2.3 requires any 26.2.x version of 'Minecraft' (minecraft), but only the wrong version is present: 26.3!
         - Mod 'Easy Anvils' (easyanvils) 26.2.3 requires version 0.156.0 or later of fabric-api, which is missing!
         - Mod 'Easy Anvils' (easyanvils) 26.2.3 requires any version of forgeconfigapiport, which is missing!
         - Mod 'Easy Anvils' (easyanvils) 26.2.3 requires version 26.2.1 or later of puzzleslib, which is missing!
        	at net.fabricmc.loader.impl.FormattedException.ofLocalized(FormattedException.java:51)
        	at net.fabricmc.loader.impl.FabricLoaderImpl.load(FabricLoaderImpl.java:202)
        """;

    [Fact]
    public void TryParse_DetectsFabricBlock()
    {
        var info = ModIncompatibilityParser.TryParse(FabricIncompatibleModsError);

        Assert.NotNull(info);
        Assert.Contains("Incompatible mods found!", info!.Header);
    }

    [Fact]
    public void TryParse_ParsesAllSolutionBullets()
    {
        var info = ModIncompatibilityParser.TryParse(FabricIncompatibleModsError)!;

        Assert.Equal(5, info.Solutions.Count);
        Assert.Empty(info.RawSolutions);

        var fabricApi = info.Solutions[0];
        Assert.Equal(SuggestionKind.Install, fabricApi.Kind);
        Assert.Equal("fabric-api", fabricApi.Name);
        Assert.Equal(RequirementKind.AtLeast, fabricApi.Requirement!.Kind);
        Assert.Equal("0.160.0", fabricApi.Requirement.Version);

        var anyVersion = info.Solutions[2];
        Assert.Equal(RequirementKind.Any, anyVersion.Requirement!.Kind);

        var replaceMc = info.Solutions[4];
        Assert.Equal(SuggestionKind.Replace, replaceMc.Kind);
        Assert.Equal("Minecraft", replaceMc.Name);
        Assert.Equal("minecraft", replaceMc.Id);
        Assert.Equal("26.3", replaceMc.OldVersion);
    }

    [Fact]
    public void TryParse_ParsesAllDetailBullets()
    {
        var info = ModIncompatibilityParser.TryParse(FabricIncompatibleModsError)!;

        Assert.Equal(7, info.Issues.Count);
        Assert.Empty(info.RawDetails);

        var architectury = info.Issues[0];
        Assert.Equal(IssueKind.MissingDependency, architectury.Kind);
        Assert.Equal("Architectury", architectury.ModName);
        Assert.Equal("architectury", architectury.ModId);
        Assert.Equal("21.1.9", architectury.ModVersion);
        Assert.Equal("fabric-api", architectury.DependencyName);
        Assert.Equal(RequirementKind.AtLeast, architectury.Requirement!.Kind);
        Assert.Equal("0.160.0", architectury.Requirement.Version);

        var easyAnvilsMc = info.Issues[3];
        Assert.Equal(IssueKind.WrongVersion, easyAnvilsMc.Kind);
        Assert.Equal("Minecraft", easyAnvilsMc.DependencyName);
        Assert.Equal("minecraft", easyAnvilsMc.DependencyId);
        Assert.Equal(RequirementKind.AnyMajor, easyAnvilsMc.Requirement!.Kind);
        Assert.Equal("26.2", easyAnvilsMc.Requirement.Version);
        Assert.Equal("26.3", easyAnvilsMc.PresentVersion);

        // Стек-трейс не попадает ни в решения, ни в подробности
        Assert.DoesNotContain(info.Issues, i => i.Raw.StartsWith("at "));
        Assert.DoesNotContain(info.Solutions, s => s.Raw.StartsWith("at "));
    }

    [Fact]
    public void TryParse_HandlesConflictsAndDepends()
    {
        const string text = """
            Incompatible mods found!
            More details:
             - Mod 'Some Mod' (somemod) 1.0.0 conflicts with othermod!
             - Mod 'Other Mod' (othermod) 1.0.0 depends on somemod, which is missing!
            """;
        var info = ModIncompatibilityParser.TryParse(text)!;

        Assert.Equal(2, info.Issues.Count);
        Assert.Equal(IssueKind.ConflictsWith, info.Issues[0].Kind);
        Assert.Equal("othermod", info.Issues[0].DependencyId);
        Assert.Equal(IssueKind.DependsOn, info.Issues[1].Kind);
        Assert.Equal("somemod", info.Issues[1].DependencyName);
    }

    [Fact]
    public void TryParse_ReturnsNull_ForNonModError()
    {
        const string javaError = "Error: A JNI error has occurred, please check your installation and try again";
        Assert.Null(ModIncompatibilityParser.TryParse(javaError));
        Assert.Null(ModIncompatibilityParser.TryParse(null));
        Assert.Null(ModIncompatibilityParser.TryParse(""));
    }

    [Fact]
    public void TryParse_WorksWithJustTheHeaderLine()
    {
        var info = ModIncompatibilityParser.TryParse("[main/ERROR]: Incompatible mods found!");

        Assert.NotNull(info);
        Assert.Empty(info!.Solutions);
        Assert.Empty(info.Issues);
    }

    [Fact]
    public void ParseVersionRequirement_HandlesReplaceRange()
    {
        var req = ModIncompatibilityParser.ParseVersionRequirement(
            "any version between 26.2 (inclusive) and 26.3- (exclusive)");

        Assert.Equal(RequirementKind.Range, req.Kind);
        Assert.Equal("26.2", req.Version);
        Assert.Equal("26.3-", req.VersionTo);
    }

    [Fact]
    public void IsModIncompatibility_ReturnsTrue_ForKnownPatterns()
    {
        Assert.True(ModIncompatibilityParser.IsModIncompatibility("[main/ERROR]: Incompatible mods found!"));
        Assert.True(ModIncompatibilityParser.IsModIncompatibility("Some of your mods are incompatible with the game or each other!"));
        Assert.False(ModIncompatibilityParser.IsModIncompatibility("Mod resolution failed"));
        Assert.False(ModIncompatibilityParser.IsModIncompatibility(null));
    }
}