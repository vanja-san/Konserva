using Konserva.Models;
using Konserva.Services;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Xunit;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для McServerInstaller
/// </summary>
public class McServerInstallerTests : IDisposable
{
    private readonly string _testDir;
    private readonly McServerInstaller _installer;

    public McServerInstallerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"konserva_installer_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _installer = new McServerInstaller(new HttpClient());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData("1.21.1", true)]
    [InlineData("1.20.4", true)]
    [InlineData("1.19.2", true)]
    [InlineData("26.1", true)]
    [InlineData("1.21.10", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    public void TryParseMcVersion_ValidVersions_ReturnsTrue(string version, bool expectedValid)
    {
        // Act
        var result = _installer.TryParseMcVersion(version, out var major, out var minor);

        // Assert
        result.Should().Be(expectedValid);

        if (expectedValid)
        {
            major.Should().BeGreaterThan(0);
        }
    }

    [Theory]
    [InlineData("1.21.1", 1, 21)]
    [InlineData("1.20.4", 1, 20)]
    [InlineData("1.19.2", 1, 19)]
    [InlineData("26.1.0", 26, 1)]
    [InlineData("1.21.10", 1, 21)]
    public void TryParseMcVersion_ExtractsMajorAndMinor(string version, int expectedMajor, int expectedMinor)
    {
        // Act
        var result = _installer.TryParseMcVersion(version, out var major, out var minor);

        // Assert
        result.Should().BeTrue();
        major.Should().Be(expectedMajor);
        minor.Should().Be(expectedMinor);
    }

    #region FindServerJar Tests

    [Fact]
    public void FindServerJar_ReturnsServerJar_WhenExists()
    {
        File.WriteAllText(Path.Combine(_testDir, "server.jar"), "fake");
        var result = _installer.FindServerJar(_testDir);
        result.Should().EndWith("server.jar");
    }

    [Fact]
    public void FindServerJar_ReturnsFabricJar_WhenExists()
    {
        File.WriteAllText(Path.Combine(_testDir, "fabric-server-launch.jar"), "fake");
        var result = _installer.FindServerJar(_testDir);
        result.Should().EndWith("fabric-server-launch.jar");
    }

    [Fact]
    public void FindServerJar_ReturnsForgeJar_WhenExists()
    {
        CreateFakeJar(Path.Combine(_testDir, "forge-1.21.1-52.0.1.jar"));
        var result = _installer.FindServerJar(_testDir);
        result.Should().Contain("forge-");
    }

    [Fact]
    public void FindServerJar_ReturnsShimJar_WhenExists()
    {
        CreateFakeJar(Path.Combine(_testDir, "minecraft-shim.jar"));
        var result = _installer.FindServerJar(_testDir);
        result.Should().EndWith("-shim.jar");
    }

    [Fact]
    public void FindServerJar_ReturnsEmpty_ForNeoforgeJar()
    {
        // NeoForge не использует -jar, запускается через @args файлы
        CreateFakeJar(Path.Combine(_testDir, "neoforge-1.21.1.jar"));
        var result = _installer.FindServerJar(_testDir);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindServerJar_ReturnsEmpty_WhenNoPriorityJarExists()
    {
        // Without server.jar, fabric, quilt, paper or forge-*.jar with Main-Class — empty
        CreateFakeJar(Path.Combine(_testDir, "custom-server.jar"));
        var result = _installer.FindServerJar(_testDir);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindServerJar_ReturnsEmpty_WhenNoJarExists()
    {
        var result = _installer.FindServerJar(_testDir);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindServerJar_PriorityOrder_ServerJarFirst()
    {
        // Создаём несколько jar файлов — server.jar должен иметь приоритет
        File.WriteAllText(Path.Combine(_testDir, "fabric-server-launch.jar"), "fake");
        File.WriteAllText(Path.Combine(_testDir, "server.jar"), "fake");

        var result = _installer.FindServerJar(_testDir);
        result.Should().EndWith("server.jar");
    }

    #endregion

    #region GetServerLaunchType Tests

    [Fact]
    public void GetServerLaunchType_ReturnsFabric_WhenFabricJarExists()
    {
        File.WriteAllText(Path.Combine(_testDir, "fabric-server-launch.jar"), "fake");
        var result = _installer.GetServerLaunchType(_testDir);
        result.Should().Be(ServerLaunchType.Fabric);
    }

    [Fact]
    public void GetServerLaunchType_ReturnsQuilt_WhenQuiltJarExists()
    {
        File.WriteAllText(Path.Combine(_testDir, "quilt-server-0.25.0.jar"), "fake");
        var result = _installer.GetServerLaunchType(_testDir);
        result.Should().Be(ServerLaunchType.Quilt);
    }

    [Fact]
    public void GetServerLaunchType_ReturnsForge_WhenForgeJarExists()
    {
        File.WriteAllText(Path.Combine(_testDir, "forge-1.21.1.jar"), "fake");
        var result = _installer.GetServerLaunchType(_testDir);
        result.Should().Be(ServerLaunchType.Forge);
    }

    [Fact]
    public void GetServerLaunchType_ReturnsForge_WhenShimJarExists()
    {
        File.WriteAllText(Path.Combine(_testDir, "minecraft-shim.jar"), "fake");
        var result = _installer.GetServerLaunchType(_testDir);
        result.Should().Be(ServerLaunchType.Forge);
    }

    [Fact]
    public void GetServerLaunchType_ReturnsNeoForge_WhenNeoforgeJarExists()
    {
        File.WriteAllText(Path.Combine(_testDir, "neoforge-1.21.1.jar"), "fake");
        var result = _installer.GetServerLaunchType(_testDir);
        result.Should().Be(ServerLaunchType.NeoForge);
    }

    [Fact]
    public void GetServerLaunchType_ReturnsStandard_WhenOnlyServerJarExists()
    {
        File.WriteAllText(Path.Combine(_testDir, "server.jar"), "fake");
        var result = _installer.GetServerLaunchType(_testDir);
        result.Should().Be(ServerLaunchType.Standard);
    }

    [Fact]
    public void GetServerLaunchType_ReturnsStandard_WhenEmptyDirectory()
    {
        var result = _installer.GetServerLaunchType(_testDir);
        result.Should().Be(ServerLaunchType.Standard);
    }

    #endregion

    #region BuildLaunchArgs Tests

    [Fact]
    public void BuildLaunchArgs_IncludesRamSettings()
    {
        var settings = new ServerSettings { RamMin = 2048, RamMax = 8192 };
        var result = _installer.BuildLaunchArgs("/path/server.jar", settings);

        result.Should().Contain("-Xms2048M");
        result.Should().Contain("-Xmx8192M");
    }

    [Fact]
    public void BuildLaunchArgs_IncludesG1gcFlags()
    {
        var settings = new ServerSettings { RamMin = 1024, RamMax = 4096 };
        var result = _installer.BuildLaunchArgs("/path/server.jar", settings);

        result.Should().Contain("-XX:+UseG1GC");
        result.Should().Contain("-XX:+ParallelRefProcEnabled");
        result.Should().Contain("-XX:+DisableExplicitGC");
    }

    [Fact]
    public void BuildLaunchArgs_IncludesJarAndNogui()
    {
        var settings = new ServerSettings { RamMin = 1024, RamMax = 4096 };
        var result = _installer.BuildLaunchArgs("/path/server.jar", settings);

        result.Should().Contain("-jar \"server.jar\" nogui");
    }

    [Fact]
    public void BuildLaunchArgs_IncludesCustomJavaArgs()
    {
        var settings = new ServerSettings
        {
            RamMin = 1024,
            RamMax = 4096,
            JavaArgs = ["-Dfml.ignoreInvalidMinecraftCertificates=true", "-XX:+UseZGC"]
        };
        var result = _installer.BuildLaunchArgs("/path/server.jar", settings);

        result.Should().Contain("-Dfml.ignoreInvalidMinecraftCertificates=true");
        result.Should().Contain("-XX:+UseZGC");
    }

    [Fact]
    public void BuildLaunchArgs_SkipsEmptyJavaArgs()
    {
        var settings = new ServerSettings
        {
            RamMin = 1024,
            RamMax = 4096,
            JavaArgs = ["", "   ", "-Xlog:gc"]
        };
        var result = _installer.BuildLaunchArgs("/path/server.jar", settings);

        result.Should().NotContain("  ");
        result.Should().Contain("-Xlog:gc");
    }

    [Fact]
    public void BuildLaunchArgs_SkipsParallelRefProcEnabled_ForJava26()
    {
        var settings = new ServerSettings { RamMin = 1024, RamMax = 4096 };
        var result = _installer.BuildLaunchArgs("/path/server.jar", settings, ServerLaunchType.Standard, javaMajorVersion: 26);

        result.Should().NotContain("ParallelRefProcEnabled");
        // Другие GC-флаги не должны пострадать
        result.Should().Contain("-XX:+UseG1GC");
        result.Should().Contain("-XX:+DisableExplicitGC");
    }

    [Fact]
    public void BuildLaunchArgs_IncludesParallelRefProcEnabled_ForJava25()
    {
        var settings = new ServerSettings { RamMin = 1024, RamMax = 4096 };
        var result = _installer.BuildLaunchArgs("/path/server.jar", settings, ServerLaunchType.Standard, javaMajorVersion: 25);

        result.Should().Contain("-XX:+ParallelRefProcEnabled");
    }

    [Fact]
    public void BuildLaunchArgs_DifferentLaunchTypes_ProducesSameArgs()
    {
        var settings = new ServerSettings { RamMin = 2048, RamMax = 4096 };
        var standard = _installer.BuildLaunchArgs("/path/server.jar", settings, ServerLaunchType.Standard);
        var fabric = _installer.BuildLaunchArgs("/path/server.jar", settings, ServerLaunchType.Fabric);
        var forge = _installer.BuildLaunchArgs("/path/server.jar", settings, ServerLaunchType.Forge);

        // В текущей реализации все типы запуска дают одинаковые аргументы
        standard.Should().Be(fabric);
        fabric.Should().Be(forge);
    }

    #endregion

    /// <summary>
    /// Создать fake jar файл с MANIFEST.MF содержащим Main-Class
    /// </summary>
    private static void CreateFakeJar(string path)
    {
        using var stream = new FileStream(path, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("META-INF/MANIFEST.MF");
        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine("Manifest-Version: 1.0");
        writer.WriteLine("Main-Class: com.example.Main");
    }
}
