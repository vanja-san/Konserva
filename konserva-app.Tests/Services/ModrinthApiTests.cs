using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Konserva.Tests.Services;

public class ModrinthApiTests
{
    [Fact]
    public async Task GetLatestVersionsAsync_SendsHashLookupRequest()
    {
        var (api, capture) = CreateApi("{}");

        await api.GetLatestVersionsAsync(
            ["hash-a", "hash-b"],
            ["fabric"],
            "1.21.1",
            ["release"]);

        capture.CallCount.Should().Be(1);
        capture.Body.Should().NotBeNull();

        var json = JsonNode.Parse(capture.Body!)!;
        json["algorithm"]!.GetValue<string>().Should().Be("sha512");
        JsonStrings(json["hashes"]).Should().BeEquivalentTo(["hash-a", "hash-b"]);
        JsonStrings(json["loaders"]).Should().Equal("fabric");
        JsonStrings(json["game_versions"]).Should().Equal("1.21.1");
        JsonStrings(json["version_types"]).Should().Equal("release");
    }

    [Fact]
    public async Task GetLatestVersionsAsync_ParsesMatchingVersions()
    {
        const string body = """
        {
          "oldhash": {
            "id": "ver1",
            "project_id": "proj1",
            "version_number": "2.0.0",
            "version_type": "release",
            "date_published": "2024-05-01T12:00:00.000000Z",
            "files": [
              {
                "url": "https://cdn.modrinth.com/data/proj1/versions/ver1/a.jar",
                "filename": "a-2.0.0.jar",
                "primary": true,
                "hashes": { "sha1": "aaa", "sha512": "newhash" }
              }
            ]
          }
        }
        """;
        var (api, _) = CreateApi(body);

        var result = await api.GetLatestVersionsAsync(["oldhash"], ["fabric"], "1.21.1", ["release"]);

        result.Should().ContainKey("oldhash");
        var version = result["oldhash"];
        version.VersionNumber.Should().Be("2.0.0");
        version.ProjectId.Should().Be("proj1");
        version.PrimaryFile.Should().NotBeNull();
        version.PrimaryFile!.FileName.Should().Be("a-2.0.0.jar");
        version.PrimaryFile!.Hashes.Sha512.Should().Be("newhash");
        version.DatePublished.Year.Should().Be(2024);
    }

    [Fact]
    public async Task GetCurrentVersionsAsync_ParsesResponse()
    {
        const string body = """
        {
          "localhash": {
            "id": "ver0",
            "project_id": "proj1",
            "version_number": "1.9.0",
            "version_type": "release",
            "date_published": "2024-01-01T00:00:00.000000Z",
            "files": []
          }
        }
        """;
        var (api, capture) = CreateApi(body);

        var result = await api.GetCurrentVersionsAsync(["localhash"]);

        capture.CallCount.Should().Be(1);
        result.Should().ContainKey("localhash");
        result["localhash"].VersionNumber.Should().Be("1.9.0");
    }

    [Fact]
    public async Task GetLatestVersionsAsync_ReturnsEmpty_OnServerError()
    {
        var (api, _) = CreateApi("boom", HttpStatusCode.InternalServerError);

        var result = await api.GetLatestVersionsAsync(["h"], ["fabric"], "1.21.1", ["release"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestVersionsAsync_ReturnsEmpty_OnGone()
    {
        var (api, _) = CreateApi("gone", HttpStatusCode.Gone);

        var result = await api.GetLatestVersionsAsync(["h"], ["fabric"], "1.21.1", ["release"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestVersionsAsync_ReturnsEmpty_OnInvalidJson()
    {
        var (api, _) = CreateApi("not json");

        var result = await api.GetLatestVersionsAsync(["h"], ["fabric"], "1.21.1", ["release"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestVersionsAsync_DoesNotCallApi_WhenNoHashes()
    {
        var (api, capture) = CreateApi("{}");

        var result = await api.GetLatestVersionsAsync([], ["fabric"], "1.21.1", ["release"]);

        result.Should().BeEmpty();
        capture.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetProjectsAsync_SendsEncodedIdsInQuery()
    {
        var (api, capture) = CreateApi("[]");

        await api.GetProjectsAsync(["proj1", "proj2"]);

        capture.CallCount.Should().Be(1);
        capture.Url.Should().Contain("/projects?ids=");
        capture.Url.Should().Contain("proj1");
        capture.Url.Should().Contain("proj2");
        capture.Url.Should().Contain("%5B");
        capture.Url.Should().Contain("%5D");
        capture.Url.Should().NotContain("version_files");
    }

    [Fact]
    public async Task GetProjectsAsync_ParsesProjectsByTitle()
    {
        const string body = """
        [
          { "id": "proj1", "slug": "my-mod", "title": "My Awesome Mod" },
          { "id": "proj2", "slug": "other", "title": "Other Mod" }
        ]
        """;
        var (api, _) = CreateApi(body);

        var result = await api.GetProjectsAsync(["proj1", "proj2"]);

        result.Should().HaveCount(2);
        result["proj1"].Title.Should().Be("My Awesome Mod");
        result["proj1"].Slug.Should().Be("my-mod");
        result["proj2"].Title.Should().Be("Other Mod");
    }

    [Fact]
    public async Task GetProjectsAsync_ReturnsEmpty_OnServerError()
    {
        var (api, _) = CreateApi("boom", HttpStatusCode.InternalServerError);

        var result = await api.GetProjectsAsync(["proj1"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectsAsync_ReturnsEmpty_OnGone()
    {
        var (api, _) = CreateApi("gone", HttpStatusCode.Gone);

        var result = await api.GetProjectsAsync(["proj1"]);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectsAsync_DoesNotCallApi_WhenNoIds()
    {
        var (api, capture) = CreateApi("[]");

        var result = await api.GetProjectsAsync([]);

        result.Should().BeEmpty();
        capture.CallCount.Should().Be(0);
    }

    private static IEnumerable<string> JsonStrings(JsonNode? node) =>
        node!.AsArray().Select(n => n!.GetValue<string>());

    private static (ModrinthApi api, Capture capture) CreateApi(
        string responseContent,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var capture = new Capture();
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capture.CallCount++;
                capture.Url = request.RequestUri?.ToString();
                capture.Body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(responseContent)
                };
            });

        var httpClient = new HttpClient(mockHandler.Object);
        return (new ModrinthApi(httpClient), capture);
    }

    private sealed class Capture
    {
        public int CallCount { get; set; }
        public string? Url { get; set; }
        public string? Body { get; set; }
    }
}
