using Konserva.Models;

namespace Konserva.Services;

/// <summary>
/// Сервис фоновой проверки обновлений.
/// Управляет жизненным циклом авто-проверки: старт, стоп, принудительная проверка.
/// </summary>
public interface IUpdateService
{
  /// <summary>
  /// Событие: найдено обновление.
  /// </summary>
  event Action<UpdateInfo>? UpdateAvailable;

  /// <summary>
  /// Запускает фоновый цикл авто-проверки обновлений.
  /// </summary>
  void Start();

  /// <summary>
  /// Останавливает фоновый цикл авто-проверки.
  /// </summary>
  void Stop();

  /// <summary>
  /// Принудительная проверка обновлений (вне очереди, для UI).
  /// </summary>
  Task<UpdateInfo> ForceCheckAsync();
}
