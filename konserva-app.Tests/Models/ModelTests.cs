using System.IO;

namespace Konserva.Tests.Models;

/// <summary>
/// Тесты для ServerSettings
/// </summary>
public class ServerSettingsTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var settings = new ServerSettings();

        settings.RamMin.Should().Be(1024);
        settings.RamMax.Should().Be(4096);
        settings.CpuCores.Should().BeGreaterThan(0);
        settings.JavaAutoSelect.Should().BeTrue();
        settings.JavaId.Should().BeNull();
        settings.JavaArgs.Should().BeEmpty();
        settings.AutoRestart.Should().BeFalse();
        settings.AutoRestartDelay.Should().Be(5);
    }

    [Fact]
    public void RamMin_ClampsToRamMax()
    {
        var settings = new ServerSettings { RamMax = 2048 };
        settings.RamMin = 4096;

        settings.RamMin.Should().Be(2048);
    }

    [Fact]
    public void RamMin_ClampsToMinimum()
    {
        var settings = new ServerSettings();
        settings.RamMin = 100;

        settings.RamMin.Should().Be(256); // Constants.MinRamMb
    }

    [Fact]
    public void RamMax_ClampsToRamMin()
    {
        var settings = new ServerSettings { RamMin = 2048 };
        settings.RamMax = 1024;

        settings.RamMax.Should().Be(2048);
    }

    [Fact]
    public void RamMax_ClampsToMaximum()
    {
        var settings = new ServerSettings();
        settings.RamMax = 100000;

        settings.RamMax.Should().Be(65536); // Constants.MaxRamMb
    }

    [Fact]
    public void CpuCores_ClampsToMinimum()
    {
        var settings = new ServerSettings();
        settings.CpuCores = 0;

        settings.CpuCores.Should().Be(1);
    }

    [Fact]
    public void CpuCores_ClampsToProcessorCount()
    {
        var settings = new ServerSettings();
        settings.CpuCores = 1000;

        settings.CpuCores.Should().BeInRange(1, Environment.ProcessorCount);
    }

    [Fact]
    public void AutoRestartDelay_HasRangeAttribute_ForValidation()
    {
        var settings = new ServerSettings();
        // Range attribute is used for UI validation, not auto-clamping
        settings.AutoRestartDelay = 99999;
        settings.AutoRestartDelay.Should().Be(99999); // No auto-clamp in setter
        settings.Validate().Should().BeFalse(); // But validation catches it
    }

    [Fact]
    public void AutoRestartDelay_ValidationRange()
    {
        var settings = new ServerSettings { AutoRestartDelay = 10 };
        settings.Validate().Should().BeTrue();

        settings.AutoRestartDelay = -1;
        settings.Validate().Should().BeFalse();
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var settings = new ServerSettings
        {
            RamMin = 2048,
            RamMax = 8192,
            JavaArgs = ["-Xlog:gc"]
        };

        var clone = settings.Clone();

        clone.Should().NotBeSameAs(settings);
        clone.RamMin.Should().Be(2048);
        clone.RamMax.Should().Be(8192);
        clone.JavaArgs.Should().NotBeSameAs(settings.JavaArgs);
        clone.JavaArgs.Should().Contain("-Xlog:gc");

        // Изменение клона не влияет на оригинал
        clone.JavaArgs.Add("-Xlog:gc:file=gc.log");
        settings.JavaArgs.Should().HaveCount(1);
    }

    [Fact]
    public void Validate_ReturnsTrue_ForValidSettings()
    {
        var settings = new ServerSettings
        {
            RamMin = 1024,
            RamMax = 4096,
            CpuCores = 4,
            AutoRestartDelay = 10
        };

        settings.Validate().Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsFalse_ForInvalidRamMin()
    {
        var settings = new ServerSettings();
        // Принудительно ставим невалидное значение через рефлексию
        var field = typeof(ServerSettings).GetField("_ramMin",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(settings, 0);

        settings.Validate().Should().BeFalse();
    }
}

/// <summary>
/// Тесты для ServerProperties
/// </summary>
public class ServerPropertiesTests : IDisposable
{
    private readonly string _testFilePath;

    public ServerPropertiesTests()
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_server_props_{Guid.NewGuid()}.properties");
    }

    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var props = new ServerProperties();

        props.ServerPort.Should().Be(25565);
        props.MaxPlayers.Should().Be(20);
        props.ViewDistance.Should().Be(10);
        props.Gamemode.Should().Be("survival");
        props.Hardcore.Should().BeFalse();
        props.Difficulty.Should().Be("easy");
        props.OnlineMode.Should().BeTrue();
        props.LevelName.Should().Be("world");
        props.Motd.Should().Be("A Minecraft Server");
        props.EnableRcon.Should().BeFalse();
        props.RconPort.Should().Be(25575);
        props.WhiteList.Should().BeFalse();
        props.Pvp.Should().BeTrue();
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileNotExists()
    {
        if (File.Exists(_testFilePath)) File.Delete(_testFilePath);

        var props = ServerProperties.Load(_testFilePath);

        props.ServerPort.Should().Be(25565);
        props.MaxPlayers.Should().Be(20);
    }

    [Fact]
    public void Load_ParsesPropertiesFile()
    {
        var content = @"#Minecraft server properties
server-port=25566
max-players=50
gamemode=creative
difficulty=hard
online-mode=false
motd=My Cool Server
white-list=true
";
        File.WriteAllText(_testFilePath, content);

        var props = ServerProperties.Load(_testFilePath);

        props.ServerPort.Should().Be(25566);
        props.MaxPlayers.Should().Be(50);
        props.Gamemode.Should().Be("creative");
        props.Difficulty.Should().Be("hard");
        props.OnlineMode.Should().BeFalse();
        props.Motd.Should().Be("My Cool Server");
        props.WhiteList.Should().BeTrue();
    }

    [Fact]
    public void Load_SkipsCommentsAndEmptyLines()
    {
        var content = @"# This is a comment
server-port=25566

# Another comment
max-players=30
";
        File.WriteAllText(_testFilePath, content);

        var props = ServerProperties.Load(_testFilePath);

        props.ServerPort.Should().Be(25566);
        props.MaxPlayers.Should().Be(30);
    }

    [Fact]
    public void Load_UsesDefaultForInvalidValues()
    {
        var content = @"server-port=not_a_number
max-players=abc
view-distance=invalid
";
        File.WriteAllText(_testFilePath, content);

        var props = ServerProperties.Load(_testFilePath);

        props.ServerPort.Should().Be(25565); // default
        props.MaxPlayers.Should().Be(20);    // default
        props.ViewDistance.Should().Be(10);  // default
    }

    [Fact]
    public void Load_ParsesBooleanValues_CaseInsensitive()
    {
        var content = @"online-mode=TRUE
white-list=False
pvp=TrUe
";
        File.WriteAllText(_testFilePath, content);

        var props = ServerProperties.Load(_testFilePath);

        props.OnlineMode.Should().BeTrue();
        props.WhiteList.Should().BeFalse();
        props.Pvp.Should().BeTrue();
    }

    [Fact]
    public void Save_WritesPropertiesFile()
    {
        var props = new ServerProperties
        {
            ServerPort = 25567,
            MaxPlayers = 100,
            Gamemode = "survival",
            OnlineMode = false,
            Motd = "Test Server"
        };

        props.Save(_testFilePath);

        File.Exists(_testFilePath).Should().BeTrue();

        var content = File.ReadAllText(_testFilePath);
        content.Should().Contain("server-port=25567");
        content.Should().Contain("max-players=100");
        content.Should().Contain("gamemode=survival");
        content.Should().Contain("online-mode=false");
        content.Should().Contain("motd=Test Server");
    }

    [Fact]
    public void Save_IncludeHeaderComment()
    {
        var props = new ServerProperties();
        props.Save(_testFilePath);

        var content = File.ReadAllText(_testFilePath);
        content.Should().StartWith("#Minecraft server properties");
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var original = new ServerProperties
        {
            ServerPort = 25568,
            MaxPlayers = 64,
            Gamemode = "creative",
            Difficulty = "hard",
            OnlineMode = false,
            Motd = "Round Trip Test",
            WhiteList = true,
            EnableRcon = true,
            RconPassword = "secret"
        };

        original.Save(_testFilePath);
        var loaded = ServerProperties.Load(_testFilePath);

        loaded.ServerPort.Should().Be(25568);
        loaded.MaxPlayers.Should().Be(64);
        loaded.Gamemode.Should().Be("creative");
        loaded.Difficulty.Should().Be("hard");
        loaded.OnlineMode.Should().BeFalse();
        loaded.Motd.Should().Be("Round Trip Test");
        loaded.WhiteList.Should().BeTrue();
        loaded.EnableRcon.Should().BeTrue();
        loaded.RconPassword.Should().Be("secret");
    }

    [Fact]
    public void GamemodeDisplayName_ReturnsRussianTranslation()
    {
        var props = new ServerProperties();

        props.Gamemode = "survival";
        props.GamemodeDisplayName.Should().Be("Выживание");

        props.Gamemode = "creative";
        props.GamemodeDisplayName.Should().Be("Творчество");

        props.Gamemode = "adventure";
        props.GamemodeDisplayName.Should().Be("Приключение");

        props.Gamemode = "spectator";
        props.GamemodeDisplayName.Should().Be("Наблюдатель");

        props.Gamemode = "unknown";
        props.GamemodeDisplayName.Should().Be("unknown");
    }

    [Fact]
    public void DifficultyDisplayName_ReturnsRussianTranslation()
    {
        var props = new ServerProperties();

        props.Difficulty = "peaceful";
        props.DifficultyDisplayName.Should().Be("Мирный");

        props.Difficulty = "easy";
        props.DifficultyDisplayName.Should().Be("Легкий");

        props.Difficulty = "normal";
        props.DifficultyDisplayName.Should().Be("Нормальный");

        props.Difficulty = "hard";
        props.DifficultyDisplayName.Should().Be("Сложный");
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
            File.Delete(_testFilePath);
    }
}

/// <summary>
/// Тесты для ModLoader
/// </summary>
public class ModLoaderTests
{
    [Fact]
    public void Constructor_DefaultValues()
    {
        var loader = new ModLoader();

        loader.Type.Should().Be(ModLoaderType.Vanilla);
        loader.Version.Should().Be("");
        loader.LoaderVersion.Should().BeNull(); // defaults to null, not ""
    }

    [Fact]
    public void IsModded_ReturnsTrue_ForNonVanillaTypes()
    {
        var types = new[]
        {
            ModLoaderType.Forge, ModLoaderType.NeoForge, ModLoaderType.Fabric,
            ModLoaderType.Quilt, ModLoaderType.Paper, ModLoaderType.Purpur
        };

        foreach (var type in types)
        {
            var loader = new ModLoader { Type = type };
            loader.IsModded.Should().BeTrue($"Type={type}");
        }
    }

    [Fact]
    public void IsModded_ReturnsFalse_ForVanilla()
    {
        var loader = new ModLoader { Type = ModLoaderType.Vanilla };
        loader.IsModded.Should().BeFalse();
    }

    [Fact]
    public void FullName_ReturnsVanilla_ForVanilla()
    {
        var loader = new ModLoader { Type = ModLoaderType.Vanilla };
        loader.FullName.Should().Be("Vanilla");
    }

    [Fact]
    public void FullName_ReturnsTypeAndLoaderVersion_WhenLoaderVersionSet()
    {
        var loader = new ModLoader { Type = ModLoaderType.Fabric, LoaderVersion = "0.16.0" };
        loader.FullName.Should().Be("Fabric 0.16.0");
    }

    [Fact]
    public void FullName_ReturnsTypeAndVersion_WhenLoaderVersionNotSet()
    {
        var loader = new ModLoader { Type = ModLoaderType.Forge, Version = "1.21.1" };
        loader.FullName.Should().Be("Forge 1.21.1");
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var loader = new ModLoader
        {
            Type = ModLoaderType.Fabric,
            Version = "1.21.1",
            LoaderVersion = "0.16.0"
        };

        var clone = loader.Clone();

        clone.Should().NotBeSameAs(loader);
        clone.Type.Should().Be(ModLoaderType.Fabric);
        clone.Version.Should().Be("1.21.1");
        clone.LoaderVersion.Should().Be("0.16.0");
    }

    [Fact]
    public void ModLoaderType_AllValues_Exist()
    {
        Enum.GetValues<ModLoaderType>().Should().HaveCount(7);
        Enum.GetNames<ModLoaderType>().Should().Contain("Vanilla");
        Enum.GetNames<ModLoaderType>().Should().Contain("Forge");
        Enum.GetNames<ModLoaderType>().Should().Contain("Fabric");
    }
}

/// <summary>
/// Тесты для ModItem
/// </summary>
public class ModItemTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var mod = new ModItem();

        mod.Name.Should().Be("");
        mod.Version.Should().Be("");
        mod.FileName.Should().Be("");
        mod.FilePath.Should().Be("");
        mod.FileSize.Should().Be(0);
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var mod = new ModItem
        {
            Name = "Fabric API",
            Version = "0.97.0",
            FileName = "fabric-api-0.97.0.jar",
            FilePath = "/mods/fabric-api.jar",
            FileSize = 5_000_000
        };

        mod.Name.Should().Be("Fabric API");
        mod.Version.Should().Be("0.97.0");
        mod.FileName.Should().Be("fabric-api-0.97.0.jar");
        mod.FilePath.Should().Be("/mods/fabric-api.jar");
        mod.FileSize.Should().Be(5_000_000);
    }
}

/// <summary>
/// Тесты для PluginItem
/// </summary>
public class PluginItemTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        var plugin = new PluginItem();

        plugin.Name.Should().Be("");
        plugin.Version.Should().Be("");
        plugin.FileName.Should().Be("");
        plugin.FilePath.Should().Be("");
        plugin.FileSize.Should().Be(0);
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var plugin = new PluginItem
        {
            Name = "EssentialsX",
            Version = "2.20.1",
            FileName = "EssentialsX-2.20.1.jar",
            FilePath = "/plugins/EssentialsX.jar",
            FileSize = 3_000_000
        };

        plugin.Name.Should().Be("EssentialsX");
        plugin.Version.Should().Be("2.20.1");
        plugin.FileName.Should().Be("EssentialsX-2.20.1.jar");
        plugin.FilePath.Should().Be("/plugins/EssentialsX.jar");
        plugin.FileSize.Should().Be(3_000_000);
    }
}

/// <summary>
/// Тесты для ApiEndpoints
/// </summary>
public class ApiEndpointsTests
{
    [Fact]
    public void Constructor_DefaultUrls_AreSet()
    {
        var endpoints = new ApiEndpoints();

        endpoints.MojangManifest.Should().NotBeNullOrEmpty();
        endpoints.FabricMeta.Should().NotBeNullOrEmpty();
        endpoints.FabricInstaller.Should().NotBeNullOrEmpty();
        endpoints.ForgeMaven.Should().NotBeNullOrEmpty();
        endpoints.NeoForgeMaven.Should().NotBeNullOrEmpty();
        endpoints.NeoForgeApi.Should().NotBeNullOrEmpty();
        endpoints.QuiltMeta.Should().NotBeNullOrEmpty();
        endpoints.QuiltInstaller.Should().NotBeNullOrEmpty();
        endpoints.PaperApi.Should().NotBeNullOrEmpty();
        endpoints.PurpurApi.Should().NotBeNullOrEmpty();
        endpoints.Adoptium.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AllUrls_StartWithHttps()
    {
        var endpoints = new ApiEndpoints();

        var urls = new[]
        {
            endpoints.MojangManifest, endpoints.FabricMeta, endpoints.FabricInstaller,
            endpoints.ForgeMaven, endpoints.NeoForgeMaven, endpoints.NeoForgeApi,
            endpoints.QuiltMeta, endpoints.QuiltInstaller, endpoints.PaperApi,
            endpoints.PurpurApi, endpoints.Adoptium
        };

        foreach (var url in urls)
        {
            url.Should().StartWith("https://");
        }
    }

    [Fact]
    public void CanModifyUrls()
    {
        var endpoints = new ApiEndpoints
        {
            MojangManifest = "https://custom.url/manifest.json"
        };

        endpoints.MojangManifest.Should().Be("https://custom.url/manifest.json");
    }
}
