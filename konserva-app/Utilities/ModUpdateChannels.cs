using Konserva.Models;

namespace Konserva.Utilities;

/// <summary>
/// Преобразование канала обновлений в список version_types Modrinth.
/// </summary>
public static class ModUpdateChannels
{
    public static IReadOnlyList<string> ToVersionTypes(ModUpdateChannel channel) => channel switch
    {
        ModUpdateChannel.BetaOnly => ["beta"],
        ModUpdateChannel.AlphaOnly => ["alpha"],
        ModUpdateChannel.All => ["release", "beta", "alpha"],
        _ => ["release"]
    };
}
