using Konserva.Localization;
using Konserva.Models;
using Konserva.Services;
using Konserva.ViewModels;
using Moq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Xunit;

namespace Konserva.Tests;

/// <summary>
/// Covers <see cref="SettingsViewModel"/>. The ViewModel is fully injectable, so
/// its mapping between <see cref="AppConfig"/> and bindable state is testable
/// without opening a window.
/// </summary>
/// <remarks>
/// SaveSettings calls the process-wide <see cref="LocalizationManager.SetLanguage"/>,
/// so the culture is captured on construction and restored on dispose. Otherwise
/// this class would silently re-localise every culture-sensitive test that runs
/// after it in the same process.
/// </remarks>
[Collection("Sequential")]
[Trait("Category", "Unit")]
public class SettingsViewModelTests : IDisposable
{
    private readonly string _originalCulture = LocalizationManager.CurrentCulture.Name;

    private readonly Mock<IConfigService> _configMock = new();
    private readonly Mock<IJavaManagementService> _javaMock = new();
    private readonly Mock<IUpdateService> _updateMock = new();
    private readonly AppConfig _config = new();
    private readonly SettingsViewModel _viewModel;

    public SettingsViewModelTests()
    {
        _configMock.Setup(x => x.GetConfig()).Returns(_config);
        _viewModel = new SettingsViewModel(_configMock.Object, _javaMock.Object, _updateMock.Object);
    }

    public void Dispose() => LocalizationManager.SetLanguage(_originalCulture);

    [Fact]
    public void LoadSettings_MapsConfigToProperties()
    {
        _config.ServersDirectory = @"D:\Servers";
        _config.CheckUpdates = true;
        _config.UpdateCheckIntervalHours = 48;
        _config.MinimizeToTrayMode = "Minimize";
        _config.ConsoleAutoScroll = false;
        _config.ConsoleWordWrap = true;
        _config.JavaInstallations = [new JavaInstallation { Id = "j1", Name = "Java 21" }];

        _viewModel.LoadSettings();

        Assert.Equal(@"D:\Servers", _viewModel.ServersDirectory);
        Assert.True(_viewModel.CheckUpdatesScheduled);
        Assert.Equal(48, _viewModel.UpdateIntervalHours);
        Assert.Equal("Minimize", _viewModel.MinimizeToTrayMode);
        Assert.False(_viewModel.ConsoleAutoScroll);
        Assert.True(_viewModel.ConsoleWordWrap);
        Assert.Single(_viewModel.JavaInstallations);
        Assert.False(_viewModel.IsJavaEmpty);
    }

    /// <summary>
    /// A hand-edited config must not be able to produce a nonsensical interval —
    /// 0 or a negative value would make the scheduled check fire constantly.
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(500, 168)]
    public void LoadSettings_ClampsUpdateInterval(int configured, int expected)
    {
        _config.UpdateCheckIntervalHours = configured;

        _viewModel.LoadSettings();

        Assert.Equal(expected, _viewModel.UpdateIntervalHours);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void CheckUpdatesMode_DerivedPropertiesFollowScheduledFlag(
        bool scheduled, bool expectedOnLaunch, bool expectedVisible)
    {
        _viewModel.CheckUpdatesScheduled = scheduled;

        Assert.Equal(expectedOnLaunch, _viewModel.CheckUpdatesOnLaunch);
        Assert.Equal(expectedVisible, _viewModel.IsScheduledModeVisible);
        Assert.NotEqual(string.Empty, _viewModel.CheckUpdatesModeText);
    }

    /// <summary>
    /// Guards the [NotifyPropertyChangedFor] wiring: without it the toggle buttons
    /// keep their old state because nothing tells the view to re-read them.
    /// </summary>
    [Fact]
    public void CheckUpdatesScheduled_RaisesChangeForEveryDependentProperty()
    {
        var changed = new List<string>();
        _viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        _viewModel.CheckUpdatesScheduled = true;

        Assert.Contains(nameof(SettingsViewModel.CheckUpdatesOnLaunch), changed);
        Assert.Contains(nameof(SettingsViewModel.IsScheduledModeVisible), changed);
        Assert.Contains(nameof(SettingsViewModel.CheckUpdatesModeText), changed);
    }

    [Fact]
    public void JavaInstallations_RaisesChangeForIsJavaEmpty()
    {
        var changed = new List<string>();
        _viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        _viewModel.JavaInstallations = [new JavaInstallation { Id = "j1" }];

        Assert.Contains(nameof(SettingsViewModel.IsJavaEmpty), changed);
    }

    [Fact]
    public void UpdateIntervalHours_RaisesChangeForUpdateIntervalText()
    {
        var changed = new List<string>();
        _viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        _viewModel.UpdateIntervalHours = 72;

        Assert.Contains(nameof(SettingsViewModel.UpdateIntervalText), changed);
    }

    [Fact]
    public void SaveSettings_WritesPropertiesBackToConfig()
    {
        _viewModel.LoadSettings();

        _viewModel.ServersDirectory = @"E:\Games";
        _viewModel.CheckUpdatesScheduled = true;
        _viewModel.UpdateIntervalHours = 12;
        _viewModel.MinimizeToTrayMode = "Close";
        _viewModel.ConsoleAutoScroll = false;

        _viewModel.SaveSettings();

        Assert.Equal(@"E:\Games", _config.ServersDirectory);
        Assert.True(_config.CheckUpdates);
        Assert.Equal(12, _config.UpdateCheckIntervalHours);
        Assert.Equal("Close", _config.MinimizeToTrayMode);
        Assert.False(_config.ConsoleAutoScroll);
        _configMock.Verify(x => x.SaveConfig(_config), Times.Once);
    }

    [Fact]
    public void SaveSettings_ReturnsTrueOnlyWhenLanguageChanged()
    {
        _viewModel.LoadSettings();

        Assert.False(_viewModel.SaveSettings());
        Assert.False(_viewModel.SaveSettings());

        _viewModel.Language = "ru-RU";

        Assert.True(_viewModel.SaveSettings());
        Assert.Equal("ru-RU", _config.Language);
    }

    /// <summary>
    /// LoadSettings sets _isLoading so that the PropertyChanged side effects it
    /// triggers do not immediately write the partially loaded state back to disk.
    /// </summary>
    [Fact]
    public void SaveSettings_DoesNotPersistDuringLoad()
    {
        _viewModel.LoadSettings();

        _configMock.Verify(x => x.SaveConfig(It.IsAny<AppConfig>()), Times.Never);
    }

    [Fact]
    public void SetServersDirectory_PersistsImmediately()
    {
        _viewModel.SetServersDirectory(@"F:\Minecraft");

        Assert.Equal(@"F:\Minecraft", _viewModel.ServersDirectory);
        Assert.Equal(@"F:\Minecraft", _config.ServersDirectory);
        _configMock.Verify(x => x.SaveConfig(_config), Times.Once);
    }

    [Fact]
    public void RemoveJava_ReturnsResultFromService()
    {
        _javaMock.Setup(x => x.RemoveJava(It.IsAny<string>())).Returns(true);

        Assert.True(_viewModel.RemoveJava("j1"));

        _javaMock.Setup(x => x.RemoveJava(It.IsAny<string>())).Returns(false);

        Assert.False(_viewModel.RemoveJava("j1"));
    }

    [Fact]
    public void AddJava_ReturnsNullWhenServiceRejectsPath()
    {
        _javaMock.Setup(x => x.AddJava(It.IsAny<string>())).Returns((JavaInstallation?)null);

        Assert.Null(_viewModel.AddJava(@"C:\Java\bin\java.exe"));
    }

    [Fact]
    public void ScanJava_ReplacesInstallationsAndReturnsCount()
    {
        _javaMock.Setup(x => x.ScanAndAddJava()).Returns(
        [
            new JavaInstallation { Id = "a" },
            new JavaInstallation { Id = "b" }
        ]);

        var count = _viewModel.ScanJava();

        Assert.Equal(2, count);
        Assert.Equal(2, _viewModel.JavaInstallations.Count);
        Assert.False(_viewModel.IsJavaEmpty);
    }

    [Fact]
    public void FormatUpdateInterval_SwitchesFromHoursToDaysAboveOneDay()
    {
        var under = _viewModel.FormatUpdateInterval(12);
        var over = _viewModel.FormatUpdateInterval(48);

        Assert.NotEqual(under, over);
        Assert.NotEqual(string.Empty, under);
        Assert.NotEqual(string.Empty, over);
    }

    [Fact]
    public void LoadSettings_ExposesMutableCollection()
    {
        _viewModel.LoadSettings();

        _viewModel.JavaInstallations.Add(new JavaInstallation { Id = "new" });

        Assert.Equal("new", _viewModel.JavaInstallations[0].Id);
        Assert.IsType<ObservableCollection<JavaInstallation>>(_viewModel.JavaInstallations);
    }

    [Fact]
    public void PropertyChanged_IsRaisedForSourceGeneratedProperties()
    {
        var changed = new List<string>();
        ((INotifyPropertyChanged)_viewModel).PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        _viewModel.ServersDirectory = @"C:\New";

        Assert.Contains(nameof(SettingsViewModel.ServersDirectory), changed);
    }
}