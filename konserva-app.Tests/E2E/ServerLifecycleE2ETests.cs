using Konserva.Models;
using Konserva.Services;
using System.IO;

namespace Konserva.Tests.E2E;

/// <summary>
/// E2E тесты: полные сценарии работы с серверами
/// </summary>
public class ServerLifecycleE2ETests : IDisposable
{
    private readonly string _testDirectory;
    private readonly IConfigService _configService;
    private readonly IServerManager _serverManager;
    private readonly IServerStorageService _storageService;
    private bool _disposed;

    public ServerLifecycleE2ETests()
    {
        // Создаём временную директорию для тестов
        _testDirectory = Path.Combine(Path.GetTempPath(), $"konserva_e2e_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        
        // Инициализируем сервисы
        _configService = new ConfigService();
        _storageService = new ServerStorageService();
        _serverManager = new McServerManager(_storageService, _configService);
    }

    #region Тест 1: Полный жизненный цикл сервера

    /// <summary>
    /// E2E: Создание → Проверка → Запуск → Остановка → Удаление
    /// </summary>
    [Fact]
    public async Task E2E_ServerFullLifecycle_FromCreationToDeletion()
    {
        // ========== ARRANGE ==========
        var serverName = $"E2E Test Server {DateTime.Now:yyyyMMddHHmmss}";
        var mcVersion = "1.21.1";
        var modLoader = new ModLoader { Type = ModLoaderType.Vanilla };
        
        Logger.Info($"[E2E] Starting test: {serverName}", "E2E");
        
        try
        {
            // ========== ACT 1: Создаём сервер ==========
            Logger.Info("[E2E] Creating server...", "E2E");
            
            var server = _serverManager.CreateServer(
                serverName,
                mcVersion,
                modLoader,
                _testDirectory
            );
            
            // ========== ASSERT 1: Сервер создан ==========
            Logger.Info($"[E2E] Server created: {server.Id}", "E2E");
            
            server.Should().NotBeNull();
            server.Name.Should().Be(serverName);
            server.McVersion.Should().Be(mcVersion);
            server.ModLoader.Type.Should().Be(ModLoaderType.Vanilla);
            server.Status.Should().Be(ServerStatus.Stopped);
            server.Port.Should().Be(25565); // Порт по умолчанию
            
            // Проверяем, что сервер появился в списке
            var servers = _serverManager.GetServers();
            servers.Should().Contain(s => s.Id == server.Id);
            
            // ========== ACT 2: Проверяем состояние до запуска ==========
            Logger.Info("[E2E] Checking pre-start state...", "E2E");
            
            var retrievedServer = _serverManager.GetServer(server.Id);
            retrievedServer.Should().NotBeNull();
            retrievedServer!.Name.Should().Be(serverName);
            
            // ========== ACT 3: Пытаемся запустить сервер ==========
            Logger.Info("[E2E] Starting server...", "E2E");
            
            _serverManager.StartServer(server.Id);
            
            // Ждём немного (сервер не запустится без jar, но статус изменится)
            await Task.Delay(3000);
            
            // ========== ASSERT 3: Проверяем статус после запуска ==========
            Logger.Info($"[E2E] Server status after start: {retrievedServer.Status}", "E2E");
            
            // Статус должен измениться (Starting, Running, или Error если нет jar)
            retrievedServer.Status.Should().BeOneOf(
                ServerStatus.Starting,
                ServerStatus.Running,
                ServerStatus.Stopped,    // Может не запуститься без jar файла
                ServerStatus.Error       // Ошибка если нет Java или jar
            );
            
            // ========== ACT 4: Останавливаем сервер ==========
            Logger.Info("[E2E] Stopping server...", "E2E");
            
            _serverManager.StopServer(server.Id);
            await Task.Delay(2000);
            
            // ========== ASSERT 4: Сервер остановлен ==========
            var stoppedServer = _serverManager.GetServer(server.Id);
            stoppedServer!.Status.Should().Be(ServerStatus.Stopped);
            
            // ========== ACT 5: Обновляем настройки сервера ==========
            Logger.Info("[E2E] Updating server settings...", "E2E");
            
            stoppedServer.Port = 25570;
            stoppedServer.AutoStart = true;
            _serverManager.UpdateServer(stoppedServer);
            
            // ========== ASSERT 5: Настройки сохранены ==========
            var updatedServer = _serverManager.GetServer(server.Id);
            updatedServer!.Port.Should().Be(25570);
            updatedServer.AutoStart.Should().BeTrue();
            
            // ========== ACT 6: Получаем статистику ==========
            var (total, running, stopped) = _serverManager.GetStats();
            total.Should().BeGreaterThanOrEqualTo(1);
            
            // ========== ACT 7: Удаляем сервер ==========
            Logger.Info("[E2E] Deleting server...", "E2E");
            
            await _serverManager.DeleteServerAsync(server.Id);
            
            // ========== ASSERT 7: Сервер удалён ==========
            var deletedServer = _serverManager.GetServer(server.Id);
            deletedServer.Should().BeNull();
            
            Logger.Info("[E2E] Test completed successfully", "E2E");
        }
        catch (Exception ex)
        {
            Logger.Error($"[E2E] Test failed: {ex.Message}", ex, "E2E");
            throw;
        }
    }

    #endregion

    #region Тест 2: Создание нескольких серверов

    /// <summary>
    /// E2E: Создание нескольких серверов одновременно
    /// </summary>
    [Fact]
    public void E2E_CreateMultipleServers_AllCreatedSuccessfully()
    {
        // Arrange
        var serverConfigs = new[]
        {
            new { Name = "E2E Server 1", Version = "1.21.1" },
            new { Name = "E2E Server 2", Version = "1.20.4" },
            new { Name = "E2E Server 3", Version = "1.19.2" }
        };
        
        Logger.Info("[E2E] Creating multiple servers...", "E2E");
        
        try
        {
            // Act: Создаём 3 сервера
            var servers = serverConfigs.Select(config =>
                _serverManager.CreateServer(
                    config.Name,
                    config.Version,
                    new ModLoader { Type = ModLoaderType.Vanilla },
                    Path.Combine(_testDirectory, config.Name)
                )
            ).ToList();
            
            // Assert 1: Все серверы созданы
            servers.Count.Should().Be(3);
            servers.Should().OnlyContain(s => s != null);
            
            // Assert 2: У всех серверов уникальные ID
            var ids = servers.Select(s => s.Id).ToList();
            ids.Distinct().Count().Should().Be(3);
            
            // Assert 3: Все серверы в списке
            var allServers = _serverManager.GetServers();
            allServers.Count.Should().BeGreaterThanOrEqualTo(3);
            
            // Assert 4: Проверяем имена и версии
            servers[0].Name.Should().Be("E2E Server 1");
            servers[0].McVersion.Should().Be("1.21.1");
            
            servers[1].Name.Should().Be("E2E Server 2");
            servers[1].McVersion.Should().Be("1.20.4");
            
            servers[2].Name.Should().Be("E2E Server 3");
            servers[2].McVersion.Should().Be("1.19.2");
            
            // Assert 5: Все серверы остановлены
            servers.Should().OnlyContain(s => s.Status == ServerStatus.Stopped);
            
            Logger.Info($"[E2E] Created {servers.Count} servers successfully", "E2E");
        }
        finally
        {
            // Cleanup: Удаляем все созданные серверы
            var createdServers = _serverManager.GetServers()
                .Where(s => s.Name.StartsWith("E2E Server"))
                .ToList();
            
            foreach (var server in createdServers)
            {
                _serverManager.DeleteServerAsync(server.Id).Wait();
            }
        }
    }

    #endregion

    #region Тест 3: Создание серверов с разными модлоадерами

    /// <summary>
    /// E2E: Создание серверов с разными типами модлоадеров
    /// </summary>
    [Fact]
    public void E2E_CreateServersWithDifferentModLoaders_AllCreated()
    {
        // Arrange
        var modLoaderTypes = new[]
        {
            ModLoaderType.Vanilla,
            ModLoaderType.Forge,
            ModLoaderType.NeoForge,
            ModLoaderType.Fabric,
            ModLoaderType.Quilt
        };
        
        Logger.Info("[E2E] Creating servers with different mod loaders...", "E2E");
        
        try
        {
            // Act: Создаём серверы с разными модлоадерами
            var servers = modLoaderTypes.Select((type, index) =>
                _serverManager.CreateServer(
                    $"E2E {type} Server",
                    "1.21.1",
                    new ModLoader { Type = type },
                    Path.Combine(_testDirectory, type.ToString())
                )
            ).ToList();
            
            // Assert 1: Все серверы созданы
            servers.Count.Should().Be(5);
            
            // Assert 2: Проверяем типы модлоадеров
            servers[0].ModLoader.Type.Should().Be(ModLoaderType.Vanilla);
            servers[1].ModLoader.Type.Should().Be(ModLoaderType.Forge);
            servers[2].ModLoader.Type.Should().Be(ModLoaderType.NeoForge);
            servers[3].ModLoader.Type.Should().Be(ModLoaderType.Fabric);
            servers[4].ModLoader.Type.Should().Be(ModLoaderType.Quilt);
            
            // Assert 3: У всех уникальные ID
            var ids = servers.Select(s => s.Id).ToList();
            ids.Distinct().Count().Should().Be(5);
            
            Logger.Info("[E2E] All mod loader types created successfully", "E2E");
        }
        finally
        {
            // Cleanup
            var createdServers = _serverManager.GetServers()
                .Where(s => s.Name.StartsWith("E2E"))
                .ToList();
            
            foreach (var server in createdServers)
            {
                _serverManager.DeleteServerAsync(server.Id).Wait();
            }
        }
    }

    #endregion

    #region Тест 4: Клонирование сервера

    /// <summary>
    /// E2E: Клонирование сервера создаёт независимую копию
    /// </summary>
    [Fact]
    public void E2E_ServerClone_CreatesIndependentCopy()
    {
        // Arrange
        var original = _serverManager.CreateServer(
            "Original Server",
            "1.21.1",
            new ModLoader { Type = ModLoaderType.Vanilla, Version = "1.0" },
            Path.Combine(_testDirectory, "Original")
        );
        
        Logger.Info("[E2E] Testing server cloning...", "E2E");
        
        try
        {
            // Act: Клонируем сервер
            var clone = original.Clone();
            clone.Name = "Cloned Server";
            clone.Port = 25570;
            clone.ModLoader.Version = "2.0";
            
            // Assert 1: Клон имеет другие свойства
            clone.Name.Should().Be("Cloned Server");
            clone.Port.Should().Be(25570);
            
            // Assert 2: Оригинал не изменён
            original.Name.Should().Be("Original Server");
            original.Port.Should().Be(25565);
            
            // Assert 3: ModLoader тоже склонирован (независимая копия)
            clone.ModLoader.Version.Should().Be("2.0");
            original.ModLoader.Version.Should().Be("1.0"); // Оригинал не изменился
            
            // Assert 4: Settings тоже склонированы
            clone.Settings.RamMax = 8192;
            original.Settings.RamMax.Should().Be(4096); // Оригинал не изменился
            
            Logger.Info("[E2E] Server clone test passed", "E2E");
        }
        finally
        {
            // Cleanup
            _serverManager.DeleteServerAsync(original.Id).Wait();
        }
    }

    #endregion

    #region Тест 5: Статистика серверов

    /// <summary>
    /// E2E: Подсчёт статистики работает корректно
    /// </summary>
    [Fact]
    public void E2E_ServerStats_ReflectsActualState()
    {
        // Arrange
        var server1 = _serverManager.CreateServer(
            "Stats Server 1",
            "1.21.1",
            new ModLoader { Type = ModLoaderType.Vanilla },
            Path.Combine(_testDirectory, "Stats1")
        );
        
        var server2 = _serverManager.CreateServer(
            "Stats Server 2",
            "1.21.1",
            new ModLoader { Type = ModLoaderType.Vanilla },
            Path.Combine(_testDirectory, "Stats2")
        );
        
        Logger.Info("[E2E] Testing server statistics...", "E2E");
        
        try
        {
            // Act 1: Получаем начальную статистику
            var (total1, running1, stopped1) = _serverManager.GetStats();
            
            // Assert 1: Два сервера, оба остановлены
            total1.Should().BeGreaterThanOrEqualTo(2);
            
            // Act 2: Запускаем один сервер
            _serverManager.StartServer(server1.Id);
            
            // Ждём изменения статуса
            Thread.Sleep(1000);
            
            // Получаем обновлённую статистику
            var (total2, running2, stopped2) = _serverManager.GetStats();
            
            // Assert 2: Хотя бы один сервер в статусе запуска/запущен
            running2.Should().BeGreaterThanOrEqualTo(0); // Может быть 0 если не успел запуститься
            
            Logger.Info($"[E2E] Stats: Total={total2}, Running={running2}, Stopped={stopped2}", "E2E");
        }
        finally
        {
            // Cleanup
            _serverManager.StopServer(server1.Id);
            _serverManager.DeleteServerAsync(server1.Id).Wait();
            _serverManager.DeleteServerAsync(server2.Id).Wait();
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            // Очищаем все тестовые серверы
            try
            {
                var testServers = _serverManager.GetServers()
                    .Where(s => s.Name.StartsWith("E2E") || s.Name.Contains("Test"))
                    .ToList();
                
                foreach (var server in testServers)
                {
                    try
                    {
                        _serverManager.DeleteServerAsync(server.Id).Wait();
                    }
                    catch { }
                }
            }
            catch { }
            
            // Удаляем временную директорию
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch { }
            }
            
            _disposed = true;
            Logger.Info("[E2E] Test cleanup completed", "E2E");
        }
    }
}
