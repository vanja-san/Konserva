using Konserva.Models;

namespace Konserva.Utilities;

/// <summary>
/// Conversion of an update channel to a Modrinth version_types list.
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
