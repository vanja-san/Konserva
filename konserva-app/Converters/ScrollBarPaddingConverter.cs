using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Konserva.Converters;

/// <summary>
/// Converts ComputedVerticalScrollBarVisibility into a Thickness for ScrollViewer.Padding.
///
/// When the scrollbar is visible the right padding is reduced to MinRightPadding,
/// so the scrollbar sits at the window edge but the content does not stick to the track.
/// When the scrollbar is hidden NormalRightPadding is used for symmetry with the left margin.
///
/// Parameter: "normal_padding" or "normal_padding|min_padding"
/// Defaults: NormalRightPadding=16, MinRightPadding=6
///
/// Usage:
/// <ScrollViewer Padding="{Binding ComputedVerticalScrollBarVisibility,
///     RelativeSource={RelativeSource Self},
///     Converter={StaticResource ScrollBarPadding},
///     ConverterParameter=16}" />
/// </summary>
public class ScrollBarPaddingConverter : IValueConverter
{
    public double NormalRightPadding { get; set; } = 16;
    public double MinRightPadding { get; set; } = 16;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Visibility visibility)
            return new Thickness(0);

        var normalPadding = NormalRightPadding;
        var minPadding = MinRightPadding;

        if (parameter is string paramStr)
        {
            var parts = paramStr.Split('|');
            if (parts.Length >= 1 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                normalPadding = parsed;
            if (parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMin))
                minPadding = parsedMin;
        }

        return visibility == Visibility.Visible
            ? new Thickness(0, 0, minPadding, 0)
            : new Thickness(0, 0, normalPadding, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
