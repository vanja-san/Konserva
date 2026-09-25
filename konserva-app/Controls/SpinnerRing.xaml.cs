using System.Windows;
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
    private DoubleAnimation? _spinAnimation;

    public SpinnerRing()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => StartSpin();

    private void OnUnloaded(object sender, RoutedEventArgs e) => StopSpin();

    /// <summary>
    /// Запускает вращение. Повторный вызов безопасен — анимация не дублируется.
    /// </summary>
    public void StartSpin()
    {
        if (_spinAnimation != null)
            return;

        _spinAnimation = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };

        Rotation.BeginAnimation(RotateTransform.AngleProperty, _spinAnimation);
    }

    /// <summary>
    /// Останавливает вращение и сбрасывает угол.
    /// Обязательно вызывается при скрытии: анимация с RepeatBehavior.Forever
    /// иначе продолжает молотить рендер-путь всё время жизни приложения.
    /// </summary>
    public void StopSpin()
    {
        if (_spinAnimation == null)
            return;

        Rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        Rotation.Angle = 0;
        _spinAnimation = null;
    }
}
