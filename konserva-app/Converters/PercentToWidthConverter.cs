using System.Globalization;
using System.Windows.Data;

namespace Konserva.Converters;

/// <summary>
/// Converts a percentage to the width of a progress bar
/// </summary>
public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percent && parameter is double maxWidth)
        {
            return maxWidth * (percent / 100.0);
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
