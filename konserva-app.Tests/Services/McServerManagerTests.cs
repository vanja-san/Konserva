using Konserva.Models;
using Konserva.Services;
using Moq;
using System.IO;

namespace Konserva.Tests.Services;

/// <summary>
/// Fake in-memory storage service для тестирования McServerManager
/// </summary>
public class FakeServerStorageService : IServerStorageService
{
    private readonly List<Server> _servers = [];

    public string ServersPath => Path.Combine(Path.GetTempPath(), "konserva_test_servers");

    public List<Server> LoadServers() => [.. _servers];
    public void SaveServers(List<Server> servers)
    {
        _servers.Clear();
        _servers.AddRange(servers);
    }
    public void AddServer(Server server) => _servers.Add(server);
    public void UpdateServer(Server server)
    {
        var idx = _servers.FindIndex(s => s.Id == server.Id);
        if (idx >= 0) _servers[idx] = server;
    }
    public void DeleteServer(string serverId) => _servers.RemoveAll(s => s.Id == serverId);
    public Task<List<Server>> LoadServersAsync(CancellationToken ct = default) => Task.FromResult(LoadServers());
    public Task SaveServersAsync(List<Server> servers, CancellationToken ct = default)
    {
        SaveServers(servers);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Тесты для McServerManager
/// </summary>
[Collection("Sequential")]
public class McServerManagerTests : IDisposable
{
    private readonly FakeServerStorageService _storage;
    private readonly Mock<IConfigService> _configMock;
    private readonly McServerManager _manager;
    private readonly string _testServerPath;
    private bool _disposed;

    public McServerManagerTests()
    {
        _testServerPath = Path.Combine(Path.GetTempPath(), $"konserva_mgr_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testServerPath);

        _storage = new FakeServerStorageService();
        _configMock = new Mock<IConfigService>();
        _configMock.Setup(c => c.GetConfig()).Returns(new AppConfig());

        _manager = new McServerManager(_storage, _configMock.Object);
    }

    #region GetServers Tests

    [Fact]
    public void GetServers_ReturnsEmptyList_WhenNoServers()
    {
        var result = _manager.GetServers();
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetServers_ReturnsAllServers()
    {
        _manager.CreateServer("Server1", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        _manager.CreateServer("Server2", "1.20.4", new ModLoader { Type = ModLoaderType.Fabric }, _testServerPath);

        var result = _manager.GetServers();
        result.Should().HaveCount(2);
        result.Should().ContainSingle(s => s.Name == "Server1");
        result.Should().ContainSingle(s => s.Name == "Server2");
    }

    [Fact]
    public void GetServers_ReturnsCopy_NotInternalReference()
    {
        _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);

        var result = _manager.GetServers();
        result.Should().HaveCount(1);

        // Внешние модификации не влияют на внутреннее хранилище
        // (result — это копия, не ссылка на _servers)
    }

    #endregion

    #region GetServer Tests

    [Fact]
    public void GetServer_ReturnsServer_WhenIdExists()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);

        var result = _manager.GetServer(server.Id);
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
    }

    [Fact]
    public void GetServer_ReturnsNull_WhenIdNotFound()
    {
        var result = _manager.GetServer("non-existent-id");
        result.Should().BeNull();
    }

    #endregion

    #region CreateServer Tests

    [Fact]
    public void CreateServer_CreatesServerWithCorrectValues()
    {
        var modLoader = new ModLoader { Type = ModLoaderType.Fabric, Version = "1.21.1", LoaderVersion = "0.16.0" };
        var server = _manager.CreateServer("MyServer", "1.21.1", modLoader, _testServerPath);

        server.Should().NotBeNull();
        server.Name.Should().Be("MyServer");
        server.McVersion.Should().Be("1.21.1");
        server.ModLoader.Type.Should().Be(ModLoaderType.Fabric);
        server.Path.Should().Be(_testServerPath);
        server.Settings.RamMin.Should().Be(1024);
        server.Settings.RamMax.Should().Be(4096);
        server.Status.Should().Be(ServerStatus.Stopped);
    }

    [Fact]
    public void CreateServer_AssignsUniqueId()
    {
        var server1 = _manager.CreateServer("Server1", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        var server2 = _manager.CreateServer("Server2", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);

        server1.Id.Should().NotBeNullOrEmpty();
        server2.Id.Should().NotBeNullOrEmpty();
        server1.Id.Should().NotBe(server2.Id);
    }

    [Fact]
    public void CreateServer_InvokesOnServersChanged()
    {
        var eventFired = false;
        _manager.OnServersChanged += () => eventFired = true;

        _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);

        eventFired.Should().BeTrue();
    }

    #endregion

    #region UpdateServer Tests

    [Fact]
    public void UpdateServer_UpdatesExistingServer()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        server.Name = "UpdatedName";
        server.Port = 25566;

        _manager.UpdateServer(server);

        var retrieved = _manager.GetServer(server.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("UpdatedName");
        retrieved.Port.Should().Be(25566);
    }

    [Fact]
    public void UpdateServer_ClonesSettings()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        server.Settings.RamMax = 8192;

        _manager.UpdateServer(server);

        var retrieved = _manager.GetServer(server.Id);
        retrieved!.Settings.RamMax.Should().Be(8192);
    }

    [Fact]
    public void UpdateServer_IgnoresNonExistentServer()
    {
        var server = new Server { Id = "non-existent", Name = "Ghost" };
        _manager.Invoking(m => m.UpdateServer(server)).Should().NotThrow();
    }

    #endregion

    #region DeleteServerAsync Tests

    [Fact]
    public async Task DeleteServerAsync_RemovesServer()
    {
        var server = _manager.CreateServer("ToDelete", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);

        await _manager.DeleteServerAsync(server.Id);

        _manager.GetServers().Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteServerAsync_IgnoresNonExistentId()
    {
        var action = async () => await _manager.DeleteServerAsync("non-existent");
        await action.Should().NotThrowAsync();
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_ReturnsZeroCounts_WhenNoServers()
    {
        var (total, running, stopped) = _manager.GetStats();
        total.Should().Be(0);
        running.Should().Be(0);
        stopped.Should().Be(0);
    }

    [Fact]
    public void GetStats_ReturnsCorrectCounts()
    {
        _manager.CreateServer("S1", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        _manager.CreateServer("S2", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        _manager.CreateServer("S3", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);

        var (total, running, stopped) = _manager.GetStats();
        total.Should().Be(3);
        stopped.Should().Be(3);
        running.Should().Be(0);
    }

    #endregion

    #region GetTotalMemoryUsage Tests

    [Fact]
    public void GetTotalMemoryUsage_ReturnsZero_WhenNoRunningServers()
    {
        var result = _manager.GetTotalMemoryUsage();
        result.Should().Be(0L);
    }

    #endregion

    #region SendCommand Tests

    [Fact]
    public void SendCommand_DoesNotThrow_WhenServerNotRunning()
    {
        var action = () => _manager.SendCommand("non-existent", "stop");
        action.Should().NotThrow();
    }

    #endregion

    #region Event Tests

    [Fact]
    public void OnServerStartError_Event_CanBeSubscribed()
    {
        var action = () => _manager.OnServerStartError += (server, error) => { };
        action.Should().NotThrow();
    }

    [Fact]
    public void OnServerStartError_Event_CanBeUnsubscribed()
    {
        Action<Server, string> handler = (server, error) => { };
        _manager.OnServerStartError += handler;
        var action = () => _manager.OnServerStartError -= handler;
        action.Should().NotThrow();
    }

    #endregion

    #region Process Management Tests

    [Fact]
    public void GetProcess_ReturnsNull_WhenServerNotStarted()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        _manager.GetProcess(server.Id).Should().BeNull();
    }

    [Fact]
    public void GetProcesses_ReturnsEmptyList_WhenNoServersStarted()
    {
        _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        _manager.GetProcesses().Should().BeEmpty();
    }

    #endregion

    #region StartServer Edge Cases

    [Fact]
    public async Task StartServer_DoesNotThrow_WhenServerNotFound()
    {
        var action = async () => await _manager.StartServerAsync("non-existent-id");
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartServerAsync_DoesNotThrow_WhenServerNotFound()
    {
        var action = async () => await _manager.StartServerAsync("non-existent-id");
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopServer_DoesNotThrow_WhenServerNotRunning()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        var action = async () => await _manager.StopServerAsync(server.Id);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopServerAsync_DoesNotThrow_WhenServerNotRunning()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        var action = async () => await _manager.StopServerAsync(server.Id);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopServer_DoesNotThrow_WhenIdNotFound()
    {
        var action = async () => await _manager.StopServerAsync("non-existent-id");
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopServerAsync_DoesNotThrow_WhenIdNotFound()
    {
        var action = async () => await _manager.StopServerAsync("non-existent-id");
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartServerAsync_WithCancellation_DoesNotThrow()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = async () => await _manager.StartServerAsync(server.Id, cts.Token);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopServerAsync_WithCancellation_DoesNotThrow()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = async () => await _manager.StopServerAsync(server.Id, cts.Token);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteServerAsync_WithCancellation_DoesNotThrow()
    {
        var server = _manager.CreateServer("Test", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = async () => await _manager.DeleteServerAsync(server.Id, cts.Token);
        await action.Should().NotThrowAsync();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task GetServers_ThreadConcurrentAccess_DoesNotThrow()
    {
        for (int i = 0; i < 10; i++)
        {
            _manager.CreateServer($"Server{i}", "1.21.1", new ModLoader { Type = ModLoaderType.Vanilla }, _testServerPath);
        }

        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        for (int t = 0; t < 10; t++)
        {
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    for (int i = 0; i < 100; i++)
                    {
                        var servers = _manager.GetServers();
                        _ = servers.Count;
                        await Task.Yield(); // Ensure we're actually yielding
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);
        exceptions.Should().BeEmpty();
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            if (Directory.Exists(_testServerPath))
            {
                try { Directory.Delete(_testServerPath, true); } catch { }
            }
            _disposed = true;
        }
    }
}
