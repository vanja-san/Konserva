using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Konserva.Converters;

/// <summary>
/// Конвертирует статус сервера в кисть
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    // Резервные цвета, если ресурса темы нет (например, в юнит-тестах).
    // Они никогда не меняются, поэтому их можно заморозить.
    private static readonly SolidColorBrush FallbackSuccess = Frozen(0x22, 0xC5, 0x5E);
    private static readonly SolidColorBrush FallbackWarning = Frozen(0xF5, 0x9E, 0x0B);
    private static readonly SolidColorBrush FallbackError = Frozen(0xEF, 0x44, 0x44);
    private static readonly Brush DefaultBrush = Brushes.Transparent;

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Берёт кисть из словаря ресурсов темы.
    /// <para>
    /// Ресурс читается на каждый вызов, а не кэшируется в static-поле:
    /// иначе цвета «замерзали» на момент первого обращения и не менялись
    /// при переключении светлой/тёмной темы.
    /// </para>
    /// </summary>
    private static SolidColorBrush FromTheme(string key, SolidColorBrush fallback)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush themeBrush)
            return themeBrush;

        return fallback;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Running" => FromTheme("SystemFillColorSuccessBrush", FallbackSuccess),
            "Starting" => FromTheme("SystemFillColorCautionBrush", FallbackWarning),
            "Stopping" => FromTheme("SystemFillColorCautionBrush", FallbackWarning),
            "Error" => FromTheme("SystemFillColorCriticalBrush", FallbackError),
            _ => DefaultBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
