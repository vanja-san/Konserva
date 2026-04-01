using System.Globalization;
using System.Windows.Data;

namespace Konserva.Converters;

/// <summary>
/// Конвертер: показывает placeholder если текст пуст
/// </summary>
public class StringToPlaceholderConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && string.IsNullOrEmpty(str))
        {
            return parameter?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return System.Windows.DependencyProperty.UnsetValue;
    }
}
