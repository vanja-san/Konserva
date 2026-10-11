using System.Globalization;
using System.Windows.Data;

namespace Konserva.Converters;

/// <summary>
/// Converter: checks whether a string is empty
/// </summary>
public class StringIsEmptyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return string.IsNullOrEmpty(str);
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return System.Windows.DependencyProperty.UnsetValue;
    }
}
