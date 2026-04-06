using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Konserva.Converters;

/// <summary>
/// Конвертирует boolean в кисть (зеленый или прозрачный)
/// </summary>
public class BoolToGreenBrushConverter : IValueConverter
{
    private static readonly Brush GreenBrush = CreateBrush(0x22, 0xC5, 0x5E);
    private static readonly Brush TransparentBrush = Brushes.Transparent;

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
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
