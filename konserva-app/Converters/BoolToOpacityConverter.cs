using System.Globalization;
using System.Windows.Data;

namespace Konserva.Converters;

/// <summary>
/// Converts bool to Opacity. true → 1.0, false → 0.55
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? 1.0 : 0.55;
        }
        return 1.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
