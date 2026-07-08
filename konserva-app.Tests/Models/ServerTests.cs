using Konserva.Models;
using Konserva.Utilities;
using Xunit;

namespace Konserva.Tests.Models;

/// <summary>
/// Тесты для модели Server
/// </summary>
public class ServerTests
{
    [Fact]
    public void Constructor_CreatesServerWithDefaultValues()
    {
        // Act
        var server = new Server
        {
            Name = "TestServer",
            McVersion = "1.21.1",
            Port = 25565,
            ModLoader = new ModLoader { Type = ModLoaderType.Vanilla }
        };

        // Assert
        server.Name.Should().Be("TestServer");
        server.McVersion.Should().Be("1.21.1");
        server.Port.Should().Be(25565);
        server.Status.Should().Be(ServerStatus.Stopped);
        server.ModLoader.Type.Should().Be(ModLoaderType.Vanilla);
    }

    [Fact]
    public void Name_TrimsWhitespace()
    {
        // Arrange
        var server = new Server();

        // Act
        server.Name = "  Test Server  ";

        // Assert
        server.Name.Should().Be("Test Server");
    }

    [Fact]
    public void Name_TruncatesToMaxLength()
    {
        // Arrange
        var server = new Server();
        var longName = new string('A', 150);

        // Act
        server.Name = longName;

        // Assert
        server.Name.Length.Should().Be(Constants.MaxServerNameLength);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-100, 1)]
    [InlineData(65536, 65535)]
    [InlineData(100000, 65535)]
    public void Port_ClampsToValidRange(int input, int expected)
    {
        // Arrange
        var server = new Server();

        // Act
        server.Port = input;

        // Assert
        server.Port.Should().Be(expected);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        // Arrange
        var original = new Server
        {
            Name = "Original",
            McVersion = "1.21.1",
            Port = 25565
        };

        // Act
        var clone = original.Clone();
        clone.Name = "Clone";

        // Assert
        original.Name.Should().Be("Original");
        clone.Name.Should().Be("Clone");
    }

    [Fact]
    public void Validate_ReturnsTrueForValidServer()
    {
        // Arrange
        var server = new Server
        {
            Name = "ValidServer",
            Port = 25565
        };

        // Act
        var result = server.Validate();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_ReturnsFalseForInvalidName()
    {
        // Arrange
        var server = new Server
        {
            Name = "",
            Port = 25565
        };

        // Act
        var result = server.Validate();

        // Assert
        result.Should().BeFalse();
    }
}
