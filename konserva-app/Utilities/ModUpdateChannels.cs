using Konserva.Models;

namespace Konserva.Utilities;

/// <summary>
/// Преобразование канала обновлений в список version_types Modrinth.
/// </summary>
public static class ModUpdateChannels
{
    public static IReadOnlyList<string> ToVersionTypes(ModUpdateChannel channel) => channel switch
    {
        ModUpdateChannel.Beta => ["release", "beta"],
        ModUpdateChannel.Alpha => ["release", "beta", "alpha"],
        _ => ["release"]
    };
}
