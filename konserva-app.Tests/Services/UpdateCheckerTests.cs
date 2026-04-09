using Konserva.Models;
using Konserva.Services;
using System.Text.Json;

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

    #region FindAsset Tests

    [Fact]
    public void FindAsset_ReturnsMatchingAsset_ForFullBuildType()
    {
        // Arrange: создаём фейковый JSON ответа GitHub Releases
        var json = """
        {
            "assets": [
                {
                    "name": "Konserva-1.5.0-deps.zip",
                    "browser_download_url": "https://example.com/deps.zip",
                    "size": 10000000
                },
                {
                    "name": "Konserva-1.5.0-full.zip",
                    "browser_download_url": "https://example.com/full.zip",
                    "size": 60000000
                }
            ]
        }
        """;
        var release = JsonDocument.Parse(json).RootElement;

        var method = typeof(UpdateChecker).GetMethod("FindAsset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act
        var result = ((string name, string url, long size))method!.Invoke(null, [release, "full"])!;

        // Assert
        result.name.Should().Be("Konserva-1.5.0-full.zip");
        result.url.Should().Be("https://example.com/full.zip");
        result.size.Should().Be(60000000);
    }

    [Fact]
    public void FindAsset_ReturnsMatchingAsset_ForDepsBuildType()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "Konserva-1.5.0-deps.zip",
                    "browser_download_url": "https://example.com/deps.zip",
                    "size": 10000000
                },
                {
                    "name": "Konserva-1.5.0-full.zip",
                    "browser_download_url": "https://example.com/full.zip",
                    "size": 60000000
                }
            ]
        }
        """;
        var release = JsonDocument.Parse(json).RootElement;

        var method = typeof(UpdateChecker).GetMethod("FindAsset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = ((string name, string url, long size))method!.Invoke(null, [release, "deps"])!;

        result.name.Should().Be("Konserva-1.5.0-deps.zip");
        result.url.Should().Be("https://example.com/deps.zip");
        result.size.Should().Be(10000000);
    }

    [Fact]
    public void FindAsset_ReturnsEmpty_WhenNoMatchingAsset()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "Konserva-1.5.0-deps.zip",
                    "browser_download_url": "https://example.com/deps.zip",
                    "size": 10000000
                }
            ]
        }
        """;
        var release = JsonDocument.Parse(json).RootElement;

        var method = typeof(UpdateChecker).GetMethod("FindAsset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = ((string name, string url, long size))method!.Invoke(null, [release, "full"])!;

        result.name.Should().BeEmpty();
        result.url.Should().BeEmpty();
        result.size.Should().Be(0);
    }

    [Fact]
    public void FindAsset_ReturnsEmpty_WhenNoAssetsArray()
    {
        var json = """{ "tag_name": "v1.5.0" }""";
        var release = JsonDocument.Parse(json).RootElement;

        var method = typeof(UpdateChecker).GetMethod("FindAsset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = ((string name, string url, long size))method!.Invoke(null, [release, "full"])!;

        result.name.Should().BeEmpty();
        result.url.Should().BeEmpty();
        result.size.Should().Be(0);
    }

    [Fact]
    public void FindAsset_IgnoresNonZipAssets()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "Konserva-1.5.0-full.exe",
                    "browser_download_url": "https://example.com/full.exe",
                    "size": 60000000
                }
            ]
        }
        """;
        var release = JsonDocument.Parse(json).RootElement;

        var method = typeof(UpdateChecker).GetMethod("FindAsset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = ((string name, string url, long size))method!.Invoke(null, [release, "full"])!;

        result.name.Should().BeEmpty();
    }

    [Fact]
    public void FindAsset_CaseInsensitiveSuffixMatch()
    {
        var json = """
        {
            "assets": [
                {
                    "name": "Konserva-1.5.0-FULL.ZIP",
                    "browser_download_url": "https://example.com/full.zip",
                    "size": 60000000
                }
            ]
        }
        """;
        var release = JsonDocument.Parse(json).RootElement;

        var method = typeof(UpdateChecker).GetMethod("FindAsset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = ((string name, string url, long size))method!.Invoke(null, [release, "full"])!;

        result.name.Should().Be("Konserva-1.5.0-FULL.ZIP");
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
