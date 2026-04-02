using Konserva.Services;
using System.Text.RegularExpressions;

namespace Konserva.Utilities;

public static partial class JavaVersionParser
{
    [GeneratedRegex(@"version ""?([^""\s]+)""?")]
    private static partial Regex VersionRegex();

    public static string ParseVersion(string versionOutput)
    {
        var match = VersionRegex().Match(versionOutput);
        return match.Success ? match.Groups[1].Value : "неизвестно";
    }

    public static int ParseMajorVersion(string versionOutput)
    {
        var version = ParseVersion(versionOutput);

        if (string.IsNullOrEmpty(version) || version == "неизвестно")
            return 8;

        int majorVersion = 8;

        if (version.StartsWith("1."))
        {
            if (version.Length >= 3 && int.TryParse(version.AsSpan(2, 1), out var oldMajor))
            {
                majorVersion = oldMajor;
            }
        }
        else
        {
            var firstPart = version.Split('.').FirstOrDefault();
            if (int.TryParse(firstPart, out var newMajor))
            {
                majorVersion = newMajor;
            }
        }

        return majorVersion;
    }

    public static int GetRequiredJavaVersion(string mcVersion, McServerInstaller.ServerLaunchType launchType)
    {
        if (launchType is McServerInstaller.ServerLaunchType.Forge or McServerInstaller.ServerLaunchType.NeoForge)
            return 17;

        var parts = mcVersion.Split('.');
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor))
        {
            if (major == 1 && minor >= 21) return 21;
            if (major == 1 && minor == 20 && parts.Length >= 3 && int.TryParse(parts[2], out var build) && build >= 5) return 21;
            if (major == 1 && minor >= 18) return 17;
            if (major == 1 && minor == 17) return 16;
        }

        return 8;
    }
}