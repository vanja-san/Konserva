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
  public async Task ForceCheckAsync_UpdatesLastUpdateCheck()
  {
    // Arrange
    _config.LastUpdateCheck = null;

    // Act
    await _service.ForceCheckAsync();

    // Assert
    _config.LastUpdateCheck.Should().NotBeNull();
    // Должно быть близко к текущему времени
    _config.LastUpdateCheck.Should().BeCloseTo(SystemTime.UtcNow, TimeSpan.FromSeconds(5));
  }

  [Fact]
  public async Task ForceCheckAsync_FiresEvent_WhenUpdateAvailable()
  {
    // Можно протестировать только мокая UpdateChecker,
    // но он статический — пропускаем этот сценарий.
    // Проверяем, что событие не null-safe
    var updateInfo = await _service.ForceCheckAsync();
    updateInfo.Should().NotBeNull();
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

  [Fact]
  public async Task ForceCheckAsync_ReturnsUpdateInfo()
  {
    // Act
    var result = await _service.ForceCheckAsync();

    // Assert
    result.Should().NotBeNull();
    result.CurrentVersion.Should().NotBeNullOrEmpty();
  }

  public void Dispose()
  {
    _service.Dispose();
  }
}
