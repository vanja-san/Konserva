namespace Konserva.Models;

/// <summary>
/// Канал обновлений, который учитывается при поиске новой версии мода.
/// Соответствует version_type Modrinth (release / beta / alpha).
/// </summary>
public enum ModUpdateChannel
{
    /// <summary>Только стабильные релизы (тестовые версии не предлагаются).</summary>
    Release,

    /// <summary>Только бета-версии.</summary>
    BetaOnly,

    /// <summary>Только альфа-версии.</summary>
    AlphaOnly,

    /// <summary>Все версии: релизы, беты и альфы.</summary>
    All
}