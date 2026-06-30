using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для UpdateService.
/// </summary>
public class UpdateServiceTests : IDisposable
{
  private readonly Mock<IConfigService> _configMock;
  private readonly AppConfig _config;
  private readonly UpdateService _service;

  public UpdateServiceTests()
  {
    _config = new AppConfig
    {
      CheckUpdates = false,
      UpdateCheckIntervalHours = 24,
      LastUpdateCheck = null
    };

    _configMock = new Mock<IConfigService>();
    _configMock.Setup(c => c.GetConfig()).Returns(_config);
    _configMock.Setup(c => c.UpdateConfig(It.IsAny<Action<AppConfig>>()))
        .Callback<Action<AppConfig>>(action => action(_config));

    _service = new UpdateService(_configMock.Object);
  }

  [Fact]
  public void Constructor_ThrowsOnNullConfig()
  {
    // Act
    var act = () => new UpdateService(null!);

    // Assert
    act.Should().Throw<ArgumentNullException>();
  }

  [Fact]
  public async Task ForceCheckAsync_DoesNotThrow()
  {
    // Act
    var updateInfo = await _service.ForceCheckAsync();

    // Assert
    updateInfo.Should().NotBeNull();
    // В тестовой среде GitHub API может быть недоступен — это нормально
    updateInfo.CurrentVersion.Should().NotBeNullOrEmpty();
  }

  [Fact]
  public async Task StartStop_DoesNotThrow()
  {
    // Act & Assert
    _service.Start();
    _service.Stop();
    await Task.CompletedTask;
  }

  [Fact]
  public async Task StartTwice_RestartsWithoutCrash()
  {
    // Act
    _service.Start();
    _service.Start(); // Должен перезапустить без ошибки
    _service.Stop();

    await Task.CompletedTask;
  }

  public void Dispose()
  {
    _service.Dispose();
  }
}
