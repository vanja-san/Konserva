using Konserva.Services;
using Moq;
using Moq.Protected;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Xunit;

namespace Konserva.Tests.Services;

public class McVersionsApiTests : IDisposable
{
    private readonly string _cacheFolder;
    private readonly string _cacheFile;
    private bool _disposed;

    public McVersionsApiTests()
    {
        // Каждый тест получает свою временную папку — никаких конфликтов файлового кэша
        _cacheFolder = Path.Combine(Path.GetTempPath(), $"konserva_mcapi_test_{Guid.NewGuid()}");
        _cacheFile = Path.Combine(_cacheFolder, "versions_cache.json");
        Directory.CreateDirectory(_cacheFolder);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { Directory.Delete(_cacheFolder, recursive: true); }
        catch { /* игнорируем ошибки при очистке */ }
    }

    [Fact]
    public async Task GetMcVersions_ReturnsVersions_FromMoJangApi()
    {
        var mockResponse = new
        {
            latest = new { release = "1.20.4", snapshot = "1.20.4" },
            versions = new[]
            {
                new { id = "1.20.4", type = "release" },
                new { id = "1.20.3", type = "release" },
                new { id = "1.20.2", type = "release" }
            }
        };

        var api = CreateApiWithMockHandler(JsonSerializer.Serialize(mockResponse));
        var versions = await api.GetMcVersions();

        Assert.Equal(3, versions.Length);
        Assert.Contains("1.20.4", versions);
        Assert.Contains("1.20.3", versions);
    }

    [Fact]
    public async Task GetMcVersions_ReturnsEmptyArray_OnFailure()
    {
        var api = CreateApiWithMockHandler("invalid json");
        var versions = await api.GetMcVersions();

        Assert.NotNull(versions);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task GetFabricVersions_ParsesCorrectly()
    {
        var mockResponse = new[]
        {
            new { loader = new { version = "0.15.0" } },
            new { loader = new { version = "0.14.0" } }
        };

        var api = CreateApiWithMockHandler(JsonSerializer.Serialize(mockResponse));
        var versions = await api.GetFabricVersions("1.20.4");

        Assert.Equal(2, versions.Length);
        Assert.Equal("0.15.0", versions[0]);
        Assert.Equal("0.14.0", versions[1]);
    }

    [Fact]
    public async Task GetFabricVersions_ReturnsLatest_OnFailure()
    {
        var api = CreateApiWithMockHandler("invalid");
        var versions = await api.GetFabricVersions("1.20.4");

        Assert.Single(versions);
        Assert.Equal("latest", versions[0]);
    }

    [Fact]
    public async Task GetQuiltVersions_ParsesCorrectly()
    {
        var mockResponse = new[]
        {
            new { loader = new { version = "0.25.0" } },
            new { loader = new { version = "0.24.0-beta.1" } }
        };

        var api = CreateApiWithMockHandler(JsonSerializer.Serialize(mockResponse));
        var versions = await api.GetQuiltVersions("1.20.4");

        Assert.Equal(2, versions.Length);
        // Stable versions should come first
        Assert.Equal("0.25.0", versions[0]);
    }

    [Fact]
    public async Task GetQuiltVersions_ReturnsLatest_On404()
    {
        // API возвращает ["latest"] при 404 как fallback
        var api = CreateApiWithMockHandler("Not Found", HttpStatusCode.NotFound);
        var versions = await api.GetQuiltVersions("1.0.0");

        Assert.Single(versions);
        Assert.Equal("latest", versions[0]);
    }

    private McVersionsApi CreateApiWithMockHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        var httpClient = new HttpClient(mockHandler.Object);
        return new McVersionsApi(httpClient, _cacheFolder);
    }
}
