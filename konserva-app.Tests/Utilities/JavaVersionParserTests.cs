using Konserva.Utilities;
using Xunit;

namespace Konserva.Tests.Utilities;

/// <summary>
/// Тесты для JavaVersionParser
/// </summary>
public class JavaVersionParserTests
{
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
    [InlineData("java version \"1.8.0_301\"", "1.8.0_301")]
    [InlineData("java version \"11.0.11\" 2021-04-20 LTS", "11.0.11")]
    [InlineData("java version \"17.0.1\" 2021-10-19 LTS", "17.0.1")]
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
    [InlineData("1.21.1", McServerInstaller.ServerLaunchType.Forge, 17)]   // Forge 1.17+ требует Java 17
    [InlineData("1.20.4", McServerInstaller.ServerLaunchType.NeoForge, 17)]
    [InlineData("1.21.1", McServerInstaller.ServerLaunchType.Fabric, 21)]  // Fabric 1.20.5+ требует Java 21
    [InlineData("1.19.2", McServerInstaller.ServerLaunchType.Standard, 17)]
    public void GetRequiredJavaVersion_ReturnsCorrectVersion(
        string minecraftVersion,
        McServerInstaller.ServerLaunchType launchType,
        int expectedJavaVersion)
    {
        // Act
        var result = JavaVersionParser.GetRequiredJavaVersion(minecraftVersion, launchType);
        
        // Assert
        result.Should().Be(expectedJavaVersion);
    }
}
