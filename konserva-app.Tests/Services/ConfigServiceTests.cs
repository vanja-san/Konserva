using Konserva.Models;
using Konserva.Services;
using System.Collections.ObjectModel;
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

    #region GetConfig Tests

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
        config.Language.Should().Be("System");
        config.JavaInstallations.Should().BeEmpty();
        config.DefaultRamMin.Should().Be(1024);
        config.DefaultRamMax.Should().Be(4096);
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
    public void GetConfig_ReturnsCachedConfig_OnMultipleCalls()
    {
        // Arrange
        var config1 = _configService.GetConfig();
        
        // Act
        var config2 = _configService.GetConfig();

        // Assert
        config2.Should().BeSameAs(config1);
    }

    [Fact]
    public void GetConfig_DefaultJavaPath_ReturnsJava()
    {
        // Act
        var config = _configService.GetConfig();

        // Assert
        config.DefaultJavaPath.Should().Be("java");
        config.GetDefaultJavaPath().Should().Be("java");
    }

    [Fact]
    public void GetConfig_DirectoryCreation_CreatesRequiredDirectories()
    {
        // Act
        _configService.GetConfig();

        // Assert
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Servers")).Should().BeTrue();
        Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Logs")).Should().BeTrue();
    }

    #endregion

    #region SaveConfig Tests

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
            JavaInstallations = new ObservableCollection<JavaInstallation>
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

    [Fact]
    public void SaveConfig_PreservesJavaInstallations()
    {
        // Arrange
        var config = new AppConfig
        {
            JavaInstallations = new ObservableCollection<JavaInstallation>
            {
                new JavaInstallation { Id = "java1", Name = "Java 17", Path = "C:\\Java17", Version = "17.0.1", MajorVersion = 17, IsDefault = true },
                new JavaInstallation { Id = "java2", Name = "Java 21", Path = "C:\\Java21", Version = "21.0.1", MajorVersion = 21, IsDefault = false }
            }
        };

        // Act
        _configService.SaveConfig(config);
        var loaded = _configService.GetConfig();

        // Assert
        loaded.JavaInstallations.Should().HaveCount(2);
        loaded.JavaInstallations.First(j => j.Id == "java1").IsDefault.Should().BeTrue();
        loaded.JavaInstallations.First(j => j.Id == "java2").IsDefault.Should().BeFalse();
    }

    #endregion

    #region UpdateConfig Tests

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
    public void UpdateConfig_CanAddJavaInstallation()
    {
        // Arrange
        var config = new AppConfig();
        _configService.SaveConfig(config);

        // Act
        _configService.UpdateConfig(c =>
        {
            c.JavaInstallations.Add(new JavaInstallation
            {
                Id = "java1",
                Name = "Java 17",
                Path = "C:\\Java17",
                Version = "17.0.1",
                MajorVersion = 17,
                IsDefault = true
            });
        });

        // Assert
        var updated = _configService.GetConfig();
        updated.JavaInstallations.Should().HaveCount(1);
        updated.JavaInstallations[0].Name.Should().Be("Java 17");
    }

    #endregion

    #region GetDefaultJava Tests

    [Fact]
    public void GetDefaultJava_ReturnsFirstJava_WhenNoDefaultId()
    {
        // Arrange
        var config = new AppConfig
        {
            JavaInstallations = new ObservableCollection<JavaInstallation>
            {
                new JavaInstallation { Id = "java1", Name = "Java 17", IsDefault = false },
                new JavaInstallation { Id = "java2", Name = "Java 21", IsDefault = false }
            }
        };
        _configService.SaveConfig(config);

        // Act
        var defaultJava = _configService.GetConfig().GetDefaultJava();

        // Assert
        defaultJava.Should().NotBeNull();
        defaultJava!.Id.Should().Be("java1");
    }

    [Fact]
    public void GetDefaultJava_ReturnsJavaWithDefaultId()
    {
        // Arrange
        var config = new AppConfig
        {
            DefaultJavaId = "java2",
            JavaInstallations = new ObservableCollection<JavaInstallation>
            {
                new JavaInstallation { Id = "java1", Name = "Java 17", IsDefault = false },
                new JavaInstallation { Id = "java2", Name = "Java 21", IsDefault = true }
            }
        };
        _configService.SaveConfig(config);

        // Act
        var defaultJava = _configService.GetConfig().GetDefaultJava();

        // Assert
        defaultJava.Should().NotBeNull();
        defaultJava!.Id.Should().Be("java2");
    }

    [Fact]
    public void GetDefaultJava_ReturnsNull_WhenNoJavaInstallations()
    {
        // Arrange
        var config = new AppConfig();
        _configService.SaveConfig(config);

        // Act
        var defaultJava = _configService.GetConfig().GetDefaultJava();

        // Assert
        defaultJava.Should().BeNull();
    }

    #endregion

    #region ConfigDirectory Tests

    [Fact]
    public void ConfigDirectory_ReturnsAppContextBaseDirectory()
    {
        // Act
        var configDir = AppConfig.ConfigDirectory;

        // Assert
        configDir.Should().Be(AppContext.BaseDirectory);
    }

    #endregion

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
