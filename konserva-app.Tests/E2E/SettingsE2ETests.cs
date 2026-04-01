using Konserva.Models;
using Konserva.Services;
using Konserva.Utilities;
using System.IO;
using System.Reflection;

namespace Konserva.Tests.E2E;

/// <summary>
/// E2E тесты: настройки приложения и конфигурация
/// </summary>
public class SettingsE2ETests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _configPath;
    private ConfigService _configService;
    private bool _disposed;

    public SettingsE2ETests()
    {
        // Создаём временную директорию для тестов
        _testDirectory = Path.Combine(Path.GetTempPath(), $"konserva_settings_e2e_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        
        _configPath = Path.Combine(_testDirectory, "config.json");
        
        // Создаём ConfigService и подменяем путь через рефлексию
        _configService = new ConfigService();
        var field = typeof(ConfigService).GetField("_configPath", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(_configService, _configPath);
        
        Logger.Info("[E2E Settings] Test initialized", "E2E");
    }

    #region Тест 1: Полный цикл настроек

    /// <summary>
    /// E2E: Изменение всех настроек → Сохранение → Чтение → Проверка
    /// </summary>
    [Fact]
    public void E2E_SettingsFullCycle_SaveAndLoad()
    {
        Logger.Info("[E2E Settings] Starting full settings cycle test", "E2E");
        
        try
        {
            // ========== ARRANGE ==========
            var javaInstallations = new List<JavaInstallation>
            {
                new JavaInstallation
                {
                    Id = "java1",
                    Path = @"C:\Java\jdk-17",
                    Version = "17.0.1",
                    MajorVersion = 17,
                    IsDefault = true
                },
                new JavaInstallation
                {
                    Id = "java2",
                    Path = @"C:\Java\jdk-21",
                    Version = "21.0.0",
                    MajorVersion = 21,
                    IsDefault = false
                }
            };
            
            // ========== ACT 1: Создаём конфигурацию ==========
            var config = new AppConfig
            {
                Theme = "Dark",
                Language = "ru",
                JavaInstallations = javaInstallations,
                DefaultJavaId = "java1",
                DefaultRamMin = 2048,
                DefaultRamMax = 8192,
                CheckUpdates = false,
                ServersDirectory = _testDirectory
            };
            
            Logger.Info("[E2E Settings] Creating config...", "E2E");
            
            // ========== ACT 2: Сохраняем конфигурацию ==========
            _configService.SaveConfig(config);
            
            Logger.Info("[E2E Settings] Config saved", "E2E");
            
            // ========== ASSERT 2: Проверяем сохранение ==========
            File.Exists(_configPath).Should().BeTrue();
            
            // ========== ACT 3: Читаем конфигурацию заново ==========
            var loadedConfig = _configService.GetConfig();
            
            Logger.Info("[E2E Settings] Config loaded", "E2E");
            
            // ========== ASSERT 3: Проверяем все поля ==========
            loadedConfig.Theme.Should().Be("Dark");
            loadedConfig.Language.Should().Be("ru");
            loadedConfig.JavaInstallations.Count.Should().Be(2);
            loadedConfig.DefaultJavaId.Should().Be("java1");
            loadedConfig.DefaultRamMin.Should().Be(2048);
            loadedConfig.DefaultRamMax.Should().Be(8192);
            loadedConfig.CheckUpdates.Should().BeFalse();
            loadedConfig.ServersDirectory.Should().Be(_testDirectory);
            
            // ========== ASSERT 4: Проверяем Java установки ==========
            var java1 = loadedConfig.JavaInstallations.First(j => j.Id == "java1");
            java1.Path.Should().Be(@"C:\Java\jdk-17");
            java1.MajorVersion.Should().Be(17);
            java1.IsDefault.Should().BeTrue();
            
            var java2 = loadedConfig.JavaInstallations.First(j => j.Id == "java2");
            java2.Path.Should().Be(@"C:\Java\jdk-21");
            java2.MajorVersion.Should().Be(21);
            java2.IsDefault.Should().BeFalse();
            
            // ========== ACT 5: Обновляем конфигурацию ==========
            Logger.Info("[E2E Settings] Updating config...", "E2E");
            
            _configService.UpdateConfig(c =>
            {
                c.Theme = "Light";
                c.DefaultRamMin = 4096;
                c.CheckUpdates = true;
            });
            
            // ========== ASSERT 5: Проверяем обновления ==========
            var updatedConfig = _configService.GetConfig();
            updatedConfig.Theme.Should().Be("Light");
            updatedConfig.DefaultRamMin.Should().Be(4096);
            updatedConfig.CheckUpdates.Should().BeTrue();
            updatedConfig.DefaultRamMax.Should().Be(8192); // Не изменилось
            
            Logger.Info("[E2E Settings] Full cycle test passed", "E2E");
        }
        catch (Exception ex)
        {
            Logger.Error($"[E2E Settings] Test failed: {ex.Message}", ex, "E2E");
            throw;
        }
    }

    #endregion

    #region Тест 2: Тема приложения

    /// <summary>
    /// E2E: Смена темы сохраняется и применяется
    /// </summary>
    [Theory]
    [InlineData("System")]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void E2E_ThemeSwitching_PersistsCorrectly(string theme)
    {
        Logger.Info($"[E2E Settings] Testing theme: {theme}", "E2E");
        
        try
        {
            // Arrange
            var config = _configService.GetConfig();
            
            // Act: Устанавливаем тему
            config.Theme = theme;
            _configService.SaveConfig(config);
            
            // Assert: Проверяем сохранение
            var loaded = _configService.GetConfig();
            loaded.Theme.Should().Be(theme);
        }
        finally
        {
            // Cleanup
            _configService.UpdateConfig(c => c.Theme = "System");
        }
    }

    #endregion

    #region Тест 3: Управление Java

    /// <summary>
    /// E2E: Добавление, изменение и удаление Java установок
    /// </summary>
    [Fact]
    public void E2E_JavaManagement_AddUpdateRemove()
    {
        Logger.Info("[E2E Settings] Testing Java management...", "E2E");
        
        try
        {
            // ========== ARRANGE ==========
            var config = _configService.GetConfig();
            var initialCount = config.JavaInstallations.Count;
            
            // ========== ACT 1: Добавляем Java ==========
            var newJava = new JavaInstallation
            {
                Id = $"test_java_{Guid.NewGuid()}",
                Name = "Test Java 17",
                Path = @"C:\TestJava\jdk-17",
                Version = "17.0.0",
                MajorVersion = 17,
                IsDefault = false
            };
            
            config.JavaInstallations.Add(newJava);
            _configService.SaveConfig(config);
            
            // ========== ASSERT 1: Java добавлена ==========
            var loaded1 = _configService.GetConfig();
            loaded1.JavaInstallations.Count.Should().Be(initialCount + 1);
            loaded1.JavaInstallations.Should().Contain(j => j.Id == newJava.Id);
            
            // ========== ACT 2: Обновляем Java ==========
            var javaToUpdate = loaded1.JavaInstallations.First(j => j.Id == newJava.Id);
            javaToUpdate.MajorVersion = 18;
            javaToUpdate.IsDefault = true;
            _configService.SaveConfig(loaded1);
            
            // ========== ASSERT 2: Java обновлена ==========
            var loaded2 = _configService.GetConfig();
            var updatedJava = loaded2.JavaInstallations.First(j => j.Id == newJava.Id);
            updatedJava.MajorVersion.Should().Be(18);
            updatedJava.IsDefault.Should().BeTrue();
            
            // ========== ACT 3: Удаляем Java ==========
            loaded2.JavaInstallations.Remove(updatedJava);
            _configService.SaveConfig(loaded2);
            
            // ========== ASSERT 3: Java удалена ==========
            var loaded3 = _configService.GetConfig();
            loaded3.JavaInstallations.Count.Should().Be(initialCount);
            loaded3.JavaInstallations.Should().NotContain(j => j.Id == newJava.Id);
            
            Logger.Info("[E2E Settings] Java management test passed", "E2E");
        }
        catch (Exception ex)
        {
            Logger.Error($"[E2E Settings] Java management test failed: {ex.Message}", ex, "E2E");
            throw;
        }
    }

    #endregion

    #region Тест 4: RAM настройки

    /// <summary>
    /// E2E: Настройки RAM сохраняются корректно
    /// </summary>
    [Theory]
    [InlineData(1024, 4096)]
    [InlineData(2048, 8192)]
    [InlineData(4096, 16384)]
    [InlineData(512, 2048)]
    public void E2E_RamSettings_SaveAndLoad(int minRam, int maxRam)
    {
        Logger.Info($"[E2E Settings] Testing RAM settings: {minRam}/{maxRam}", "E2E");
        
        try
        {
            // Act: Устанавливаем RAM
            _configService.UpdateConfig(c =>
            {
                c.DefaultRamMin = minRam;
                c.DefaultRamMax = maxRam;
            });
            
            // Assert: Проверяем сохранение
            var config = _configService.GetConfig();
            config.DefaultRamMin.Should().Be(minRam);
            config.DefaultRamMax.Should().Be(maxRam);
        }
        finally
        {
            // Cleanup
            _configService.UpdateConfig(c =>
            {
                c.DefaultRamMin = 1024;
                c.DefaultRamMax = 4096;
            });
        }
    }

    #endregion

    #region Тест 5: API Endpoints

    /// <summary>
    /// E2E: API Endpoints загружаются и сохраняются
    /// </summary>
    [Fact]
    public void E2E_ApiEndpoints_LoadAndSave()
    {
        Logger.Info("[E2E Settings] Testing API endpoints...", "E2E");
        
        try
        {
            // Act: Получаем конфигурацию
            var config = _configService.GetConfig();
            
            // Assert 1: ApiEndpoints не null
            config.ApiEndpoints.Should().NotBeNull();
            
            // Assert 2: Проверяем основные endpoints
            config.ApiEndpoints.MojangManifest.Should().NotBeNullOrEmpty();
            config.ApiEndpoints.FabricMeta.Should().NotBeNullOrEmpty();
            config.ApiEndpoints.NeoForgeMaven.Should().NotBeNullOrEmpty();
            
            // Assert 3: Проверяем, что URL валидны
            config.ApiEndpoints.MojangManifest.Should().StartWith("https://");
            config.ApiEndpoints.FabricMeta.Should().StartWith("https://");
            
            Logger.Info("[E2E Settings] API endpoints test passed", "E2E");
        }
        catch (Exception ex)
        {
            Logger.Error($"[E2E Settings] API endpoints test failed: {ex.Message}", ex, "E2E");
            throw;
        }
    }

    #endregion

    #region Тест 6: Recent Servers

    /// <summary>
    /// E2E: Список последних серверов работает
    /// </summary>
    [Fact]
    public void E2E_RecentServers_AddAndPersist()
    {
        Logger.Info("[E2E Settings] Testing recent servers...", "E2E");
        
        try
        {
            // Arrange
            var config = _configService.GetConfig();
            var initialCount = config.RecentServers.Count;
            
            // Act 1: Добавляем сервер
            config.RecentServers.Add("Test Server 1");
            _configService.SaveConfig(config);
            
            // Assert 1: Сервер добавлен
            var loaded1 = _configService.GetConfig();
            loaded1.RecentServers.Count.Should().Be(initialCount + 1);
            loaded1.RecentServers.Should().Contain("Test Server 1");
            
            // Act 2: Добавляем ещё серверы
            loaded1.RecentServers.Add("Test Server 2");
            loaded1.RecentServers.Add("Test Server 3");
            _configService.SaveConfig(loaded1);
            
            // Assert 2: Все серверы добавлены
            var loaded2 = _configService.GetConfig();
            loaded2.RecentServers.Should().Contain(new[]
            {
                "Test Server 1",
                "Test Server 2",
                "Test Server 3"
            });
            
            Logger.Info("[E2E Settings] Recent servers test passed", "E2E");
        }
        catch (Exception ex)
        {
            Logger.Error($"[E2E Settings] Recent servers test failed: {ex.Message}", ex, "E2E");
            throw;
        }
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            // Cleanup: Сбрасываем конфигурацию
            try
            {
                _configService.UpdateConfig(c =>
                {
                    c.Theme = "System";
                    c.DefaultRamMin = 1024;
                    c.DefaultRamMax = 4096;
                    c.CheckUpdates = true;
                });
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
            
            _configService?.Dispose();
            _disposed = true;
            
            Logger.Info("[E2E Settings] Test cleanup completed", "E2E");
        }
    }
}
