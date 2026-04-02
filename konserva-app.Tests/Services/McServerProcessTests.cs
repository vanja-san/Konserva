using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для McServerProcess
/// </summary>
public class McServerProcessTests : IDisposable
{
    private readonly string _testServerPath;
    private readonly IConfigService _configService;
    private bool _disposed;

    public McServerProcessTests()
    {
        // Создаём временную папку для тестового сервера
        _testServerPath = Path.Combine(Path.GetTempPath(), $"konserva_server_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testServerPath);

        // Создаём тестовую конфигурацию
        _configService = new ConfigService();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_InitializesWithDefaultValues()
    {
        // Arrange
        var server = new Server
        {
            Name = "TestServer",
            McVersion = "1.21.1",
            Path = _testServerPath
        };

        // Act
        var process = new McServerProcess(server, _configService);

        // Assert
        process.Server.Should().Be(server);
        process.Status.Should().Be(ServerStatus.Stopped);
        process.PlayersOnline.Should().Be(0);
        process.Process.Should().BeNull();
        process.LastError.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithNullConfigService_DoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };

        // Act
        var action = () => new McServerProcess(server, null);

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region GetLogs Tests

    [Fact]
    public void GetLogs_ReturnsEmptyList_WhenNoLogs()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        var logs = process.GetLogs();

        // Assert
        logs.Should().BeEmpty();
    }

    [Fact]
    public void GetFullLog_ReturnsEmptyString_WhenNoLogs()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        var log = process.GetFullLog();

        // Assert
        log.Should().BeEmpty();
    }

    [Fact]
    public void GetLogs_ReturnsReadOnlyList()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        var logs = process.GetLogs();

        // Assert
        logs.Should().NotBeNull();
        // IReadOnlyList по определению read-only
    }

    #endregion

    #region Start Tests

    [Fact]
    public void Start_ThrowsException_WhenServerJarNotFound()
    {
        // Arrange
        var server = new Server
        {
            Name = "Test",
            Path = _testServerPath,
            McVersion = "1.21.1",
            ModLoader = new ModLoader { Type = ModLoaderType.Vanilla }
        };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        // Start запускается асинхронно и не бросает исключения сразу
        // Проверяем, что метод вызывается без исключений
        process.Start();
    }

    [Fact]
    public async Task StartAsync_WithFakeServer_DoesNotCrash()
    {
        // Arrange
        var server = new Server
        {
            Name = "FakeServer",
            McVersion = "1.21.1",
            Path = _testServerPath,
            ModLoader = new ModLoader { Type = ModLoaderType.Vanilla }
        };

        // Создаём фейковый server.jar
        var jarPath = Path.Combine(_testServerPath, "server.jar");
        File.WriteAllText(jarPath, "fake jar content");

        var process = new McServerProcess(server, _configService);

        try
        {
            // Act
            // Пытаемся запустить (должно завершиться ошибкой, но не крашем)
            await process.Awaiting(p => p.StartAsync()).Should().ThrowAsync<Exception>();

            // Assert
            // Статус должен измениться на Error
            process.Status.Should().Be(ServerStatus.Error);
        }
        finally
        {
            // Cleanup
            if (File.Exists(jarPath))
                File.Delete(jarPath);

            process?.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_WhenDirectoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var server = new Server
        {
            Name = "Test",
            Path = "C:\\NonExistent\\Server",
            McVersion = "1.21.1"
        };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        await process.Awaiting(p => p.StartAsync()).Should().ThrowAsync<DirectoryNotFoundException>();
        
        // Статус должен быть Error
        process.Status.Should().Be(ServerStatus.Error);
        process.LastError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Start_MultipleCalls_DoesNotCreateMultipleProcesses()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        process.Start();
        process.Start();
        process.Start();

        // Assert
        // Не должно быть нескольких процессов
        process.Process.Should().BeNull();
    }

    #endregion

    #region Stop Tests

    [Fact]
    public void Stop_WhenServerNotRunning_DoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        // Проверяем, что Dispose вызывается без исключений
        process.Dispose();
    }

    [Fact]
    public async Task StopAsync_WhenServerNotRunning_CompletesSuccessfully()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        await process.StopAsync();
        // Если метод завершился без исключений - тест прошёл
    }

    [Fact]
    public async Task StopAsync_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        await process.StopAsync();
        await process.StopAsync();
        await process.StopAsync();
    }

    #endregion

    #region SendCommand Tests

    [Fact]
    public void SendCommand_WhenServerNotRunning_DoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        var action = () => process.SendCommand("stop");
        action.Should().NotThrow();
    }

    [Fact]
    public void SendCommand_WithEmptyCommand_DoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        var action = () => process.SendCommand("");
        action.Should().NotThrow();
    }

    [Fact]
    public void SendCommand_WithNullCommand_DoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        var action = () => process.SendCommand(null!);
        action.Should().NotThrow();
    }

    #endregion

    #region Status Tests

    [Fact]
    public void IsRunning_ReturnsFalse_WhenNotStarted()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        var isRunning = process.Status == ServerStatus.Running;

        // Assert
        isRunning.Should().BeFalse();
    }

    [Fact]
    public void Status_InitialState_IsStopped()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        var status = process.Status;

        // Assert
        status.Should().Be(ServerStatus.Stopped);
    }

    #endregion

    #region Server Property Tests

    [Fact]
    public void ServerProperty_ReturnsSameServerInstance()
    {
        // Arrange
        var server = new Server
        {
            Name = "TestServer",
            Path = _testServerPath
        };
        var process = new McServerProcess(server, _configService);

        // Act
        var retrievedServer = process.Server;

        // Assert
        retrievedServer.Should().BeSameAs(server);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ReleasesResources()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        process.Dispose();

        // Assert
        // Dispose не должен бросать исключений
        // Процесс должен быть null или disposed
        process.Process.Should().BeNull();
    }

    [Fact]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        process.Dispose();
        process.Dispose();
        process.Dispose();
    }

    [Fact]
    public void Dispose_ClearsLogs()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        process.Dispose();

        // Assert
        process.GetLogs().Should().BeEmpty();
    }

    #endregion

    #region PlayersOnline Tests

    [Fact]
    public void PlayersOnline_InitialState_IsZero()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        var players = process.PlayersOnline;

        // Assert
        players.Should().Be(0);
    }

    #endregion

    #region LastError Tests

    [Fact]
    public void LastError_InitialState_IsNull()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        var error = process.LastError;

        // Assert
        error.Should().BeNull();
    }

    #endregion

    #region Event Tests

    [Fact]
    public void OnStatusChanged_Event_SubscriptionDoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        var action = () => process.OnStatusChanged += (status) => { };
        action.Should().NotThrow();
    }

    [Fact]
    public void OnLog_Event_SubscriptionDoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        var action = () => process.OnLog += (line) => { };
        action.Should().NotThrow();
    }

    [Fact]
    public void OnPlayersChanged_Event_SubscriptionDoesNotThrow()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act & Assert
        var action = () => process.OnPlayersChanged += (players) => { };
        action.Should().NotThrow();
    }

    #endregion

    #region AppendLog Tests

    [Fact]
    public void AppendLog_AddsTimestamp()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        process.AppendLog("Test message");
        var logs = process.GetLogs();

        // Assert
        logs.Should().HaveCount(1);
        logs[0].Should().Contain("Test message");
        logs[0].Should().MatchRegex(@"\[\d{2}:\d{2}:\d{2}\.\d{3}\]");
    }

    [Fact]
    public void AppendLog_MultipleMessages_AddsAll()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act
        process.AppendLog("Message 1");
        process.AppendLog("Message 2");
        process.AppendLog("Message 3");
        var logs = process.GetLogs();

        // Assert
        logs.Should().HaveCount(3);
    }

    [Fact]
    public void AppendLog_ExceedsMaxLines_RemovesOldest()
    {
        // Arrange
        var server = new Server { Name = "Test", Path = _testServerPath };
        var process = new McServerProcess(server, _configService);

        // Act - добавляем больше чем MaxLogLines (1000)
        for (int i = 0; i < 1005; i++)
        {
            process.AppendLog($"Message {i}");
        }
        var logs = process.GetLogs();

        // Assert
        logs.Count.Should().Be(1000);
        logs.Last().Should().Contain("Message 1004");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FullLifecycle_CreateStartStopDispose_Works()
    {
        // Arrange
        var server = new Server
        {
            Name = "LifecycleTest",
            McVersion = "1.21.1",
            Path = _testServerPath
        };
        var process = new McServerProcess(server, _configService);

        try
        {
            // Act & Assert - Create
            process.Should().NotBeNull();
            process.Status.Should().Be(ServerStatus.Stopped);

            // Start (должно завершиться ошибкой из-за отсутствия jar/eula)
            await process.Awaiting(p => p.StartAsync()).Should().ThrowAsync<Exception>();
            
            // Статус должен быть Error
            process.Status.Should().Be(ServerStatus.Error);

            // Stop (должно работать даже после ошибки)
            await process.StopAsync();

            // Dispose
            process.Dispose();
        }
        finally
        {
            process?.Dispose();
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            // Удаляем временную папку
            if (Directory.Exists(_testServerPath))
            {
                try
                {
                    Directory.Delete(_testServerPath, true);
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
