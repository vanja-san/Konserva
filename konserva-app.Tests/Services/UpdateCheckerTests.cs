using Konserva.Services;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для UpdateChecker
/// </summary>
public class UpdateCheckerTests
{
    #region IsNewerVersion Tests (via reflection)

    [Theory]
    [InlineData("1.5.0", "1.5.1", true)]
    [InlineData("1.5.0", "2.0.0", true)]
    [InlineData("1.5.0", "1.6.0", true)]
    [InlineData("1.5.1", "1.5.0", false)]
    [InlineData("2.0.0", "1.5.0", false)]
    [InlineData("1.5.0", "1.5.0", false)]
    [InlineData("1.0.0", "0.9.9", false)]
    public void IsNewerVersion_CompareVersions_ReturnsCorrectResult(string current, string latest, bool expectedNewer)
    {
        // Arrange
        var method = typeof(UpdateChecker).GetMethod("IsNewerVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act
        var result = method!.Invoke(null, [current, latest]);

        // Assert
        result.Should().Be(expectedNewer);
    }

    [Theory]
    [InlineData("invalid", "1.0.0", false)]
    [InlineData("1.0.0", "invalid", false)]
    [InlineData("", "1.0.0", false)]
    [InlineData("1.0.0", "", false)]
    public void IsNewerVersion_InvalidVersion_ReturnsFalse(string current, string latest, bool expected)
    {
        var method = typeof(UpdateChecker).GetMethod("IsNewerVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method!.Invoke(null, [current, latest]);
        result.Should().Be(expected);
    }

    #endregion

    #region GetCurrentVersion Tests

    [Fact]
    public void GetCurrentVersion_ReturnsValidVersionString()
    {
        var method = typeof(UpdateChecker).GetMethod("GetCurrentVersion",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method!.Invoke(null, null);

        result.Should().NotBeNull();
        var versionStr = result as string;
        versionStr.Should().NotBeNullOrEmpty();
        Version.TryParse(versionStr, out _).Should().BeTrue();
    }

    #endregion

    #region DetectBuildType Tests

    [Fact]
    public void DetectBuildType_ReturnsValidBuildType()
    {
        var result = UpdateChecker.DetectBuildType();
        result.Should().BeOneOf("full", "deps");
    }

    #endregion

    #region CheckAsync Integration Tests

    [Fact]
    public async Task CheckAsync_ReturnsUpdateInfo_WithCurrentVersion()
    {
        // Act
        var result = await UpdateChecker.CheckAsync();

        // Assert
        result.Should().NotBeNull();
        result.CurrentVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CheckAsync_ReturnsUpdateInfo_EvenOnNetworkFailure()
    {
        // Даже при сетевой ошибке метод не должен бросать исключений
        var action = async () => await UpdateChecker.CheckAsync();
        await action.Should().NotThrowAsync();
    }

    #endregion
}

/// <summary>
/// Тесты для UpdateInfo model
/// </summary>
public class UpdateInfoTests
{
    [Fact]
    public void UpdateInfo_DefaultValues_AreCorrect()
    {
        var info = new UpdateInfo();

        info.IsAvailable.Should().BeFalse();
        info.NewVersion.Should().Be("");
        info.CurrentVersion.Should().Be("");
        info.DownloadUrl.Should().Be("");
        info.AssetName.Should().Be("");
        info.ReleaseNotes.Should().Be("");
        info.ChangelogUrl.Should().Be("");
        info.SizeBytes.Should().Be(0L);
    }

    [Fact]
    public void UpdateInfo_CanSetAllProperties()
    {
        var info = new UpdateInfo
        {
            IsAvailable = true,
            NewVersion = "1.6.0",
            CurrentVersion = "1.5.0",
            DownloadUrl = "https://example.com/update.zip",
            AssetName = "update.zip",
            ReleaseNotes = "Bug fixes",
            ChangelogUrl = "https://github.com/changelog",
            SizeBytes = 50_000_000
        };

        info.IsAvailable.Should().BeTrue();
        info.NewVersion.Should().Be("1.6.0");
        info.CurrentVersion.Should().Be("1.5.0");
        info.DownloadUrl.Should().Be("https://example.com/update.zip");
        info.AssetName.Should().Be("update.zip");
        info.ReleaseNotes.Should().Be("Bug fixes");
        info.ChangelogUrl.Should().Be("https://github.com/changelog");
        info.SizeBytes.Should().Be(50_000_000);
    }
}
