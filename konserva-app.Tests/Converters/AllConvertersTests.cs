using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Konserva.Converters;

namespace Konserva.Tests.Converters;

/// <summary>
/// Тесты для BoolToGreenBrushConverter
/// </summary>
public class BoolToGreenBrushConverterTests
{
    private readonly BoolToGreenBrushConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsGreenBrush()
    {
        var result = _converter.Convert(true, typeof(Brush), null, CultureInfo.InvariantCulture);
        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result;
        brush.Color.ToString().Should().Be("#FF22C55E");
        brush.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void Convert_False_ReturnsTransparentBrush()
    {
        var result = _converter.Convert(false, typeof(Brush), null, CultureInfo.InvariantCulture);
        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result;
        brush.Color.A.Should().Be(0); // fully transparent
        brush.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void Convert_NonBoolInput_ReturnsTransparentBrush()
    {
        var result = _converter.Convert("not a bool", typeof(Brush), null, CultureInfo.InvariantCulture);
        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result;
        brush.Color.A.Should().Be(0); // fully transparent
    }

    [Fact]
    public void Convert_NullInput_ReturnsTransparentBrush()
    {
        var result = _converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture);
        result.Should().BeOfType<SolidColorBrush>();
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        _converter.Invoking(c => c.ConvertBack(null, typeof(bool), null, CultureInfo.InvariantCulture))
            .Should().Throw<NotImplementedException>();
    }
}

/// <summary>
/// Тесты для BoolToVisibilityConverter
/// </summary>
public class BoolToVisibilityConverterTests
{
    private readonly BoolToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsVisible()
    {
        var result = _converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Convert_False_ReturnsCollapsed()
    {
        var result = _converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_TrueWithInvertParam_ReturnsCollapsed()
    {
        var result = _converter.Convert(true, typeof(Visibility), "invert", CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_FalseWithInvertParam_ReturnsVisible()
    {
        var result = _converter.Convert(false, typeof(Visibility), "invert", CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Convert_InvertParam_CaseInsensitive()
    {
        _converter.Convert(true, typeof(Visibility), "INVERT", CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
        _converter.Convert(true, typeof(Visibility), "InVeRt", CultureInfo.InvariantCulture).Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_NonBoolInput_ReturnsCollapsed()
    {
        var result = _converter.Convert("not a bool", typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ConvertBack_Visible_ReturnsTrue()
    {
        var result = _converter.ConvertBack(Visibility.Visible, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(true);
    }

    [Fact]
    public void ConvertBack_Collapsed_ReturnsFalse()
    {
        var result = _converter.ConvertBack(Visibility.Collapsed, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Fact]
    public void ConvertBack_Hidden_ReturnsFalse()
    {
        var result = _converter.ConvertBack(Visibility.Hidden, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Fact]
    public void ConvertBack_NonVisibilityInput_ReturnsFalse()
    {
        var result = _converter.ConvertBack("not visibility", typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }
}

/// <summary>
/// Тесты для BoolToVisibilityInverseConverter
/// </summary>
public class BoolToVisibilityInverseConverterTests
{
    private readonly BoolToVisibilityInverseConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsCollapsed()
    {
        var result = _converter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_False_ReturnsVisible()
    {
        var result = _converter.Convert(false, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Convert_NonBoolInput_ReturnsVisible()
    {
        var result = _converter.Convert("not a bool", typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void ConvertBack_Collapsed_ReturnsTrue()
    {
        var result = _converter.ConvertBack(Visibility.Collapsed, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(true);
    }

    [Fact]
    public void ConvertBack_Visible_ReturnsFalse()
    {
        var result = _converter.ConvertBack(Visibility.Visible, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Fact]
    public void ConvertBack_NonVisibilityInput_ReturnsFalse()
    {
        var result = _converter.ConvertBack("not visibility", typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }
}

/// <summary>
/// Тесты для EmptyToVisibilityConverter
/// </summary>
public class EmptyToVisibilityConverterTests
{
    private readonly EmptyToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_EmptyString_ReturnsVisible()
    {
        var result = _converter.Convert("", typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Convert_NullString_ReturnsCollapsed()
    {
        // EmptyToVisibilityConverter: non-string input returns Collapsed
        var result = _converter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_WhitespaceString_ReturnsVisible()
    {
        var result = _converter.Convert("   ", typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Convert_NonEmptyString_ReturnsCollapsed()
    {
        var result = _converter.Convert("hello", typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_NonStringInput_ReturnsCollapsed()
    {
        var result = _converter.Convert(42, typeof(Visibility), null, CultureInfo.InvariantCulture);
        result.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        _converter.Invoking(c => c.ConvertBack(null, typeof(string), null, CultureInfo.InvariantCulture))
            .Should().Throw<NotImplementedException>();
    }
}

/// <summary>
/// Тесты для FileSizeConverter
/// </summary>
public class FileSizeConverterTests
{
    private readonly FileSizeConverter _converter = new();

    [Fact]
    public void Convert_ZeroBytes_ReturnsZeroB()
    {
        var result = _converter.Convert(0L, typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("0 B");
    }

    [Fact]
    public void Convert_Bytes_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(512L, typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("512 B");
    }

    [Fact]
    public void Convert_Kilobytes_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(1536L, typeof(string), null, CultureInfo.InvariantCulture);
        // InvariantCulture uses '.' as decimal separator
        ((string)result).Should().Contain("KB");
        ((string)result).Should().Contain("1");
    }

    [Fact]
    public void Convert_Megabytes_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(1048576L, typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("1 MB");
    }

    [Fact]
    public void Convert_Gigabytes_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(1073741824L, typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("1 GB");
    }

    [Fact]
    public void Convert_Terabytes_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(1099511627776L, typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("1 TB");
    }

    [Fact]
    public void Convert_IntValue_ReturnsCorrectFormat()
    {
        var result = _converter.Convert(2048, typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("2 KB");
    }

    [Fact]
    public void Convert_NonNumericInput_ReturnsZeroB()
    {
        var result = _converter.Convert("not a number", typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("0 B");
    }

    [Fact]
    public void Convert_NullInput_ReturnsZeroB()
    {
        var result = _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("0 B");
    }

    [Fact]
    public void Convert_Back_ThrowsNotImplementedException()
    {
        _converter.Invoking(c => c.ConvertBack("1 MB", typeof(long), null, CultureInfo.InvariantCulture))
            .Should().Throw<NotImplementedException>();
    }
}

/// <summary>
/// Тесты для PercentToWidthConverter
/// </summary>
public class PercentToWidthConverterTests
{
    private readonly PercentToWidthConverter _converter = new();

    [Fact]
    public void Convert_50Percent_ReturnsHalfWidth()
    {
        var result = _converter.Convert(50.0, typeof(double), 200.0, CultureInfo.InvariantCulture);
        result.Should().Be(100.0);
    }

    [Fact]
    public void Convert_100Percent_ReturnsFullWidth()
    {
        var result = _converter.Convert(100.0, typeof(double), 300.0, CultureInfo.InvariantCulture);
        result.Should().Be(300.0);
    }

    [Fact]
    public void Convert_0Percent_ReturnsZeroWidth()
    {
        var result = _converter.Convert(0.0, typeof(double), 200.0, CultureInfo.InvariantCulture);
        result.Should().Be(0.0);
    }

    [Fact]
    public void Convert_Over100Percent_ReturnsWidthGreaterThanMax()
    {
        var result = _converter.Convert(150.0, typeof(double), 100.0, CultureInfo.InvariantCulture);
        result.Should().Be(150.0);
    }

    [Fact]
    public void Convert_NonDoubleValue_ReturnsZero()
    {
        var result = _converter.Convert("50", typeof(double), 200.0, CultureInfo.InvariantCulture);
        result.Should().Be(0.0);
    }

    [Fact]
    public void Convert_NonDoubleParameter_ReturnsZero()
    {
        var result = _converter.Convert(50.0, typeof(double), "200", CultureInfo.InvariantCulture);
        result.Should().Be(0.0);
    }

    [Fact]
    public void Convert_NullParameter_ReturnsZero()
    {
        var result = _converter.Convert(50.0, typeof(double), null, CultureInfo.InvariantCulture);
        result.Should().Be(0.0);
    }

    [Fact]
    public void ConvertBack_ThrowsNotImplementedException()
    {
        _converter.Invoking(c => c.ConvertBack(100.0, typeof(double), null, CultureInfo.InvariantCulture))
            .Should().Throw<NotImplementedException>();
    }
}

/// <summary>
/// Тесты для StatusToBrushConverter
/// </summary>
public class StatusToBrushConverterTests
{
    private readonly StatusToBrushConverter _converter = new();

    [Theory]
    [InlineData("Running", "#FF22C55E")]
    [InlineData("Starting", "#FFF59E0B")]
    [InlineData("Stopping", "#FFF59E0B")]
    [InlineData("Error", "#FFEF4444")]
    [InlineData("Stopped", "#FF333333")]
    [InlineData("Unknown", "#FF333333")]
    public void Convert_Status_ReturnsCorrectBrush(string status, string expectedColor)
    {
        var result = _converter.Convert(status, typeof(Brush), null, CultureInfo.InvariantCulture);
        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result;
        brush.Color.ToString().Should().Be(expectedColor);
        brush.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void Convert_NullInput_ReturnsDefaultBrush()
    {
        var result = _converter.Convert(null, typeof(Brush), null, CultureInfo.InvariantCulture);
        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result;
        brush.Color.ToString().Should().Be("#FF333333");
    }

    [Fact]
    public void Convert_NonStringInput_ReturnsDefaultBrush()
    {
        var result = _converter.Convert(42, typeof(Brush), null, CultureInfo.InvariantCulture);
        result.Should().BeOfType<SolidColorBrush>();
        var brush = (SolidColorBrush)result;
        brush.Color.ToString().Should().Be("#FF333333");
    }

    [Fact]
    public void Convert_Back_ThrowsNotImplementedException()
    {
        _converter.Invoking(c => c.ConvertBack(null, typeof(string), null, CultureInfo.InvariantCulture))
            .Should().Throw<NotImplementedException>();
    }
}

/// <summary>
/// Тесты для StringIsEmptyConverter
/// </summary>
public class StringIsEmptyConverterTests
{
    private readonly StringIsEmptyConverter _converter = new();

    [Fact]
    public void Convert_EmptyString_ReturnsTrue()
    {
        var result = _converter.Convert("", typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(true);
    }

    [Fact]
    public void Convert_NonEmptyString_ReturnsFalse()
    {
        var result = _converter.Convert("hello", typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Fact]
    public void Convert_NullString_ReturnsTrue()
    {
        var result = _converter.Convert(null, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(true);
    }

    [Fact]
    public void Convert_NonStringInput_ReturnsTrue()
    {
        var result = _converter.Convert(42, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(true);
    }

    [Fact]
    public void Convert_WhitespaceString_ReturnsFalse()
    {
        // Uses IsNullOrEmpty, not IsNullOrWhiteSpace
        var result = _converter.Convert("   ", typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Fact]
    public void ConvertBack_ReturnsUnsetValue()
    {
        var result = _converter.ConvertBack(true, typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be(System.Windows.DependencyProperty.UnsetValue);
    }
}

/// <summary>
/// Тесты для StringToPlaceholderConverter
/// </summary>
public class StringToPlaceholderConverterTests
{
    private readonly StringToPlaceholderConverter _converter = new();

    [Fact]
    public void Convert_EmptyString_ReturnsPlaceholder()
    {
        var result = _converter.Convert("", typeof(string), "Enter text", CultureInfo.InvariantCulture);
        result.Should().Be("Enter text");
    }

    [Fact]
    public void Convert_NonEmptyString_ReturnsEmpty()
    {
        var result = _converter.Convert("hello", typeof(string), "Enter text", CultureInfo.InvariantCulture);
        result.Should().Be("");
    }

    [Fact]
    public void Convert_NullString_ReturnsEmpty()
    {
        // StringToPlaceholderConverter: null is not a string, so the 'is string' check fails
        var result = _converter.Convert(null, typeof(string), "Placeholder", CultureInfo.InvariantCulture);
        result.Should().Be("");
    }

    [Fact]
    public void Convert_NullParameter_ReturnsEmpty()
    {
        var result = _converter.Convert("", typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be("");
    }

    [Fact]
    public void Convert_NonStringInput_ReturnsEmpty()
    {
        var result = _converter.Convert(42, typeof(string), "Placeholder", CultureInfo.InvariantCulture);
        result.Should().Be("");
    }

    [Fact]
    public void ConvertBack_ReturnsUnsetValue()
    {
        var result = _converter.ConvertBack("text", typeof(string), null, CultureInfo.InvariantCulture);
        result.Should().Be(System.Windows.DependencyProperty.UnsetValue);
    }
}
