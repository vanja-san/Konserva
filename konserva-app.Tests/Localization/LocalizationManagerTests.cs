using Konserva.Localization;
using Xunit;

namespace Konserva.Tests.Localization;

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
}
