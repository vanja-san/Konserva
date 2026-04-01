using System.Globalization;
using System.Windows.Data;

namespace Konserva.Converters;

/// <summary>
/// Конвертирует числовое значение в формат размера (B, KB, MB, GB)
/// </summary>
public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
            return FormatSize(bytes);
        if (value is int intSize)
            return FormatSize(intSize);
        return "0 B";

        static string FormatSize(long size)
        {
            string[] sizes = ["B", "KB", "MB", "GB", "TB"];
            int order = 0;
            double sizeDouble = size;

            while (sizeDouble >= 1024 && order < sizes.Length - 1)
            {
                order++;
                sizeDouble /= 1024;
            }

            return $"{sizeDouble:0.##} {sizes[order]}";
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
