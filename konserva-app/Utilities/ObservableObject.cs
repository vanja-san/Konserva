using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Konserva.Utilities;

/// <summary>
/// Базовый класс для объектов, поддерживающих INotifyPropertyChanged
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
  public event PropertyChangedEventHandler? PropertyChanged;

  /// <summary>
  /// Вызов PropertyChanged для указанного свойства
  /// </summary>
  protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
  {
    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
  }

  /// <summary>
  /// Установка значения поля с уведомлением об изменении
  /// </summary>
  protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
  {
    if (EqualityComparer<T>.Default.Equals(field, value))
      return false;

    field = value;
    OnPropertyChanged(propertyName);
    return true;
  }
}
