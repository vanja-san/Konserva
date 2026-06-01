namespace Konserva.Tests.Models;

/// <summary>
/// Тесты для ServerLaunchType — проверка enum значений.
/// </summary>
public class ServerLaunchTypeTests
{
    [Fact]
    public void Enum_HasExpectedValues()
    {
        ((int)ServerLaunchType.Standard).Should().Be(0);
        ((int)ServerLaunchType.Fabric).Should().Be(1);
        ((int)ServerLaunchType.Quilt).Should().Be(2);
        ((int)ServerLaunchType.Forge).Should().Be(3);
        ((int)ServerLaunchType.NeoForge).Should().Be(4);
    }

    [Fact]
    public void Enum_HasFiveMembers()
    {
        Enum.GetValues<ServerLaunchType>().Should().HaveCount(5);
    }

    [Fact]
    public void Standard_IsDefault()
    {
        default(ServerLaunchType).Should().Be(ServerLaunchType.Standard);
    }
}
