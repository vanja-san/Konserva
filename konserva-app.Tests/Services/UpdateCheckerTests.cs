using System.Net;
using System.Net.Http;
using Konserva.Services;
using Moq;
using Moq.Protected;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для UpdateChecker.
/// <para>
/// HttpClient собирается на заглушке <see cref="HttpMessageHandler"/>: реальные
/// запросы к raw.githubusercontent.com делали тесты сетевыми и нестабильными.
/// </para>
/// </summary>
[Trait("Category", "Integration")]
public class UpdateCheckerTests
{
    /// <summary>
    /// Создаёт HttpClient, отвечающий заданным статусом и телом.
    /// </summary>
    private static HttpClient CreateClient(HttpStatusCode status, string content = "")
    {
        // Loose, а не Strict: HttpClient.Dispose() дёргает Dispose у обработчика,
        // а у Strict-мока нет на это настройки и тест падает на уборке.
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(content)
            });

        return new HttpClient(handler.Object);
    }

    /// <summary>
    /// Манифест, где текущая сборка считается устаревшей.
    /// </summary>
    private static string ManifestWithUpdate(string sha256 = "e05dce6cffeb19130b2c0a08ec97185f122994cc2d431ff9b5bb00dbf3fbb7cc") =>
        $$"""
        {
          "latestVersion": "99.0.0",
          "minRequiredVersion": "0.0.1",
          "downloads": {
            "deps": {
              "url": "https://example.com/Konserva-Deps.zip",
              "sizeBytes": 1234,
              "assetName": "Konserva-Deps.zip",
              "sha256": "{{sha256}}"
            }
          },
          "releaseNotes": "notes",
          "changelogUrl": "https://example.com/changelog"
        }
        """;

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
        // Arrange
        using var httpClient = CreateClient(HttpStatusCode.OK);
        var checker = new UpdateChecker(httpClient);

        // Act
        var result = checker.GetCurrentVersion();

        // Assert
        result.Should().NotBeNullOrEmpty();
        Version.TryParse(result, out _).Should().BeTrue();
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

    #region CheckAsync Tests (без сети)

    [Fact]
    public async Task CheckAsync_ReturnsUpdateInfo_WithCurrentVersion()
    {
        // Arrange
        using var httpClient = CreateClient(HttpStatusCode.OK, ManifestWithUpdate());
        var checker = new UpdateChecker(httpClient);

        // Act
        var result = await checker.CheckAsync();

        // Assert
        result.Should().NotBeNull();
        result.CurrentVersion.Should().NotBeNullOrEmpty();
        result.IsCheckSuccessful.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAsync_ReturnsUpdateInfo_EvenOnNetworkFailure()
    {
        // Arrange
        using var httpClient = CreateClient(HttpStatusCode.ServiceUnavailable);
        var checker = new UpdateChecker(httpClient);

        // Act & Assert: даже при сетевой ошибке метод не должен бросать исключений
        var action = async () => await checker.CheckAsync();
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAsync_OnNetworkFailure_ReportsUnsuccessfulCheck()
    {
        using var httpClient = CreateClient(HttpStatusCode.ServiceUnavailable);
        var checker = new UpdateChecker(httpClient);

        var result = await checker.CheckAsync();

        result.IsCheckSuccessful.Should().BeFalse();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_OnMalformedJson_DoesNotThrow()
    {
        using var httpClient = CreateClient(HttpStatusCode.OK, "{ not json ");
        var checker = new UpdateChecker(httpClient);

        var action = async () => await checker.CheckAsync();

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckAsync_WhenUpToDate_DoesNotOfferUpdate()
    {
        using var httpClient = CreateClient(HttpStatusCode.OK,
            """{ "latestVersion": "0.0.1" }""");
        var checker = new UpdateChecker(httpClient);

        var result = await checker.CheckAsync();

        result.IsCheckSuccessful.Should().BeTrue();
        result.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAsync_WhenUpdateAvailable_PopulatesFieldsFromManifest()
    {
        const string expectedSha = "e05dce6cffeb19130b2c0a08ec97185f122994cc2d431ff9b5bb00dbf3fbb7cc";
        using var httpClient = CreateClient(HttpStatusCode.OK, ManifestWithUpdate(expectedSha));
        var checker = new UpdateChecker(httpClient);

        var result = await checker.CheckAsync();

        // В тестовой сборке exe < 30 МБ, поэтому выбирается ассет "deps".
        result.IsAvailable.Should().BeTrue();
        result.NewVersion.Should().Be("99.0.0");
        result.AssetName.Should().Be("Konserva-Deps.zip");
        result.DownloadUrl.Should().Be("https://example.com/Konserva-Deps.zip");
        result.SizeBytes.Should().Be(1234);
        result.Sha256.Should().Be(expectedSha);
        result.ReleaseNotes.Should().Be("notes");
    }

    #endregion
}

/// <summary>
/// Тесты для UpdateInfo model
/// </summary>
[Trait("Category", "Integration")]
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
