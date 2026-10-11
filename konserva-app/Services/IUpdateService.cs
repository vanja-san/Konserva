using Konserva.Models;

namespace Konserva.Services;

/// <summary>
/// Background update check service.
/// Manages the auto-check lifecycle: start, stop, forced check.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Event: an update was found.
    /// </summary>
    event Action<UpdateInfo>? UpdateAvailable;

    /// <summary>
    /// Event: the update check has started (for the UI — show a ProgressRing).
    /// </summary>
    event Action? CheckStarted;

    /// <summary>
    /// Event: the update check has finished (for the UI — hide the ProgressRing).
    /// </summary>
    event Action<UpdateInfo>? CheckCompleted;

    /// <summary>
    /// Starts the background auto-check loop.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the background auto-check loop.
    /// </summary>
    void Stop();

    /// <summary>
    /// Forced update check (out of queue, used by the UI).
    /// </summary>
    Task<UpdateInfo> ForceCheckAsync();
}
