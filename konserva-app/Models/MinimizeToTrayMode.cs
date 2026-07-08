namespace Konserva.Models;

/// <summary>
/// Режим сворачивания в системный трей
/// </summary>
public enum MinimizeToTrayMode
{
    /// <summary>
    /// Не сворачивать в трей
    /// </summary>
    None,

    /// <summary>
    /// Только при закрытии окна
    /// </summary>
    OnClose,

    /// <summary>
    /// Только при сворачивании окна
    /// </summary>
    OnMinimize,

    /// <summary>
    /// При любом действии (и закрытии, и сворачивании)
    /// </summary>
    Always
}
