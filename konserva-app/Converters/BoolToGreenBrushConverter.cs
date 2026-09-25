using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Konserva.Converters;

/// <summary>
/// Конвертирует boolean в кисть (зеленый или прозрачный)
/// </summary>
public class BoolToGreenBrushConverter : IValueConverter
{
    private static readonly Brush TransparentBrush = Brushes.Transparent;

    // Резервный цвет, если ресурса темы нет (например, в юнит-тестах).
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
            // Читаем ресурс темы на каждый вызов: static-кэш «замерзал» цвет
            // на момент первого обращения и не реагировал на смену темы.
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
