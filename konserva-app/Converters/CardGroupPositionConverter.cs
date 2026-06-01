using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Konserva.Converters;

/// <summary>
/// Возвращает CornerRadius, BorderThickness или Visibility разделителя
/// в зависимости от позиции элемента в группе слитных карточек.
/// Параметр: "cornerRadius" (по умолчанию), "borderThickness", "separator"
/// </summary>
public class CardGroupPositionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not int index || values[1] is not int count)
            return DependencyProperty.UnsetValue;

        var mode = parameter?.ToString() ?? "cornerRadius";

        return mode switch
        {
            "cornerRadius" => GetCornerRadius(index, count),
            "borderThickness" => GetBorderThickness(index, count),
            "separator" => GetSeparatorVisibility(index, count),
            _ => DependencyProperty.UnsetValue,
        };
    }

    private static CornerRadius GetCornerRadius(int index, int count)
    {
        if (count <= 1)
            return new CornerRadius(8);
        if (index == 0)
            return new CornerRadius(8, 8, 0, 0);
        if (index == count - 1)
            return new CornerRadius(0, 0, 8, 8);
        return new CornerRadius(0);
    }

    private static Thickness GetBorderThickness(int index, int count)
    {
        if (count <= 1)
            return new Thickness(1);
        if (index == 0)
            return new Thickness(1, 1, 1, 0);
        if (index == count - 1)
            return new Thickness(1, 0, 1, 1);
        return new Thickness(1, 0, 1, 0);
    }

    private static Visibility GetSeparatorVisibility(int index, int count)
    {
        return index < count - 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
