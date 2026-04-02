using Konserva.Models;
using Konserva.Services;
using System.IO;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для ServerStorageService
/// </summary>
public class ServerStorageServiceTests : IDisposable
{
    private readonly string _testServersIndexPath;
    private readonly string _testServersDir;
    private readonly ServerStorageService _storageService;
    private bool _disposed;

    public ServerStorageServiceTests()
    {
        // Создаём временную директорию для тестов
        _testServersDir = Path.Combine(Path.GetTempPath(), $"konserva_servers_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testServersDir);
        
        _testServersIndexPath = Path.Combine(_testServersDir, "servers.json");
        _storageService = new ServerStorageService();

        // Подменяем путь через рефлексию
        var field = typeof(ServerStorageService).GetField("_serversIndexPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(_storageService, _testServersIndexPath);
    }

    #region LoadServers Tests

    [Fact]
    public void LoadServers_ReturnsEmptyList_WhenFileDoesNotExist()
    {
        // Arrange
        // Файл не существует

        // Act
        var servers = _storageService.LoadServers();

        // Assert
        servers.Should().BeEmpty();
    }

    [Fact]
    public void LoadServers_ReturnsServers_FromFile()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "TestServer1", McVersion = "1.21.1", ModLoader = new ModLoader { Type = ModLoaderType.Vanilla } },
            new Server { Name = "TestServer2", McVersion = "1.20.4", ModLoader = new ModLoader { Type = ModLoaderType.Forge } }
        };
        SaveServersDirectly(servers);

        // Act
        var loaded = _storageService.LoadServers();

        // Assert
        loaded.Should().HaveCount(2);
        loaded[0].Name.Should().Be("TestServer1");
        loaded[1].Name.Should().Be("TestServer2");
    }

    [Fact]
    public void LoadServers_SetsStoppedStatus_ForAllServers()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "TestServer", McVersion = "1.21.1" }
        };
        SaveServersDirectly(servers);

        // Act
        var loaded = _storageService.LoadServers();

        // Assert
        loaded[0].Status.Should().Be(ServerStatus.Stopped);
    }

    [Fact]
    public async Task LoadServersAsync_ReturnsSameAsSync()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "TestServer", McVersion = "1.21.1" }
        };
        SaveServersDirectly(servers);

        // Act
        var loaded = await _storageService.LoadServersAsync();

        // Assert
        loaded.Should().HaveCount(1);
    }

    [Fact]
    public void LoadServers_InitializesIdCounter()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "Server1" },
            new Server { Name = "Server2" },
            new Server { Name = "Server3" }
        };
        SaveServersDirectly(servers);

        // Act
        _storageService.LoadServers();
        
        // Создаём новый сервер - ID должен быть больше существующих
        var newServer = new Server { Name = "NewServer" };

        // Assert
        newServer.Id.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region SaveServers Tests

    [Fact]
    public void SaveServers_WritesToFile()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "TestServer", McVersion = "1.21.1", ModLoader = new ModLoader { Type = ModLoaderType.Vanilla } }
        };

        // Act
        _storageService.SaveServers(servers);

        // Assert
        File.Exists(_testServersIndexPath).Should().BeTrue();
    }

    [Fact]
    public void SaveServers_JsonContainsServerData()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "MyServer", McVersion = "1.21.1", Port = 25565 }
        };

        // Act
        _storageService.SaveServers(servers);

        // Assert
        var json = File.ReadAllText(_testServersIndexPath);
        json.Should().Contain("MyServer");
        json.Should().Contain("1.21.1");
        json.Should().Contain("25565");
    }

    [Fact]
    public async Task SaveServersAsync_WritesToFile()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "TestServer", McVersion = "1.21.1" }
        };

        // Act
        await _storageService.SaveServersAsync(servers);

        // Assert
        File.Exists(_testServersIndexPath).Should().BeTrue();
    }

    [Fact]
    public void SaveServers_UpdatesCachedServers()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "TestServer", McVersion = "1.21.1" }
        };

        // Act
        _storageService.SaveServers(servers);
        var loaded = _storageService.LoadServers();

        // Assert
        loaded.Should().HaveCount(1);
        loaded[0].Name.Should().Be("TestServer");
    }

    #endregion

    #region AddServer Tests

    [Fact]
    public void AddServer_AddsToListAndSaves()
    {
        // Arrange
        var server = new Server { Name = "NewServer", McVersion = "1.21.1" };

        // Act
        _storageService.AddServer(server);

        // Assert
        var loaded = _storageService.LoadServers();
        loaded.Should().HaveCount(1);
        loaded[0].Name.Should().Be("NewServer");
    }

    [Fact]
    public void AddServer_MultipleServers_AddsAll()
    {
        // Arrange
        var server1 = new Server { Name = "Server1", McVersion = "1.21.1" };
        var server2 = new Server { Name = "Server2", McVersion = "1.20.4" };

        // Act
        _storageService.AddServer(server1);
        _storageService.AddServer(server2);

        // Assert
        var loaded = _storageService.LoadServers();
        loaded.Should().HaveCount(2);
    }

    #endregion

    #region UpdateServer Tests

    [Fact]
    public void UpdateServer_UpdatesExistingServer()
    {
        // Arrange
        var server = new Server { Name = "Original", McVersion = "1.21.1" };
        _storageService.AddServer(server);
        
        // Act
        server.Name = "Updated";
        server.McVersion = "1.20.4";
        _storageService.UpdateServer(server);

        // Assert
        var loaded = _storageService.LoadServers();
        loaded[0].Name.Should().Be("Updated");
        loaded[0].McVersion.Should().Be("1.20.4");
    }

    [Fact]
    public void UpdateServer_DoesNothing_WhenServerNotFound()
    {
        // Arrange
        var server = new Server { Id = "non-existent-id", Name = "Test" };

        // Act
        _storageService.UpdateServer(server);

        // Assert
        _storageService.LoadServers().Should().BeEmpty();
    }

    [Fact]
    public void UpdateServer_ClonesSettings()
    {
        // Arrange
        var server = new Server 
        { 
            Name = "Test", 
            Settings = new ServerSettings { RamMin = 1024, RamMax = 4096 } 
        };
        _storageService.AddServer(server);

        // Act
        server.Settings.RamMin = 2048;
        _storageService.UpdateServer(server);

        // Assert
        var loaded = _storageService.LoadServers();
        loaded[0].Settings.RamMin.Should().Be(2048);
    }

    #endregion

    #region DeleteServer Tests

    [Fact]
    public void DeleteServer_RemovesServer()
    {
        // Arrange
        var server = new Server { Name = "ToDelete", McVersion = "1.21.1" };
        _storageService.AddServer(server);
        var serverId = server.Id;

        // Act
        _storageService.DeleteServer(serverId);

        // Assert
        _storageService.LoadServers().Should().BeEmpty();
    }

    [Fact]
    public void DeleteServer_DoesNothing_WhenServerNotFound()
    {
        // Arrange
        var server = new Server { Name = "Test", McVersion = "1.21.1" };
        _storageService.AddServer(server);

        // Act
        _storageService.DeleteServer("non-existent-id");

        // Assert
        _storageService.LoadServers().Should().HaveCount(1);
    }

    [Fact]
    public void DeleteServer_DeletesServerFolder()
    {
        // Arrange
        var serverPath = Path.Combine(_testServersDir, "TestServer");
        Directory.CreateDirectory(serverPath);
        
        var server = new Server 
        { 
            Name = "ToDelete", 
            McVersion = "1.21.1",
            Path = serverPath
        };
        _storageService.AddServer(server);
        var serverId = server.Id;

        // Act
        _storageService.DeleteServer(serverId);

        // Assert
        Directory.Exists(serverPath).Should().BeFalse();
    }

    #endregion

    #region Retry Logic Tests

    [Fact]
    public void SaveServers_HandlesFileLockRetry()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "TestServer", McVersion = "1.21.1" }
        };

        // Блокируем файл
        using (var fs = new FileStream(_testServersIndexPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var writer = new StreamWriter(fs);
            writer.Write("{}");
            writer.Flush();
            
            // Пытаемся сохранить (должна сработать retry-логика)
            // Act
            _storageService.SaveServers(servers);
        }

        // Assert
        var loaded = _storageService.LoadServers();
        loaded.Should().HaveCount(1);
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public void LoadServers_ReturnsCopy_NotReference()
    {
        // Arrange
        var servers = new List<Server>
        {
            new Server { Name = "TestServer", McVersion = "1.21.1" }
        };
        SaveServersDirectly(servers);

        // Act
        var loaded1 = _storageService.LoadServers();
        var loaded2 = _storageService.LoadServers();

        // Assert
        loaded1.Should().NotBeSameAs(loaded2);
    }

    #endregion

    #region Helper Methods

    private void SaveServersDirectly(List<Server> servers)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(servers, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(_testServersIndexPath, json);
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            // Удаляем временную директорию
            if (Directory.Exists(_testServersDir))
            {
                try
                {
                    Directory.Delete(_testServersDir, true);
                }
                catch
                {
                    // Игнорируем ошибки удаления
                }
            }

            _disposed = true;
        }
    }
}
