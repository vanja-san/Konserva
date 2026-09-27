using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для UpdateService.
/// </summary>
[Trait("Category", "Unit")]
public class UpdateServiceTests : IDisposable
{
  private readonly Mock<IConfigService> _configMock;
  private readonly Mock<IUpdateChecker> _updateCheckerMock;
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

    _updateCheckerMock = new Mock<IUpdateChecker>();
    _updateCheckerMock.Setup(c => c.GetCurrentVersion()).Returns("1.0.0");
    _updateCheckerMock.Setup(c => c.CheckAsync()).ReturnsAsync(new UpdateInfo
    {
      CurrentVersion = "1.0.0",
      IsCheckSuccessful = true,
      IsAvailable = false
    });

    _service = new UpdateService(_configMock.Object, _updateCheckerMock.Object);
  }

  [Fact]
  public void Constructor_ThrowsOnNullConfig()
  {
    // Act
    var act = () => new UpdateService(null!, _updateCheckerMock.Object);

    // Assert
    act.Should().Throw<ArgumentNullException>();
  }

  [Fact]
  public void Constructor_ThrowsOnNullUpdateChecker()
  {
    // Act
    var act = () => new UpdateService(_configMock.Object, null!);

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

  [Fact]
  public async Task Start_WithAvailableUpdate_RaisesUpdateAvailable()
  {
    // Arrange
    var updateInfo = new UpdateInfo
    {
      CurrentVersion = "1.0.0",
      NewVersion = "1.1.0",
      IsCheckSuccessful = true,
      IsAvailable = true
    };
    _updateCheckerMock.Setup(c => c.CheckAsync()).ReturnsAsync(updateInfo);

    var tcs = new TaskCompletionSource<UpdateInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
    _service.UpdateAvailable += i => tcs.TrySetResult(i);

    // Act
    _service.Start();

    // Assert
    var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
    completed.Should().BeSameAs(tcs.Task, "автопроверка при запуске должна опубликовать UpdateAvailable");
    (await tcs.Task).Should().BeSameAs(updateInfo);
  }

  [Fact]
  public async Task Start_WithNoUpdate_DoesNotRaiseUpdateAvailable()
  {
    // Arrange — мок возвращает IsAvailable=false по умолчанию
    var checkCompleted = new TaskCompletionSource<UpdateInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
    var updateAvailableRaised = false;
    _service.UpdateAvailable += _ => updateAvailableRaised = true;
    _service.CheckCompleted += i => checkCompleted.TrySetResult(i);

    // Act
    _service.Start();

    // Assert
    var completed = await Task.WhenAny(checkCompleted.Task, Task.Delay(TimeSpan.FromSeconds(5)));
    completed.Should().BeSameAs(checkCompleted.Task, "стартовая проверка должна завершиться");

    await Task.Delay(200);
    updateAvailableRaised.Should().BeFalse();
  }

  public void Dispose()
  {
    _service.Dispose();
  }
}
