using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Konserva.Converters;

/// <summary>
/// Converts a boolean to a brush (green or transparent)
/// </summary>
public class BoolToGreenBrushConverter : IValueConverter
{
    private static readonly Brush TransparentBrush = Brushes.Transparent;

    // Fallback color used when the theme resource is missing (for example, in unit tests).
    private static readonly SolidColorBrush FallbackGreen = CreateFallback();

    private static SolidColorBrush CreateFallback()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Read the theme resource on every call: a static cache "froze" the color
            // at the time of the first access and did not react to a theme switch.
            return boolValue
                ? (Application.Current?.TryFindResource("SystemFillColorSuccessBrush") as SolidColorBrush
                   ?? FallbackGreen)
                : TransparentBrush;
        }

        return TransparentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
