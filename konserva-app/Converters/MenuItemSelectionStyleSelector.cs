using System.Windows;
using System.Windows.Controls;

namespace Konserva.Converters;

/// <summary>
/// Отдаёт стиль выделения выбранного пункта только для MenuItem,
/// а для Separator — null (дефолтное оформление).
/// </summary>
public class MenuItemSelectionStyleSelector : StyleSelector
{
    public Style? MenuItemStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
    {
        return item is Separator ? null : MenuItemStyle;
    }
}