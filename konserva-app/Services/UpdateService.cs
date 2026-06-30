using Konserva.Models;
using Konserva.Utilities;

namespace Konserva.Services;

/// <summary>
/// Реализация сервиса проверки обновлений.
/// Выделена из MainWindow для уменьшения его ответственности.
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
  private readonly IConfigService _config;
  private CancellationTokenSource? _cts;
  private bool _disposed;

  public event Action<UpdateInfo>? UpdateAvailable;

  public UpdateService(IConfigService config)
  {
    _config = config ?? throw new ArgumentNullException(nameof(config));
  }

  /// <inheritdoc/>
  public void Start()
  {
    Stop();
    _cts = new CancellationTokenSource();
    _ = LoopAsync(_cts.Token);
  }

  /// <inheritdoc/>
  public void Stop()
  {
    _cts?.Cancel();
    _cts?.Dispose();
    _cts = null;
  }

  /// <inheritdoc/>
  public async Task<UpdateInfo> ForceCheckAsync()
  {
    var updateInfo = await UpdateChecker.CheckAsync();

    if (updateInfo.IsAvailable)
    {
      UpdateAvailable?.Invoke(updateInfo);
    }

    // Обновляем время последней проверки
    _config.UpdateConfig(c => c.LastUpdateCheck = SystemTime.UtcNow);

    return updateInfo;
  }

  private async Task LoopAsync(CancellationToken ct)
  {
    try
    {
      // Первая проверка при старте (всегда, в любом режиме)
      await ForceCheckAsync();

      while (!ct.IsCancellationRequested)
      {
        var config = _config.GetConfig();

        if (!config.CheckUpdates)
        {
          // Режим «При запуске» — ждём и проверяем, не переключили ли режим
          await Task.Delay(TimeSpan.FromMinutes(1), ct);
          continue;
        }

        // Режим «Заданное время» — ждём интервал и проверяем
        var intervalHours = Math.Clamp(config.UpdateCheckIntervalHours, 1, 168);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));

        if (await timer.WaitForNextTickAsync(ct))
        {
          await ForceCheckAsync();
        }
      }
    }
    catch (OperationCanceledException)
    {
      // Ожидаемая отмена
    }
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    Stop();
  }
}
