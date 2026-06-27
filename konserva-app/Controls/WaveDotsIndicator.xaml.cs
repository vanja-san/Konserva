using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Konserva.Controls;

/// <summary>
/// Индикатор с анимацией волны из трёх точек.
/// Используется на страницах создания сервера и настроек.
/// </summary>
public partial class WaveDotsIndicator : UserControl
{
  private bool _animationStarted;

  public WaveDotsIndicator()
  {
    InitializeComponent();
    Unloaded += OnUnloaded;
  }

  /// <summary>
  /// Запускает анимацию волны точек.
  /// </summary>
  public void Start()
  {
    if (Resources["WaveAnimation"] is Storyboard sb)
    {
      sb.Begin();
      _animationStarted = true;
    }
  }

  /// <summary>
  /// Останавливает анимацию волны точек.
  /// </summary>
  public void Stop()
  {
    if (_animationStarted && Resources["WaveAnimation"] is Storyboard sb)
    {
      sb.Stop();
      _animationStarted = false;
    }
  }

  private void OnUnloaded(object sender, RoutedEventArgs e)
  {
    Stop();
  }
}
