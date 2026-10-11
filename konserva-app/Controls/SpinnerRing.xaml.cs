using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace Konserva.Controls;

/// <summary>
/// Reliable loading indicator: an arc whose rotation is started from code
/// (unlike the WPF UI ProgressRing, where the Storyboard starts on a template trigger).
/// "Comet" effect: the arc grows from a point to almost a full ring, holds, and then collapses.
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
    /// Starts the rotation. A repeated call is safe — the animation is not duplicated.
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
    /// Stops the rotation and resets the angle.
    /// Must be called on hide: an animation with RepeatBehavior.Forever
    /// would otherwise keep hammering the render path for the whole application lifetime.
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
