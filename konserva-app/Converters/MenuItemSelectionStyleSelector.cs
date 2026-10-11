using System.Windows;
using System.Windows.Controls;

namespace Konserva.Converters;

/// <summary>
/// Returns the selected-item highlight style only for MenuItem,
/// and null for Separator (default appearance).
/// </summary>
public class MenuItemSelectionStyleSelector : StyleSelector
{
    public Style? MenuItemStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
    {
        return item is Separator ? null : MenuItemStyle;
    }
}