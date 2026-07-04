using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Konserva.Converters;

/// <summary>
/// Конвертирует статус сервера в кисть
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush SuccessBrush = CreateBrush(0x22, 0xC5, 0x5E);
    private static readonly Brush WarningBrush = CreateBrush(0xF5, 0x9E, 0x0B);
    private static readonly Brush ErrorBrush = CreateBrush(0xEF, 0x44, 0x44);
    private static readonly Brush DefaultBrush = Brushes.Transparent;

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
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
