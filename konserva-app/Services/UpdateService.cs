using Konserva.Models;
using Konserva.Utilities;

namespace Konserva.Services;

/// <summary>
/// Реализация сервиса проверки обновлений.
/// Использует in-memory троттлинг + ETag conditional requests для экономии лимита GitHub API.
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
  private readonly IConfigService _config;
  private readonly IUpdateChecker _updateChecker;
  private CancellationTokenSource? _cts;
  private bool _disposed;

  // In-memory троттлинг: защита от частых перезапусков при разработке.
  // Не сериализуется — сбрасывается при каждом запуске приложения.
  private DateTime _lastFetchTimeUtc = DateTime.MinValue;
  private static readonly TimeSpan MinFetchInterval = TimeSpan.FromMinutes(15);

  public event Action<UpdateInfo>? UpdateAvailable;

  public UpdateService(IConfigService config, IUpdateChecker updateChecker)
  {
    _config = config ?? throw new ArgumentNullException(nameof(config));
    _updateChecker = updateChecker ?? throw new ArgumentNullException(nameof(updateChecker));
  }

  /// <inheritdoc/>
  public void Start()
  {
    Stop();
    _cts = new CancellationTokenSource();
    LoopAsync(_cts.Token).SafeFireAndForget(errorMessage: "Update check loop failed");
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
    Logger.Info("Manual update check...", "UpdateService");
    var updateInfo = await FetchAndSaveAsync(force: true);

    if (updateInfo.IsAvailable)
    {
      UpdateAvailable?.Invoke(updateInfo);
    }

    return updateInfo;
  }

  /// <summary>
  /// Выполняет HTTP-запрос к version.json на raw.githubusercontent.com.
  /// При успехе обновляет LastUpdateCheck в конфиге и _lastFetchTimeUtc в памяти.
  /// </summary>
  private async Task<UpdateInfo> FetchAndSaveAsync(bool force = false)
  {
    // In-memory троттлинг: не чаще раза в 15 мин (лишняя экономия трафика)
    if (!force && (DateTime.UtcNow - _lastFetchTimeUtc) < MinFetchInterval)
    {
      Logger.Info("Skipped — last fetch < 15 min ago", "UpdateService");
      return new UpdateInfo
      {
        CurrentVersion = _updateChecker.GetCurrentVersion(),
        IsCheckSuccessful = true
      };
    }

    var updateInfo = await _updateChecker.CheckAsync();

    if (updateInfo.IsCheckSuccessful)
    {
      _lastFetchTimeUtc = DateTime.UtcNow;
      _config.UpdateConfig(c => c.LastUpdateCheck = SystemTime.UtcNow);
      Logger.Info("Update check completed successfully", "UpdateService");
    }
    else
    {
      _lastFetchTimeUtc = DateTime.UtcNow;
      Logger.Warning("Update check failed — will retry next interval", "UpdateService");
    }

    return updateInfo;
  }

  private async Task LoopAsync(CancellationToken ct)
  {
    try
    {
      // Стартовая проверка (in-memory throttle не даст вызвать чаще 15 мин)
      await FetchAndSaveAsync();

      var config = _config.GetConfig();

      if (!config.CheckUpdates)
      {
        Logger.Info("Update check in 'On Launch' mode — done", "UpdateService");
        return;
      }

      Logger.Info("Update check in 'Scheduled' mode — starting timer", "UpdateService");

      while (!ct.IsCancellationRequested)
      {
        config = _config.GetConfig();
        var intervalHours = Math.Clamp(config.UpdateCheckIntervalHours, 1, 168);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));

        if (await timer.WaitForNextTickAsync(ct))
        {
          await FetchAndSaveAsync();
        }
      }
    }
    catch (OperationCanceledException)
    {
      // Ожидаемая отмена при Stop()
    }
  }

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;
    Stop();
  }
}
