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

    public ServerStorageServiceTests()
    {
        // Сервис использует AppContext.BaseDirectory/Servers/servers.json
        // Удаляем старый файл чтобы тесты были изолированы
        var serversIndexPath = Path.Combine(AppContext.BaseDirectory, "Servers", "servers.json");
        if (File.Exists(serversIndexPath))
        {
            // Retry на случай блокировки файла другим тестом
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    File.Delete(serversIndexPath);
                    break;
                }
                catch (IOException) when (i < 4)
                {
                    Thread.Sleep(100);
                }
            }
        }

        _service = new ServerStorageService();
    }

    public void Dispose()
    {
        _service.Dispose();
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
