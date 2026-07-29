using Konserva.Utilities;

namespace Konserva.Services;

public interface IModLoaderService
{
    string[] FilterMcVersions(string[] allVersions, string modLoader, HashSet<string> paperVersions, HashSet<string>? quiltVersions, bool showSnapshots);

    Task<string[]> GetLoaderVersionsAsync(string modLoaderType, string mcVersion, bool showSnapshots);

    Task<string?> FindCompatibleMcVersionAsync(string modLoaderType, string currentMcVersion, string[] allMcVersions, bool showSnapshots);
}
