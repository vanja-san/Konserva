using Konserva.Models;
using Konserva.Utilities;

namespace Konserva.Services;

/// <summary>
/// Implementation of the update check service.
/// Uses in-memory throttling + ETag conditional requests to save on the GitHub API rate limit.
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
    private readonly IConfigService _config;
    private readonly IUpdateChecker _updateChecker;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    // In-memory throttling: protection against frequent restarts during development.
    // Not serialized — it is reset on every application launch.
    private DateTime _lastFetchTimeUtc = DateTime.MinValue;
    private static readonly TimeSpan MinFetchInterval = TimeSpan.FromMinutes(15);

    public event Action<UpdateInfo>? UpdateAvailable;
    public event Action? CheckStarted;
    public event Action<UpdateInfo>? CheckCompleted;

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
        CheckStarted?.Invoke();
        var updateInfo = await FetchAndSaveAsync(force: true);
        CheckCompleted?.Invoke(updateInfo);
        RaiseIfAvailable(updateInfo);

        return updateInfo;
    }

    private void RaiseIfAvailable(UpdateInfo updateInfo)
    {
        if (updateInfo.IsAvailable)
            UpdateAvailable?.Invoke(updateInfo);
    }

    /// <summary>
    /// Performs an HTTP request to version.json on raw.githubusercontent.com.
    /// On success updates LastUpdateCheck in the config and _lastFetchTimeUtc in memory.
    /// </summary>
    private async Task<UpdateInfo> FetchAndSaveAsync(bool force = false)
    {
        // In-memory throttling: no more than once every 15 min (extra traffic saving)
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
            // Startup check (the in-memory throttle prevents it from running more often than every 15 min)
            CheckStarted?.Invoke();
            var updateInfo = await FetchAndSaveAsync();
            CheckCompleted?.Invoke(updateInfo);
            RaiseIfAvailable(updateInfo);

            var config = _config.GetConfig();

            if (!config.CheckUpdates)
            {
                Logger.Info("Update check in 'On Launch' mode — done", "UpdateService");
                return;
            }

            Logger.Info("Update check in 'Scheduled' mode — starting timer", "UpdateService");

            var lastInterval = -1;
            PeriodicTimer? timer = null;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    config = _config.GetConfig();
                    var intervalHours = Math.Clamp(config.UpdateCheckIntervalHours, 1, 168);

                    // Recreate the timer only when the interval changes
                    if (intervalHours != lastInterval)
                    {
                        timer?.Dispose();
                        timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));
                        lastInterval = intervalHours;
                    }

                    if (await timer!.WaitForNextTickAsync(ct))
                    {
                        CheckStarted?.Invoke();
                        var result = await FetchAndSaveAsync();
                        CheckCompleted?.Invoke(result);
                        RaiseIfAvailable(result);
                    }
                }
            }
            finally
            {
                timer?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation on Stop()
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
