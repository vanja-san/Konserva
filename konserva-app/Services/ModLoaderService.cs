using Konserva.Utilities;

namespace Konserva.Services;

internal sealed class ModLoaderService(IMcVersionsApi versionsApi) : IModLoaderService
{
    private readonly IMcVersionsApi _versionsApi = versionsApi;

    public string[] FilterMcVersions(string[] allVersions, string modLoader, HashSet<string> paperVersions, HashSet<string>? quiltVersions, bool showSnapshots)
    {
        HashSet<string> supportedVersions;

        if (modLoader == "Paper")
            supportedVersions = paperVersions.Count > 0 ? paperVersions : [.. allVersions];
        else if (modLoader == "NeoForge")
            supportedVersions = [.. allVersions.Where(v =>
                McVersionHelper.TryParseMcVersion(v, out var major, out var minor) && (major > 1 || minor >= 16))];
        else if (modLoader == "Quilt" && quiltVersions != null)
            supportedVersions = quiltVersions;
        else
            supportedVersions = [.. allVersions];

        return allVersions
            .Where(v => supportedVersions.Contains(v))
            .Where(v => showSnapshots || !McVersionHelper.IsSnapshot(v))
            .ToArray();
    }

    public async Task<string[]> GetLoaderVersionsAsync(string modLoaderType, string mcVersion, bool showSnapshots)
    {
        string[] versions = modLoaderType switch
        {
            "Forge" => await _versionsApi.GetForgeVersions(mcVersion),
            "NeoForge" => await _versionsApi.GetNeoForgeVersions(mcVersion),
            "Fabric" => await _versionsApi.GetFabricVersions(mcVersion),
            "Quilt" => await _versionsApi.GetQuiltVersions(mcVersion),
            "Paper" => await _versionsApi.GetPaperVersions(mcVersion),
            _ => []
        };

        if (!showSnapshots)
        {
            versions = modLoaderType switch
            {
                "NeoForge" => [.. versions.Where(v => !McVersionHelper.IsNeoForgeSnapshot(v))],
                "Quilt" => [.. versions.Where(v => !v.Contains("-beta", StringComparison.OrdinalIgnoreCase))],
                "Paper" => [.. versions.Where(v => !v.Contains("(ALPHA)", StringComparison.OrdinalIgnoreCase))],
                _ => versions
            };
        }

        return versions;
    }

    public async Task<string?> FindCompatibleMcVersionAsync(string modLoaderType, string currentMcVersion, string[] allMcVersions, bool showSnapshots)
    {
        string[] currentVersions = await GetLoaderVersionsAsync(modLoaderType, currentMcVersion, showSnapshots);

        if (currentVersions.Length > 0 && currentVersions[0] != "latest")
            return currentMcVersion;

        var mcVersions = allMcVersions
            .Where(v => showSnapshots || !McVersionHelper.IsSnapshot(v))
            .Take(10)
            .ToList();

        foreach (var version in mcVersions)
        {
            if (version == currentMcVersion) continue;
            try
            {
                var versions = await GetLoaderVersionsAsync(modLoaderType, version, showSnapshots);
                if (versions.Length > 0 && versions[0] != "latest")
                    return version;
            }
            catch { /* skip */ }
        }

        return null;
    }
}
