using FluentAssertions;
using Konserva.Models;
using Konserva.Utilities;
using Xunit;

namespace Konserva.Tests.Utilities;

/// <summary>
/// Тесты маппинга канала обновлений модов в version_type Modrinth.
/// </summary>
[Trait("Category", "Unit")]
public class ModUpdateChannelsTests
{
    [Fact]
    public void ToVersionTypes_Release_ReturnsStableOnly()
    {
        // Act & Assert
        ModUpdateChannels.ToVersionTypes(ModUpdateChannel.Release)
            .Should().Equal("release");
    }

    [Fact]
    public void ToVersionTypes_BetaOnly_ReturnsBetaOnly()
    {
        // Act & Assert
        ModUpdateChannels.ToVersionTypes(ModUpdateChannel.BetaOnly)
            .Should().Equal("beta");
    }

    [Fact]
    public void ToVersionTypes_AlphaOnly_ReturnsAlphaOnly()
    {
        // Act & Assert
        ModUpdateChannels.ToVersionTypes(ModUpdateChannel.AlphaOnly)
            .Should().Equal("alpha");
    }

    [Fact]
    public void ToVersionTypes_All_ReturnsAllModrinthTypes()
    {
        // Act & Assert
        ModUpdateChannels.ToVersionTypes(ModUpdateChannel.All)
            .Should().Equal("release", "beta", "alpha");
    }
}