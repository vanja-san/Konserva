using Konserva.Models;
using Konserva.Services;
using Xunit;

namespace Konserva.Tests;

/// <summary>
/// Covers <see cref="ServerStartErrorAnalyzer"/> — the classification of a failed
/// server start. The analyzer is pure, so every branch is reachable without a window.
/// </summary>
[Trait("Category", "Unit")]
public class ServerStartErrorAnalyzerTests
{
    private const string FabricIncompatibleModsError = """
        [01:50:56] [main/ERROR]: Incompatible mods found!
        net.fabricmc.loader.impl.FormattedException: Some of your mods are incompatible with the game or each other!
        More details:
         - Mod 'Easy Anvils' (easyanvils) 26.2.3 requires version 0.156.0 or later of fabric-api, which is missing!
        """;

    private const string UnsupportedClassFileError = """
        Error: A JNI error has occurred, please check your installation and try again
        Exception in thread "main" java.lang.UnsupportedClassVersionError: net/minecraft/server/Main has been compiled by a more recent version of the Java Runtime (class file version 65.0), this version of the Java Runtime only recognizes class file versions up to 61.0
        """;

    private const string CustomJavaErrorMessage = """
        Требуется Java 21+ для Minecraft 1.21.1, но найдена Java 17 (17.0.8)
        Путь: C:\Program Files\Java\jdk-17\bin\java.exe
        """;

    private const string BrokenJava8Error = """
        Требуется Java 8 (8u302b08) для Minecraft 1.16.5, но найдена Java 8u402
        Обнаружена нерабочая сборка Java 8 >= 8u400
        """;

    private const string ForgeManifestEntryVerifierError = """
        java.lang.NoSuchMethodError: 'void sun.security.util.SignatureUtil.<init>(java.security.Signature)'
        Exception in thread "main" java.lang.NoSuchMethodError: sun.security.util.ManifestEntryVerifier.<init>(sun.security.x509.X500Name,java.security.Signature)
        """;

    private static Server CreateServer(string mcVersion = "1.21.1", ModLoaderType loader = ModLoaderType.Vanilla)
        => new() { Name = "Test", McVersion = mcVersion, ModLoader = new ModLoader { Type = loader } };

    [Fact]
    public void Analyze_ModIncompatibility_ReturnsModKindWithParsedInfo()
    {
        var result = ServerStartErrorAnalyzer.Analyze(CreateServer(), FabricIncompatibleModsError);

        Assert.Equal(ServerStartErrorKind.ModIncompatibility, result.Kind);
        Assert.NotNull(result.ModInfo);
    }

    [Fact]
    public void Analyze_UnsupportedClassFileVersion_ReturnsJavaKind()
    {
        var result = ServerStartErrorAnalyzer.Analyze(CreateServer(), UnsupportedClassFileError);

        Assert.Equal(ServerStartErrorKind.JavaIncompatibility, result.Kind);
        Assert.Equal(21, result.RequiredJavaVersion);
    }

    [Fact]
    public void Analyze_CustomJavaMessage_ReturnsJavaKindWithVersions()
    {
        var result = ServerStartErrorAnalyzer.Analyze(CreateServer(), CustomJavaErrorMessage);

        Assert.Equal(ServerStartErrorKind.JavaIncompatibility, result.Kind);
        Assert.Equal(21, result.RequiredJavaVersion);
        Assert.Equal(17, result.FoundJavaVersion);
    }

    /// <summary>
    /// A broken Java 8 build (>= 8u400) was already reported by GetJavaPathForServer,
    /// so the analyzer must not ask the UI to show a second, generic error.
    /// </summary>
    [Fact]
    public void Analyze_BrokenJava8_ReturnsNoneToAvoidDuplicateSnackbar()
    {
        var result = ServerStartErrorAnalyzer.Analyze(
            CreateServer("1.16.5", ModLoaderType.Forge), BrokenJava8Error);

        Assert.Equal(ServerStartErrorKind.None, result.Kind);
    }

    [Fact]
    public void Analyze_NonJavaFailure_ReturnsGenericWithOriginalMessage()
    {
        const string message = "Unable to access jarfile server.jar";

        var result = ServerStartErrorAnalyzer.Analyze(CreateServer(), message);

        Assert.Equal(ServerStartErrorKind.Generic, result.Kind);
        Assert.Equal(message, result.ErrorMessage);
    }

    /// <summary>
    /// A JVM crash with no parseable version is still a Java problem; the required
    /// version must be derived from the Minecraft version instead of being dropped.
    /// </summary>
    [Fact]
    public void Analyze_JavaCrashWithoutVersion_DerivesRequiredVersionFromMcVersion()
    {
        const string crash = "Exception in thread \"main\" java.lang.reflect.InvocationTargetException";

        var result = ServerStartErrorAnalyzer.Analyze(CreateServer("1.20.1"), crash);

        Assert.Equal(ServerStartErrorKind.JavaIncompatibility, result.Kind);
        Assert.Equal(17, result.RequiredJavaVersion);
    }

    [Fact]
    public void Analyze_Mc1_21_DerivesRequiredVersion21()
    {
        const string crash = "Exception in thread \"main\" java.lang.reflect.InvocationTargetException";

        var result = ServerStartErrorAnalyzer.Analyze(CreateServer("1.21.1"), crash);

        Assert.Equal(21, result.RequiredJavaVersion);
    }

    /// <summary>
    /// The Forge ManifestEntryVerifier crash is a Java problem worth a snackbar,
    /// but it carries no "8u400" marker, so it must not be swallowed.
    /// </summary>
    [Fact]
    public void Analyze_ForgeManifestEntryVerifier_ReturnsJavaKind()
    {
        var result = ServerStartErrorAnalyzer.Analyze(
            CreateServer("1.16.5", ModLoaderType.Forge), ForgeManifestEntryVerifierError);

        Assert.Equal(ServerStartErrorKind.JavaIncompatibility, result.Kind);
    }

    [Fact]
    public void Analyze_WithoutConfigService_ReturnsEmptyInstalledJavaList()
    {
        var result = ServerStartErrorAnalyzer.Analyze(CreateServer(), CustomJavaErrorMessage);

        Assert.Empty(result.InstalledJava);
    }

    [Fact]
    public void Analyze_ModIncompatibility_DoesNotTouchJavaVersions()
    {
        var result = ServerStartErrorAnalyzer.Analyze(CreateServer(), FabricIncompatibleModsError);

        Assert.Equal(0, result.RequiredJavaVersion);
        Assert.Empty(result.InstalledJava);
    }
}