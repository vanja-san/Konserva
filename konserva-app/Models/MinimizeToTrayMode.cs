namespace Konserva.Models;

/// <summary>
/// Minimize-to-system-tray mode
/// </summary>
public enum MinimizeToTrayMode
{
    /// <summary>
    /// Do not minimize to tray
    /// </summary>
    None,

    /// <summary>
    /// Only when the window is closed
    /// </summary>
    OnClose,

    /// <summary>
    /// Only when the window is minimized
    /// </summary>
    OnMinimize,

    /// <summary>
    /// On any action (both closing and minimizing)
    /// </summary>
    Always
}
