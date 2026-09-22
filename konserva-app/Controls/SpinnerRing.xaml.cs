using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace Konserva.Controls;

/// <summary>
/// Надёжная индикация загрузки: дуга, вращение которой запускается из кода
/// (в отличие от WPF UI ProgressRing, где Storyboard стартует по триггеру шаблона).
/// Эффект «кометы»: дуга нарастает от точки до почти полного кольца, держится и схлопывается.
/// </summary>
public partial class SpinnerRing : System.Windows.Controls.UserControl
{
    public SpinnerRing()
    {
        InitializeComponent();
        Loaded += (_, _) => StartSpin();
    }

    private void StartSpin()
    {
        Rotation.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            });
    }
}