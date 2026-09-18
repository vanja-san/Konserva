using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Konserva.Converters;

/// <summary>
/// Конвертирует статус сервера в кисть
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush SuccessBrush = CreateThemeBrush("SystemFillColorSuccessBrush", 0x22, 0xC5, 0x5E);
    private static readonly Brush WarningBrush = CreateThemeBrush("SystemFillColorCautionBrush", 0xF5, 0x9E, 0x0B);
    private static readonly Brush ErrorBrush = CreateThemeBrush("SystemFillColorCriticalBrush", 0xEF, 0x44, 0x44);
    private static readonly Brush DefaultBrush = Brushes.Transparent;

    private static SolidColorBrush CreateThemeBrush(string key, byte fallbackR, byte fallbackG, byte fallbackB)
    {
        var color = (System.Windows.Application.Current?.TryFindResource(key) as SolidColorBrush)?.Color
            ?? Color.FromRgb(fallbackR, fallbackG, fallbackB);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Running" => SuccessBrush,
            "Starting" => WarningBrush,
            "Stopping" => WarningBrush,
            "Error" => ErrorBrush,
            _ => DefaultBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}