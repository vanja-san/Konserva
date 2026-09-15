using System.Windows.Controls;

namespace Konserva.Dialogs;

/// <summary>
/// XAML-содержимое окна «Кажется, чего-то не хватает…»
/// DataContext подставляется из <see cref="ModIncompatibilityViewBuilder.From"/>.
/// </summary>
public partial class ModIncompatibilityView : UserControl
{
    public ModIncompatibilityView()
    {
        InitializeComponent();
    }
}