namespace Konserva.Models;

/// <summary>
/// Канал обновлений, который учитывается при поиске новой версии мода.
/// </summary>
public enum ModUpdateChannel
{
    /// <summary>Только стабильные релизы.</summary>
    Release,

    /// <summary>Релизы и беты.</summary>
    Beta,

    /// <summary>Релизы, беты и альфы.</summary>
    Alpha
}
