using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Konserva.Tests.Services;

// Отключаем параллельное выполнение — тесты используют общий servers.json
[Collection("Sequential")]
public class ServerStorageServiceTests : IDisposable
{
    private readonly ServerStorageService _service;
    private readonly string _testServersPath;
    private readonly string _serversIndexPath;

    public ServerStorageServiceTests()
    {
        _testServersPath = Path.Combine(Path.GetTempPath(), $"konserva_srv_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testServersPath);
        _serversIndexPath = Path.Combine(_testServersPath, "servers.json");

        var configMock = new Mock<IConfigService>();
        var config = new AppConfig { ServersDirectory = _testServersPath };
        configMock.Setup(c => c.GetConfig()).Returns(config);

        _service = new ServerStorageService(configMock.Object);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_testServersPath))
        {
            try { Directory.Delete(_testServersPath, true); }
            catch { /* ignore */ }
        }
    }

    [Fact]
    public void LoadServers_ReturnsEmptyList_WhenNoServers()
    {
        var servers = _service.LoadServers();
        Assert.NotNull(servers);
        Assert.Empty(servers);
    }

    [Fact]
    public async Task LoadServersAsync_ReturnsEmptyList_WhenNoServers()
    {
        var servers = await _service.LoadServersAsync();
        Assert.NotNull(servers);
        Assert.Empty(servers);
    }

    [Fact]
    public void SaveServers_And_LoadServers_RoundTrip()
    {
        var servers = new List<Server>
        {
            new() { Id = "test-1", Name = "Test Server 1", McVersion = "1.20.4", ModLoader = new ModLoader { Type = ModLoaderType.Vanilla } },
            new() { Id = "test-2", Name = "Test Server 2", McVersion = "1.21.1", ModLoader = new ModLoader { Type = ModLoaderType.Fabric } }
        };

        _service.SaveServers(servers);
        var loaded = _service.LoadServers();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("Test Server 1", loaded[0].Name);
        Assert.Equal("Test Server 2", loaded[1].Name);
    }

    [Fact]
    public async Task SaveServersAsync_And_LoadServersAsync_RoundTrip()
    {
        var servers = new List<Server>
        {
            new() { Id = "test-1", Name = "Async Server 1", McVersion = "1.20.4", ModLoader = new ModLoader { Type = ModLoaderType.Vanilla } }
        };

        await _service.SaveServersAsync(servers);
        var loaded = await _service.LoadServersAsync();

        Assert.Single(loaded);
        Assert.Equal("Async Server 1", loaded[0].Name);
    }
}
