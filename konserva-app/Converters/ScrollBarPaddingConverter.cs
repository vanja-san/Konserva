using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Konserva.Converters;

/// <summary>
/// Конвертирует ComputedVerticalScrollBarVisibility в Thickness для ScrollViewer.Padding.
///
/// Когда скроллбар видим — правый паддинг уменьшается до MinRightPadding,
/// чтобы скроллбар был у края окна, но контент не прилипал к треку.
/// Когда скроллбар скрыт — используется NormalRightPadding для симметрии с левым отступом.
///
/// Параметр: "нормальный_отступ" или "нормальный_отступ|минимальный_отступ"
/// По умолчанию: NormalRightPadding=16, MinRightPadding=6
///
/// Использование:
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
