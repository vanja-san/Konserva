using Konserva.Models;
using Konserva.Services;
using System.IO;
using System.Reflection;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для ConfigService
/// </summary>
public class ConfigServiceTests : IDisposable
{
    private readonly string _testConfigPath;
    private readonly ConfigService _configService;
    private bool _disposed;

    public ConfigServiceTests()
    {
        // Создаём временный файл конфигурации
        _testConfigPath = Path.Combine(Path.GetTempPath(), $"konserva_test_{Guid.NewGuid()}.json");
        _configService = new ConfigService();
        
        // Подменяем путь к конфигу через рефлексию (для тестов)
        var field = typeof(ConfigService).GetField("_configPath", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(_configService, _testConfigPath);
    }

    [Fact]
    public void GetConfig_ReturnsDefaultConfig_WhenFileDoesNotExist()
    {
        // Arrange
        // Файл не существует
        
        // Act
        var config = _configService.GetConfig();
        
        // Assert
        config.Should().NotBeNull();
        config.Theme.Should().Be("System");
        config.Language.Should().Be("ru");
        config.JavaInstallations.Should().BeEmpty();
        config.DefaultRamMin.Should().Be(1024);
        config.DefaultRamMax.Should().Be(4096);
    }

    [Fact]
    public void SaveConfig_WritesToFile()
    {
        // Arrange
        var config = new AppConfig
        {
            Theme = "Dark",
            Language = "en",
            DefaultRamMin = 2048,
            DefaultRamMax = 8192
        };
        
        // Act
        _configService.SaveConfig(config);
        
        // Assert
        File.Exists(_testConfigPath).Should().BeTrue();
        
        var savedJson = File.ReadAllText(_testConfigPath);
        savedJson.Should().Contain("\"Dark\"");
        savedJson.Should().Contain("\"en\"");
    }

    [Fact]
    public void SaveConfig_UpdatesInternalConfig()
    {
        // Arrange
        var config = new AppConfig
        {
            Theme = "Light",
            CheckUpdates = false
        };
        
        // Act
        _configService.SaveConfig(config);
        var retrieved = _configService.GetConfig();
        
        // Assert
        retrieved.Theme.Should().Be("Light");
        retrieved.CheckUpdates.Should().BeFalse();
    }

    [Fact]
    public void UpdateConfig_AppliesChangesAndSaves()
    {
        // Arrange
        var initialConfig = new AppConfig { Theme = "System" };
        _configService.SaveConfig(initialConfig);
        
        // Act
        _configService.UpdateConfig(c =>
        {
            c.Theme = "Dark";
            c.CheckUpdates = false;
        });
        
        // Assert
        var updated = _configService.GetConfig();
        updated.Theme.Should().Be("Dark");
        updated.CheckUpdates.Should().BeFalse();
    }

    [Fact]
    public async Task SaveConfigAsync_WritesToFile()
    {
        // Arrange
        var config = new AppConfig { Theme = "Dark" };
        
        // Act
        await _configService.SaveConfigAsync(config);
        
        // Assert
        File.Exists(_testConfigPath).Should().BeTrue();
    }

    [Fact]
    public async Task GetConfigAsync_ReturnsSameAsSync()
    {
        // Arrange
        var expected = new AppConfig { Theme = "Light" };
        _configService.SaveConfig(expected);
        
        // Act
        var actual = await _configService.GetConfigAsync();
        
        // Assert
        actual.Theme.Should().Be("Light");
    }

    [Fact]
    public void GetConfig_CreatesDefaultConfigFile_IfNotExists()
    {
        // Arrange
        // Файл не существует
        
        // Act
        _configService.GetConfig();
        
        // Assert
        File.Exists(_testConfigPath).Should().BeTrue();
    }

    [Fact]
    public void SaveConfig_SerializesAllProperties()
    {
        // Arrange
        var config = new AppConfig
        {
            Theme = "Dark",
            Language = "ru",
            CheckUpdates = true,
            DefaultRamMin = 2048,
            DefaultRamMax = 8192,
            ServersDirectory = "C:\\Servers",
            JavaInstallations = new List<JavaInstallation>
            {
                new JavaInstallation { Id = "java1", Path = "C:\\Java17", IsDefault = true }
            },
            DefaultJavaId = "java1"
        };
        
        // Act
        _configService.SaveConfig(config);
        
        // Assert
        var json = File.ReadAllText(_testConfigPath);
        json.Should().Contain("Dark");
        json.Should().Contain("ru");
        json.Should().Contain("Servers");  // Проверяем без слэшей
        json.Should().Contain("java1");
        json.Should().Contain("Java17");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Удаляем временный файл
            if (File.Exists(_testConfigPath))
            {
                try
                {
                    File.Delete(_testConfigPath);
                }
                catch
                {
                    // Игнорируем ошибки удаления
                }
            }
            
            _configService?.Dispose();
            _disposed = true;
        }
    }
}
