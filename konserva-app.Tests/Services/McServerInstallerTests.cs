using Konserva.Services;
using Xunit;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для McServerInstaller
/// </summary>
public class McServerInstallerTests
{
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
        var result = McServerInstaller.TryParseMcVersion(version, out var major, out var minor);
        
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
        var result = McServerInstaller.TryParseMcVersion(version, out var major, out var minor);
        
        // Assert
        result.Should().BeTrue();
        major.Should().Be(expectedMajor);
        minor.Should().Be(expectedMinor);
    }
}
