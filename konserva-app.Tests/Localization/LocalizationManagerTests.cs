using Konserva.Localization;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Konserva.Tests.Localization;

[Trait("Category", "Unit")]
public class LocalizationManagerTests
{
    [Fact]
    public void Get_ReturnsNonEmpty_ForCommonKeys()
    {
        // Проверяем ключевые ключи которые должны быть всегда
        var criticalKeys = new[]
        {
            "MsgBtn_OK", "MsgBtn_Cancel", "MsgBtn_Delete",
            "MsgTitle_Info", "MsgTitle_Warning", "MsgTitle_Error",
            "Snackbar_JavaIncompatible_Title", "Snackbar_JavaIncompatible_Message",
            "ServersPage_Search", "ServersPage_Filter_All"
        };

        foreach (var key in criticalKeys)
        {
            var value = LocalizationManager.Get(key);
            Assert.False(string.IsNullOrEmpty(value), $"Key '{key}' returned empty value");
        }
    }

    [Fact]
    public void Get_WithFormatArgs_ReplacesPlaceholders()
    {
        var result = LocalizationManager.Get("Snackbar_JavaIncompatible_Message", "1.20.4", 21, 11);
        Assert.Contains("1.20.4", result);
        Assert.Contains("21", result);
        Assert.Contains("11", result);
    }

    [Fact]
    public void Get_WithFormatArgs_Plural_ReplacesPlaceholders()
    {
        var result = LocalizationManager.Get("Snackbar_JavaIncompatible_Message_Plural", "26.1.1", 25, "11, 21");
        Assert.Contains("26.1.1", result);
        Assert.Contains("25", result);
        Assert.Contains("11, 21", result);
    }

    [Fact]
    public void GetAllKeys_ReturnsNonEmpty()
    {
        // Убедимся что локализация инициализирована
        LocalizationManager.SetLanguage("ru");

        var keys = LocalizationManager.GetAllKeys();
        Assert.NotEmpty(keys);
    }

    [Fact]
    public void LoadCulture_Regenerates_StaleFlatFormatFile()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"konserva_i18n_regen_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        var originalPath = LocalizationManager.I18nPath;

        try
        {
            LocalizationManager.I18nPath = testDir;
            var filePath = Path.Combine(testDir, "ru.json");

            // Старый формат (плоский словарь, без конверта) с устаревшим значением
            File.WriteAllText(filePath, "{\"ServerDetail_Settings_UpdateChannel\":\"Мод успешно обновлён\"}", Encoding.UTF8);

            LocalizationManager.LoadCulture("ru");

            Assert.True(File.Exists(filePath), "File should be regenerated in place");

            var content = File.ReadAllText(filePath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("SchemaVersion", out var schemaVersion), "Envelope should contain SchemaVersion");
            Assert.True(schemaVersion.GetInt32() >= LocalizationManager.LocalizationSchemaVersion, "SchemaVersion must be current");
            Assert.True(root.TryGetProperty("SourceStamp", out var sourceStamp), "Envelope should contain SourceStamp");
            Assert.False(string.IsNullOrWhiteSpace(sourceStamp.GetString()), "SourceStamp must not be empty");

            var translations = root.GetProperty("Translations");
            var currentValue = LocalizationManager.GetDefaultTranslationsForCulture("ru")!["ServerDetail_Settings_UpdateChannel"];
            Assert.Equal(currentValue, translations.GetProperty("ServerDetail_Settings_UpdateChannel").GetString());
        }
        finally
        {
            LocalizationManager.I18nPath = originalPath;
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public void LoadCulture_WritesEnvelope_WhenFileMissing()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"konserva_i18n_create_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        var originalPath = LocalizationManager.I18nPath;

        try
        {
            LocalizationManager.I18nPath = testDir;

            LocalizationManager.LoadCulture("en");

            var filePath = Path.Combine(testDir, "en.json");
            Assert.True(File.Exists(filePath), "File should be created");

            var content = File.ReadAllText(filePath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(content);
            Assert.True(doc.RootElement.TryGetProperty("SchemaVersion", out _));
            Assert.True(doc.RootElement.TryGetProperty("SourceStamp", out var stamp));
            Assert.False(string.IsNullOrWhiteSpace(stamp.GetString()));
        }
        finally
        {
            LocalizationManager.I18nPath = originalPath;
            if (Directory.Exists(testDir))
                Directory.Delete(testDir, true);
        }
    }
}
