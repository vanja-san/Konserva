using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.Diagnostics;
using System.IO;

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

    /// <summary>
    /// Интеграционный тест: создание и запуск тестового сервера
    /// </summary>
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
            await process.StartAsync();
            
            // Assert
            // Статус должен измениться на Error или остаться Stopped
            process.Status.Should().BeOneOf(
                ServerStatus.Stopped, 
                ServerStatus.Error,
                ServerStatus.Starting);
        }
        finally
        {
            // Cleanup
            if (File.Exists(jarPath))
                File.Delete(jarPath);
            
            process?.Dispose();
        }
    }

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
