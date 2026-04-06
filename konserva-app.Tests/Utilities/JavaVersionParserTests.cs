using Konserva.Utilities;
using Xunit;

namespace Konserva.Tests;

public class JavaVersionParserTests
{
    // Реальный stderr Forge когда Java слишком старая (class file version)
    private const string ForgeUnsupportedClassError = """
        Error: A JNI error has occurred, please check your installation and try again
        Exception in thread "main" java.lang.UnsupportedClassVersionError: net/minecraft/server/Main has been compiled by a more recent version of the Java Runtime (class file version 65.0), this version of the Java Runtime only recognizes class file versions up to 61.0
        	at java.lang.ClassLoader.defineClass1(Native Method)
        	at java.lang.ClassLoader.defineClass(ClassLoader.java:1017)
        """;

    // Fabric error — class file version появляется глубже в stack trace
    private const string FabricUnsupportedClassError = """
        Error: A JNI error has occurred, please check your installation and try again
        Exception in thread "main" java.lang.UnsupportedClassVersionError: net/fabricmc/loader/impl/launch/knot/KnotServer has been compiled by a more recent version of the Java Runtime (class file version 65.0), this version of the Java Runtime only recognizes class file versions up to 61.0
        	at java.lang.ClassLoader.defineClass1(Native Method)
        """;

    // Наше кастомное сообщение об ошибке (из LogJavaVersionError)
    private const string CustomJavaErrorMessage = """
        Требуется Java 21+ для Minecraft 1.21.1, но найдена Java 17 (17.0.8)
        Путь: C:\Program Files\Java\jdk-17\bin\java.exe
        """;

    // NeoForge error
    private const string NeoForgeUnsupportedClassError = """
        Error: A JNI error has occurred, please check your installation and try again
        Exception in thread "main" java.lang.UnsupportedClassVersionError: net/neoforged/neoforge/server/Main has been compiled by a more recent version of the Java Runtime (class file version 65.0), this version of the Java Runtime only recognizes class file versions up to 61.0
        """;

    // Forge bootstrap error — реальный лог из приложения
    private const string ForgeBootstrapError = """
        Exception in thread "main" java.lang.IllegalStateException: Current Java is 21 but we require at least 25
        at net.minecraftforge.bootstrap.shim.Main.main(Main.java:32)
        """;

    // Forge bootstrap error с Java 17
    private const string ForgeBootstrapError17 = """
        Exception in thread "main" java.lang.IllegalStateException: Current Java is 17 but we require at least 21
        at net.minecraftforge.bootstrap.shim.Main.main(Main.java:32)
        """;

    // ========== ParseRequiredJavaVersion ==========

    [Fact]
    public void ParseRequiredJavaVersion_FromClassFileVersion_ReturnsCorrectVersion()
    {
        // Forge: class file 65 = Java 21
        var result = JavaVersionParser.ParseRequiredJavaVersion(ForgeUnsupportedClassError);
        Assert.Equal(21, result);
    }

    [Fact]
    public void ParseRequiredJavaVersion_FromFabricError_ReturnsCorrectVersion()
    {
        var result = JavaVersionParser.ParseRequiredJavaVersion(FabricUnsupportedClassError);
        Assert.Equal(21, result);
    }

    [Fact]
    public void ParseRequiredJavaVersion_FromNeoForgeError_ReturnsCorrectVersion()
    {
        var result = JavaVersionParser.ParseRequiredJavaVersion(NeoForgeUnsupportedClassError);
        Assert.Equal(21, result);
    }

    [Fact]
    public void ParseRequiredJavaVersion_FromCustomMessage_ReturnsCorrectVersion()
    {
        var result = JavaVersionParser.ParseRequiredJavaVersion(CustomJavaErrorMessage);
        Assert.Equal(21, result);
    }

    [Fact]
    public void ParseRequiredJavaVersion_FromForgeBootstrap_ReturnsRequiredVersion()
    {
        // Реальная ошибка Forge: "Current Java is 21 but we require at least 25"
        var result = JavaVersionParser.ParseRequiredJavaVersion(ForgeBootstrapError);
        Assert.Equal(25, result);
    }

    [Fact]
    public void ParseRequiredJavaVersion_FromForgeBootstrap17_ReturnsRequiredVersion()
    {
        var result = JavaVersionParser.ParseRequiredJavaVersion(ForgeBootstrapError17);
        Assert.Equal(21, result);
    }

    // ========== ParseFoundJavaVersion ==========

    [Fact]
    public void ParseFoundJavaVersion_FromForgeError_ReturnsCorrectVersion()
    {
        // "up to 61.0" = Java 17
        var result = JavaVersionParser.ParseFoundJavaVersion(ForgeUnsupportedClassError);
        Assert.Equal(17, result);
    }

    [Fact]
    public void ParseFoundJavaVersion_FromFabricError_ReturnsCorrectVersion()
    {
        var result = JavaVersionParser.ParseFoundJavaVersion(FabricUnsupportedClassError);
        Assert.Equal(17, result);
    }

    [Fact]
    public void ParseFoundJavaVersion_FromCustomMessage_ReturnsCorrectVersion()
    {
        // Наше кастомное сообщение: "найдена Java 17"
        var result = JavaVersionParser.ParseFoundJavaVersion(CustomJavaErrorMessage);
        Assert.Equal(17, result);
    }

    [Fact]
    public void ParseFoundJavaVersion_FromCustomMessage21_ReturnsCorrectVersion()
    {
        // Реальное сообщение из лога: "Требуется Java 25+ для Minecraft 26.1.1, но найдена Java 21 (21.0.10)"
        var msg = "Требуется Java 25+ для Minecraft 26.1.1, но найдена Java 21 (21.0.10)";
        var result = JavaVersionParser.ParseFoundJavaVersion(msg);
        Assert.Equal(21, result);
    }

    [Fact]
    public void ParseFoundJavaVersion_FromForgeBootstrap_ReturnsFoundVersion()
    {
        // "Current Java is 21 but we require at least 25"
        var result = JavaVersionParser.ParseFoundJavaVersion(ForgeBootstrapError);
        Assert.Equal(21, result);
    }

    [Fact]
    public void ParseFoundJavaVersion_FromForgeBootstrap17_ReturnsFoundVersion()
    {
        var result = JavaVersionParser.ParseFoundJavaVersion(ForgeBootstrapError17);
        Assert.Equal(17, result);
    }

    // ========== ClassFileVersionToJavaVersion ==========

    [Fact]
    public void ClassFileVersionToJavaVersion_AllKnownVersions()
    {
        Assert.Equal(8, JavaVersionParser.ClassFileVersionToJavaVersion(52));
        Assert.Equal(11, JavaVersionParser.ClassFileVersionToJavaVersion(55));
        Assert.Equal(17, JavaVersionParser.ClassFileVersionToJavaVersion(61));
        Assert.Equal(21, JavaVersionParser.ClassFileVersionToJavaVersion(65));
        Assert.Equal(25, JavaVersionParser.ClassFileVersionToJavaVersion(69));
        Assert.Equal(0, JavaVersionParser.ClassFileVersionToJavaVersion(40));
    }

    // ========== GetRequiredJavaVersion ==========

    [Theory]
    // Forge — старые MC версии (1.x)
    [InlineData("1.20.4", McServerInstaller.ServerLaunchType.Forge, 17)]       // Forge MC 1.20.4 требует Java 17
    [InlineData("1.20.5", McServerInstaller.ServerLaunchType.Forge, 21)]       // Forge MC 1.20.5 требует Java 21
    [InlineData("1.21.1", McServerInstaller.ServerLaunchType.Forge, 21)]       // Forge MC 1.21+ требует Java 21
    // NeoForge — старые MC версии (1.x)
    [InlineData("1.20.4", McServerInstaller.ServerLaunchType.NeoForge, 17)]    // NeoForge MC 1.20.4 требует Java 17
    [InlineData("1.21.1", McServerInstaller.ServerLaunchType.NeoForge, 21)]    // NeoForge MC 1.21+ требует Java 21
    // Fabric — следует требованиям MC версии
    [InlineData("1.21.1", McServerInstaller.ServerLaunchType.Fabric, 21)]      // Fabric MC 1.21+ требует Java 21
    [InlineData("1.20.4", McServerInstaller.ServerLaunchType.Fabric, 17)]      // Fabric MC 1.20.4 требует Java 17
    // Standard (Vanilla, Paper, Purpur)
    [InlineData("1.20.6", McServerInstaller.ServerLaunchType.Standard, 21)]    // Vanilla 1.20.5+ требует Java 21
    [InlineData("1.18.2", McServerInstaller.ServerLaunchType.Standard, 17)]    // Vanilla 1.18+ требует Java 17
    [InlineData("1.16.5", McServerInstaller.ServerLaunchType.Standard, 8)]     // Vanilla 1.16 требует Java 8
    [InlineData("1.17.0", McServerInstaller.ServerLaunchType.Standard, 16)]    // Vanilla 1.17 требует Java 16
    // MC 26.x (новый формат без префикса 1.)
    [InlineData("26.1.0", McServerInstaller.ServerLaunchType.Forge, 25)]       // MC 26.1+ требует Java 25
    [InlineData("26.1.1", McServerInstaller.ServerLaunchType.Forge, 25)]       // MC 26.1.1 требует Java 25
    [InlineData("26.2.0", McServerInstaller.ServerLaunchType.Forge, 25)]       // MC 26.2+ требует Java 25
    [InlineData("27.0.0", McServerInstaller.ServerLaunchType.Forge, 25)]       // MC 27+ требует Java 25
    [InlineData("26.1.1", McServerInstaller.ServerLaunchType.Standard, 25)]    // MC 26.1.1 Vanilla требует Java 25
    // Старые версии
    [InlineData("1.12.2", McServerInstaller.ServerLaunchType.Forge, 17)]       // Forge старая версия требует Java 17
    [InlineData("1.12.2", McServerInstaller.ServerLaunchType.Standard, 8)]     // Vanilla 1.12 требует Java 8
    public void GetRequiredJavaVersion_AllCombinations_ReturnsCorrectVersion(string mcVersion, McServerInstaller.ServerLaunchType launchType, int expected)
    {
        var result = JavaVersionParser.GetRequiredJavaVersion(mcVersion, launchType);
        Assert.Equal(expected, result);
    }
}
