using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Konserva.Converters;

/// <summary>
/// Конвертирует boolean в кисть (зеленый или прозрачный)
/// </summary>
public class BoolToGreenBrushConverter : IValueConverter
{
    private static readonly Brush GreenBrush = CreateThemeBrush("SystemFillColorSuccessBrush", 0x22, 0xC5, 0x5E);
    private static readonly Brush TransparentBrush = Brushes.Transparent;

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
        if (value is bool boolValue)
        {
            return boolValue ? GreenBrush : TransparentBrush;
        }
        return TransparentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}