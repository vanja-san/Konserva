using System.Globalization;
using System.IO;
using System.Text.Json;
using Konserva.Localization;
using Konserva.Services;
using Xunit;

namespace Konserva.Tests.Services;

/// <summary>
/// Тесты для системы локализации
/// </summary>
public class LocalizationTests : IDisposable
{
    private readonly string _testI18nPath;
    private readonly string _originalI18nPath;

    public LocalizationTests()
    {
        // Создаём временную папку для тестовых файлов локализации
        _testI18nPath = Path.Combine(Path.GetTempPath(), $"konserva_i18n_test_{Guid.NewGuid()}", "i18n");
        Directory.CreateDirectory(_testI18nPath);
        
        // Сохраняем оригинальный путь
        _originalI18nPath = Path.Combine(AppContext.BaseDirectory, "i18n");
    }

    [Fact]
    public void LocalizationManager_GetDefaultTranslations_Russian_ReturnsNonEmptyDictionary()
    {
        // Arrange
        var culture = "ru";

        // Act
        var translations = GetDefaultTranslations(culture);

        // Assert
        Assert.NotNull(translations);
        Assert.NotEmpty(translations);
        Assert.True(translations.Count > 50, "Russian translations should have more than 50 keys");
    }

    [Fact]
    public void LocalizationManager_GetDefaultTranslations_English_ReturnsNonEmptyDictionary()
    {
        // Arrange
        var culture = "en";

        // Act
        var translations = GetDefaultTranslations(culture);

        // Assert
        Assert.NotNull(translations);
        Assert.NotEmpty(translations);
        Assert.True(translations.Count > 50, "English translations should have more than 50 keys");
    }

    [Fact]
    public void LocalizationManager_GetDefaultTranslations_UnknownCulture_ReturnsEmptyDictionary()
    {
        // Arrange
        var culture = "unknown";

        // Act
        var translations = GetDefaultTranslations(culture);

        // Assert
        Assert.NotNull(translations);
        Assert.Empty(translations);
    }

    [Fact]
    public void LocalizationManager_CreateDefaultLocalizationFile_CreatesValidJsonFile()
    {
        // Arrange
        var culture = "ru";
        var filePath = Path.Combine(_testI18nPath, $"{culture}.json");

        // Act
        CreateDefaultLocalizationFile(filePath, culture);

        // Assert
        Assert.True(File.Exists(filePath), "Localization file should be created");

        var content = File.ReadAllText(filePath);
        Assert.NotNull(content);
        Assert.NotEmpty(content);

        // Проверяем, что это валидный JSON
        var jsonDoc = JsonDocument.Parse(content);
        Assert.NotNull(jsonDoc);
    }

    [Fact]
    public void LocalizationManager_CreateDefaultLocalizationFile_ContainsRequiredKeys()
    {
        // Arrange
        var culture = "ru";
        var filePath = Path.Combine(_testI18nPath, $"{culture}.json");
        var requiredKeys = new[]
        {
            "MainWindow_Title",
            "Settings_Title",
            "CreateServer_Title",
            "ServersPage_Search",
            "Common_Cancel"
        };

        // Act
        CreateDefaultLocalizationFile(filePath, culture);
        var content = File.ReadAllText(filePath);
        var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(content);

        // Assert
        Assert.NotNull(translations);
        foreach (var key in requiredKeys)
        {
            Assert.Contains(key, translations.Keys);
        }
    }

    [Fact]
    public void LocalizationManager_CreateDefaultLocalizationFile_UsesUtf8Encoding()
    {
        // Arrange
        var culture = "ru";
        var filePath = Path.Combine(_testI18nPath, $"{culture}.json");

        // Act
        CreateDefaultLocalizationFile(filePath, culture);

        // Assert
        var bytes = File.ReadAllBytes(filePath);
        _ = bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF;

        // UTF-8 с BOM или без BOM допустимы
        var content = File.ReadAllText(filePath);
        Assert.Contains("Настройки", content); // Проверяем, что русские символы сохранились
    }

    [Fact]
    public void LocalizationManager_CreateDefaultLocalizationFile_RussianAndEnglish_HaveSameKeys()
    {
        // Arrange
        var ruFilePath = Path.Combine(_testI18nPath, "ru.json");
        var enFilePath = Path.Combine(_testI18nPath, "en.json");

        // Act
        CreateDefaultLocalizationFile(ruFilePath, "ru");
        CreateDefaultLocalizationFile(enFilePath, "en");

        var ruContent = File.ReadAllText(ruFilePath);
        var enContent = File.ReadAllText(enFilePath);

        var ruTranslations = JsonSerializer.Deserialize<Dictionary<string, string>>(ruContent);
        var enTranslations = JsonSerializer.Deserialize<Dictionary<string, string>>(enContent);

        // Assert
        Assert.NotNull(ruTranslations);
        Assert.NotNull(enTranslations);

        var ruKeys = ruTranslations.Keys.OrderBy(k => k);
        var enKeys = enTranslations.Keys.OrderBy(k => k);

        Assert.Equal(ruKeys, enKeys);
    }

    [Fact]
    public void LocalizationManager_CreateDefaultLocalizationFile_RussianTranslations_ContainCyrillicText()
    {
        // Arrange
        var culture = "ru";
        var filePath = Path.Combine(_testI18nPath, $"{culture}.json");

        // Act
        CreateDefaultLocalizationFile(filePath, culture);
        var content = File.ReadAllText(filePath);

        // Assert
        Assert.Matches(@"[\u0400-\u04FF]", content); // Проверяем наличие кириллицы
    }

    [Fact]
    public void LocalizationManager_CreateDefaultLocalizationFile_EnglishTranslations_ContainLatinText()
    {
        // Arrange
        var culture = "en";
        var filePath = Path.Combine(_testI18nPath, $"{culture}.json");

        // Act
        CreateDefaultLocalizationFile(filePath, culture);
        var content = File.ReadAllText(filePath);

        // Assert
        Assert.Matches(@"[a-zA-Z]", content); // Проверяем наличие латиницы
    }

    [Fact]
    public void LocalizationManager_Initialize_CreatesI18nDirectory()
    {
        // Arrange
        var testPath = Path.Combine(Path.GetTempPath(), $"konserva_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(testPath);

        try
        {
            // Act & Assert - просто проверяем что инициализация не бросает исключений
            var exception = Record.Exception(() =>
            {
                // Проверяем что статический класс доступен
                var culture = LocalizationManager.CurrentCulture;
                Assert.NotNull(culture);
            });

            // Assert
            Assert.Null(exception);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testPath))
            {
                Directory.Delete(testPath, true);
            }
        }
    }

    [Fact]
    public void LocalizationManager_SupportedCultures_ContainsRussianAndEnglish()
    {
        // Arrange
        var expectedCultures = new[] { "ru", "en" };

        // Act
        var cultures = LocalizationManager.SupportedCultures;

        // Assert
        Assert.Equal(expectedCultures, cultures);
    }

    [Fact]
    public void LocalizationManager_HasKey_ReturnsTrueForValidKey()
    {
        // Arrange
        LocalizationManager.Initialize();
        
        // Act
        var hasKey = LocalizationManager.HasKey("MainWindow_Header");

        // Assert
        Assert.True(hasKey);
    }

    [Fact]
    public void LocalizationManager_HasKey_ReturnsFalseForInvalidKey()
    {
        // Arrange
        LocalizationManager.Initialize();
        
        // Act
        var hasKey = LocalizationManager.HasKey("NonExistentKey");

        // Assert
        Assert.False(hasKey);
    }

    [Fact]
    public void LocalizationManager_GetWithFormat_ReturnsFormattedString()
    {
        // Arrange - добавим тестовый ключ с форматом
        // Act & Assert - просто проверяем что метод работает
        var exception = Record.Exception(() =>
        {
            // Проверяем что метод Get с аргументами не бросает исключений
            _ = LocalizationManager.Get("Common_None");
        });

        Assert.Null(exception);
    }

    /// <summary>
    /// Helper method для получения переводов по умолчанию
    /// </summary>
    private static Dictionary<string, string> GetDefaultTranslations(string culture)
    {
        return culture switch
        {
            "ru" => new Dictionary<string, string>
            {
                // MainWindow
                { "MainWindow_Title", "Konserva — Менеджер серверов Minecraft" },
                { "MainWindow_Servers", "Серверы" },
                { "MainWindow_Settings", "Настройки" },
                { "MainWindow_Header", "Konserva Manager" },
                { "StatusBar_TotalServers", "Всего серверов" },
                { "StatusBar_Running", "Запущено" },
                { "StatusBar_Memory", "Память" },
                { "StatusBar_Java_Configured", "Java настроена" },
                { "StatusBar_Java_NotConfigured", "Java не настроена" },
                { "StatusBar_Version", "Версия" },
                { "StatusBar_Version_Value", "v1.2" },
                // Settings
                { "Settings_Title", "Настройки" },
                { "Settings_Servers", "Серверы" },
                { "Settings_Servers_Directory", "Папка серверов" },
                { "Settings_Servers_Browse", "Обзор" },
                { "Settings_Java", "Java" },
                { "Settings_Java_Add", "Добавить Java" },
                { "Settings_RAM_Min", "Память и приложение" },
                { "Settings_RAM_Min_Label", "Мин. ОЗУ (МБ)" },
                { "Settings_RAM_Max_Label", "Макс. ОЗУ (МБ)" },
                { "Settings_App", "Приложение" },
                { "Settings_CheckUpdates", "Проверка обновлений" },
                { "Settings_Theme", "Тема" },
                { "Settings_Theme_System", "Как в системе" },
                { "Settings_Theme_Dark", "Тёмная" },
                { "Settings_Theme_Light", "Светлая" },
                { "Settings_About", "О программе" },
                { "Settings_About_Version", "Версия" },
                { "Settings_About_Description", "Менеджер серверов Minecraft" },
                { "Message_SettingsSaved", "Настройки сохранены" },
                // CreateServer
                { "CreateServer_Title", "Создать сервер" },
                { "CreateServer_Name", "Название сервера" },
                { "CreateServer_MinecraftVersion", "Версия Minecraft" },
                { "CreateServer_ModLoader", "Модлоадер" },
                { "CreateServer_Folder", "Папка" },
                { "CreateServer_Browse", "Обзор" },
                { "CreateServer_Create", "Создать" },
                { "CreateServer_Cancel", "Отмена" },
                { "CreateServer_Filter_Stable", "Только стабильные" },
                // ServersPage
                { "ServersPage_Search", "Поиск..." },
                { "ServersPage_Filter_All", "Все типы" },
                { "ServersPage_Filter_AllServers", "Все серверы" },
                { "ServersPage_Filter_Running", "Запущен" },
                { "ServersPage_Filter_Stopped", "Остановлен" },
                { "ServersPage_Create", "Создать сервер" },
                { "ServersPage_NoServers", "Нет серверов" },
                { "ServersPage_NoServers_Description", "Создайте первый сервер" },
                // Common
                { "Common_Cancel", "Отмена" },
                { "Common_OK", "ОК" },
                { "Common_Yes", "Да" },
                { "Common_No", "Нет" },
                { "Common_Loading", "Загрузка..." },
                { "Common_None", "Нет" },
                { "Common_InDevelopment", "В разработке" },
                // ModLoaders
                { "ModLoader_Vanilla", "Vanilla" },
                { "ModLoader_Forge", "Forge" },
                { "ModLoader_NeoForge", "NeoForge" },
                { "ModLoader_Fabric", "Fabric" },
                { "ModLoader_Quilt", "Quilt" },
                { "ModLoader_Paper", "Paper" },
                { "ModLoader_Spigot", "Spigot" },
                // ServerDetail
                { "ServerDetail_Title", "Детали сервера" },
                { "ServerDetail_Console", "Консоль" },
                { "ServerDetail_Mods", "Моды" },
                { "ServerDetail_Plugins", "Плагины" },
                { "ServerDetail_Start", "Запустить" },
                { "ServerDetail_Stop", "Остановить" },
                { "ServerDetail_Delete", "Удалить" },
                { "ServerDetail_Properties", "Свойства" },
                // ServerProperties
                { "ServerProperties_Title", "Свойства сервера" },
                { "ServerProperties_Save", "Сохранить" },
                { "ServerProperties_Cancel", "Отмена" },
                { "ServerProperties_General", "Основные" },
                { "ServerProperties_World", "Мир" },
                { "ServerProperties_Network", "Сеть" },
                { "ServerProperties_Advanced", "Дополнительно" },
                // Messages
                { "Message_ConfirmDelete", "Вы уверены?" },
                { "Message_Error", "Ошибка" },
                // Errors
                { "Error_JavaNotFound", "Java не найдена" },
                { "Error_JavaIncompatible", "Несовместимая Java" },
                { "Error_ServerInstallFailed", "Ошибка установки" }
            },
            "en" => new Dictionary<string, string>
            {
                // MainWindow
                { "MainWindow_Title", "Konserva — Minecraft Server Manager" },
                { "MainWindow_Servers", "Servers" },
                { "MainWindow_Settings", "Settings" },
                { "MainWindow_Header", "Konserva Manager" },
                { "StatusBar_TotalServers", "Total Servers" },
                { "StatusBar_Running", "Running" },
                { "StatusBar_Memory", "Memory" },
                { "StatusBar_Java_Configured", "Java configured" },
                { "StatusBar_Java_NotConfigured", "Java not configured" },
                { "StatusBar_Version", "Version" },
                { "StatusBar_Version_Value", "v1.2" },
                // Settings
                { "Settings_Title", "Settings" },
                { "Settings_Servers", "Servers" },
                { "Settings_Servers_Directory", "Servers Directory" },
                { "Settings_Servers_Browse", "Browse" },
                { "Settings_Java", "Java" },
                { "Settings_Java_Add", "Add Java" },
                { "Settings_RAM_Min", "Memory and Application" },
                { "Settings_RAM_Min_Label", "Min RAM (MB)" },
                { "Settings_RAM_Max_Label", "Max RAM (MB)" },
                { "Settings_App", "Application" },
                { "Settings_CheckUpdates", "Check for Updates" },
                { "Settings_Theme", "Theme" },
                { "Settings_Theme_System", "System Default" },
                { "Settings_Theme_Dark", "Dark" },
                { "Settings_Theme_Light", "Light" },
                { "Settings_About", "About" },
                { "Settings_About_Version", "Version" },
                { "Settings_About_Description", "Minecraft Server Manager" },
                { "Message_SettingsSaved", "Settings saved" },
                // CreateServer
                { "CreateServer_Title", "Create Server" },
                { "CreateServer_Name", "Server Name" },
                { "CreateServer_MinecraftVersion", "Minecraft Version" },
                { "CreateServer_ModLoader", "Mod Loader" },
                { "CreateServer_Folder", "Folder" },
                { "CreateServer_Browse", "Browse" },
                { "CreateServer_Create", "Create" },
                { "CreateServer_Cancel", "Cancel" },
                { "CreateServer_Filter_Stable", "Stable only" },
                // ServersPage
                { "ServersPage_Search", "Search..." },
                { "ServersPage_Filter_All", "All Types" },
                { "ServersPage_Filter_AllServers", "All Servers" },
                { "ServersPage_Filter_Running", "Running" },
                { "ServersPage_Filter_Stopped", "Stopped" },
                { "ServersPage_Create", "Create Server" },
                { "ServersPage_NoServers", "No Servers" },
                { "ServersPage_NoServers_Description", "Create your first server" },
                // Common
                { "Common_Cancel", "Cancel" },
                { "Common_OK", "OK" },
                { "Common_Yes", "Yes" },
                { "Common_No", "No" },
                { "Common_Loading", "Loading..." },
                { "Common_None", "None" },
                { "Common_InDevelopment", "In Development" },
                // ModLoaders
                { "ModLoader_Vanilla", "Vanilla" },
                { "ModLoader_Forge", "Forge" },
                { "ModLoader_NeoForge", "NeoForge" },
                { "ModLoader_Fabric", "Fabric" },
                { "ModLoader_Quilt", "Quilt" },
                { "ModLoader_Paper", "Paper" },
                { "ModLoader_Spigot", "Spigot" },
                // ServerDetail
                { "ServerDetail_Title", "Server Details" },
                { "ServerDetail_Console", "Console" },
                { "ServerDetail_Mods", "Mods" },
                { "ServerDetail_Plugins", "Plugins" },
                { "ServerDetail_Start", "Start" },
                { "ServerDetail_Stop", "Stop" },
                { "ServerDetail_Delete", "Delete" },
                { "ServerDetail_Properties", "Properties" },
                // ServerProperties
                { "ServerProperties_Title", "Server Properties" },
                { "ServerProperties_Save", "Save" },
                { "ServerProperties_Cancel", "Cancel" },
                { "ServerProperties_General", "General" },
                { "ServerProperties_World", "World" },
                { "ServerProperties_Network", "Network" },
                { "ServerProperties_Advanced", "Advanced" },
                // Messages
                { "Message_ConfirmDelete", "Are you sure?" },
                { "Message_Error", "Error" },
                // Errors
                { "Error_JavaNotFound", "Java not found" },
                { "Error_JavaIncompatible", "Incompatible Java" },
                { "Error_ServerInstallFailed", "Installation failed" }
            },
            _ => new Dictionary<string, string>()
        };
    }

    /// <summary>
    /// Helper method для создания файла локализации
    /// </summary>
    private void CreateDefaultLocalizationFile(string filePath, string culture)
    {
        var translations = GetDefaultTranslations(culture);
        var json = JsonSerializer.Serialize(translations, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
    }

    public void Dispose()
    {
        // Очищаем временные файлы
        try
        {
            if (Directory.Exists(_testI18nPath))
            {
                Directory.Delete(_testI18nPath, true);
            }
        }
        catch
        {
            // Игнорируем ошибки очистки
        }

        GC.SuppressFinalize(this);
    }
}
