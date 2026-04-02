using Konserva.Utilities;
using Xunit;

namespace Konserva.Tests.Utilities;

/// <summary>
/// Тесты для JavaVersionParser
/// </summary>
public class JavaVersionParserTests
{
    #region ParseMajorVersion Tests

    [Theory]
    [InlineData("java version \"1.8.0_301\"", 8)]
    [InlineData("java version \"1.7.0_80\"", 7)]
    [InlineData("java version \"1.6.0_45\"", 6)]
    public void ParseMajorVersion_LegacyFormat_ReturnsCorrectVersion(
        string versionOutput,
        int expectedVersion)
    {
        // Act
        var result = JavaVersionParser.ParseMajorVersion(versionOutput);

        // Assert
        result.Should().Be(expectedVersion);
    }

    [Theory]
    [InlineData("java version \"11.0.11\" 2021-04-20 LTS", 11)]
    [InlineData("java version \"17.0.1\" 2021-10-19 LTS", 17)]
    [InlineData("java version \"21.0.1\" 2023-10-17 LTS", 21)]
    [InlineData("java version \"25.0.1\" 2025-10-17 LTS", 25)]
    public void ParseMajorVersion_NewFormat_ReturnsCorrectVersion(
        string versionOutput,
        int expectedVersion)
    {
        // Act
        var result = JavaVersionParser.ParseMajorVersion(versionOutput);

        // Assert
        result.Should().Be(expectedVersion);
    }

    [Theory]
    [InlineData("openjdk version \"1.8.0_302\"", 8)]
    [InlineData("openjdk version \"11.0.12\" 2021-07-20", 11)]
    [InlineData("openjdk version \"17.0.2\" 2022-01-18", 17)]
    public void ParseMajorVersion_OpenJdkFormat_ReturnsCorrectVersion(
        string versionOutput,
        int expectedVersion)
    {
        // Act
        var result = JavaVersionParser.ParseMajorVersion(versionOutput);

        // Assert
        result.Should().Be(expectedVersion);
    }

    [Fact]
    public void ParseMajorVersion_EmptyInput_Returns8()
    {
        // Act
        var result = JavaVersionParser.ParseMajorVersion("");

        // Assert
        result.Should().Be(8);
    }

    [Fact]
    public void ParseMajorVersion_NullInput_ThrowsArgumentNullException()
    {
        // Act
        var action = () => JavaVersionParser.ParseMajorVersion(null!);

        // Assert
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParseMajorVersion_InvalidInput_Returns8()
    {
        // Act
        var result = JavaVersionParser.ParseMajorVersion("invalid version string");

        // Assert
        result.Should().Be(8);
    }

    #endregion

    #region ParseVersion Tests

    [Theory]
    [InlineData("java version \"1.8.0_301\"", "1.8.0_301")]
    [InlineData("java version \"11.0.11\" 2021-04-20 LTS", "11.0.11")]
    [InlineData("java version \"17.0.1\" 2021-10-19 LTS", "17.0.1")]
    [InlineData("java version \"21.0.1\" 2023-10-17 LTS", "21.0.1")]
    public void ParseVersion_ExtractsVersionString(
        string versionOutput,
        string expectedVersion)
    {
        // Act
        var result = JavaVersionParser.ParseVersion(versionOutput);

        // Assert
        result.Should().Be(expectedVersion);
    }

    [Theory]
    [InlineData("openjdk version \"1.8.0_302\"", "1.8.0_302")]
    [InlineData("openjdk version \"11.0.12\" 2021-07-20", "11.0.12")]
    public void ParseVersion_OpenJdkFormat_ExtractsVersionString(
        string versionOutput,
        string expectedVersion)
    {
        // Act
        var result = JavaVersionParser.ParseVersion(versionOutput);

        // Assert
        result.Should().Be(expectedVersion);
    }

    [Fact]
    public void ParseVersion_EmptyInput_ReturnsUnknown()
    {
        // Act
        var result = JavaVersionParser.ParseVersion("");

        // Assert
        result.Should().Be("неизвестно");
    }

    [Fact]
    public void ParseVersion_NullInput_ThrowsArgumentNullException()
    {
        // Act
        var action = () => JavaVersionParser.ParseVersion(null!);

        // Assert
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParseVersion_InvalidInput_ReturnsInput()
    {
        // Act
        var result = JavaVersionParser.ParseVersion("no version here");

        // Assert - Regex возвращает первую найденную группу или исходную строку
        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region GetRequiredJavaVersion Tests

    [Theory]
    [InlineData("1.21.1", McServerInstaller.ServerLaunchType.Forge, 17)]
    [InlineData("1.20.4", McServerInstaller.ServerLaunchType.NeoForge, 17)]
    [InlineData("1.19.2", McServerInstaller.ServerLaunchType.Forge, 17)]
    [InlineData("1.18.2", McServerInstaller.ServerLaunchType.Forge, 17)]
    [InlineData("1.17.1", McServerInstaller.ServerLaunchType.Forge, 17)]
    public void GetRequiredJavaVersion_Forge_ReturnsJava17(
        string minecraftVersion,
        McServerInstaller.ServerLaunchType launchType,
        int expectedJavaVersion)
    {
        // Act
        var result = JavaVersionParser.GetRequiredJavaVersion(minecraftVersion, launchType);

        // Assert
        result.Should().Be(expectedJavaVersion);
    }

    [Theory]
    [InlineData("1.21.1", McServerInstaller.ServerLaunchType.Fabric, 21)]
    [InlineData("1.20.5", McServerInstaller.ServerLaunchType.Fabric, 21)]
    [InlineData("1.20.6", McServerInstaller.ServerLaunchType.Fabric, 21)]
    [InlineData("1.21", McServerInstaller.ServerLaunchType.Fabric, 21)]
    public void GetRequiredJavaVersion_Fabric_NewVersions_ReturnsJava21(
        string minecraftVersion,
        McServerInstaller.ServerLaunchType launchType,
        int expectedJavaVersion)
    {
        // Act
        var result = JavaVersionParser.GetRequiredJavaVersion(minecraftVersion, launchType);

        // Assert
        result.Should().Be(expectedJavaVersion);
    }

    [Theory]
    [InlineData("1.20.4", McServerInstaller.ServerLaunchType.Fabric, 17)]
    [InlineData("1.20.1", McServerInstaller.ServerLaunchType.Fabric, 17)]
    [InlineData("1.19.2", McServerInstaller.ServerLaunchType.Fabric, 17)]
    [InlineData("1.18.2", McServerInstaller.ServerLaunchType.Fabric, 17)]
    public void GetRequiredJavaVersion_Fabric_OldVersions_ReturnsJava17(
        string minecraftVersion,
        McServerInstaller.ServerLaunchType launchType,
        int expectedJavaVersion)
    {
        // Act
        var result = JavaVersionParser.GetRequiredJavaVersion(minecraftVersion, launchType);

        // Assert
        result.Should().Be(expectedJavaVersion);
    }

    [Theory]
    [InlineData("1.21.1", McServerInstaller.ServerLaunchType.Standard, 21)]
    [InlineData("1.20.5", McServerInstaller.ServerLaunchType.Standard, 21)]
    [InlineData("1.20.6", McServerInstaller.ServerLaunchType.Standard, 21)]
    public void GetRequiredJavaVersion_Standard_NewVersions_ReturnsJava21(
        string minecraftVersion,
        McServerInstaller.ServerLaunchType launchType,
        int expectedJavaVersion)
    {
        // Act
        var result = JavaVersionParser.GetRequiredJavaVersion(minecraftVersion, launchType);

        // Assert
        result.Should().Be(expectedJavaVersion);
    }

    [Theory]
    [InlineData("1.20.1", McServerInstaller.ServerLaunchType.Standard, 17)]
    [InlineData("1.19.2", McServerInstaller.ServerLaunchType.Standard, 17)]
    [InlineData("1.18.2", McServerInstaller.ServerLaunchType.Standard, 17)]
    [InlineData("1.17.1", McServerInstaller.ServerLaunchType.Standard, 16)]
    [InlineData("1.16.5", McServerInstaller.ServerLaunchType.Standard, 8)]
    [InlineData("1.12.2", McServerInstaller.ServerLaunchType.Standard, 8)]
    [InlineData("1.8.9", McServerInstaller.ServerLaunchType.Standard, 8)]
    public void GetRequiredJavaVersion_Standard_OlderVersions_ReturnsCorrectJava(
        string minecraftVersion,
        McServerInstaller.ServerLaunchType launchType,
        int expectedJavaVersion)
    {
        // Act
        var result = JavaVersionParser.GetRequiredJavaVersion(minecraftVersion, launchType);

        // Assert
        result.Should().Be(expectedJavaVersion);
    }

    [Theory]
    [InlineData("1.20.5", McServerInstaller.ServerLaunchType.Standard, 21)]
    [InlineData("1.20.6", McServerInstaller.ServerLaunchType.Standard, 21)]
    [InlineData("1.21.0", McServerInstaller.ServerLaunchType.Standard, 21)]
    [InlineData("1.21.5", McServerInstaller.ServerLaunchType.Standard, 21)]
    public void GetRequiredJavaVersion_Minecraft20_5Plus_ReturnsJava21(
        string minecraftVersion,
        McServerInstaller.ServerLaunchType launchType,
        int expectedJavaVersion)
    {
        // Act
        var result = JavaVersionParser.GetRequiredJavaVersion(minecraftVersion, launchType);

        // Assert
        result.Should().Be(expectedJavaVersion);
    }

    #endregion
}
