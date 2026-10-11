namespace Konserva.Utilities;

/// <summary>
/// Helper methods for working with Minecraft and mod loader versions.
/// </summary>
public static class McVersionHelper
{
    // Minecraft versions and snapshots look like 1.21.1, 1.20.4-pre1, 24w14potato, 1.0.
    // The first character must be alphanumeric (so "-help", "--foo" are rejected) and the
    // rest is limited to the characters these names actually use. Spaces, quotes, &, |,
    // ; and = are excluded, which is what stops argument injection.
    private static readonly System.Text.RegularExpressions.Regex McVersionPattern =
        new(@"^[0-9A-Za-z][0-9A-Za-z._\-]*$",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // A JVM main class is a dotted sequence of Java identifiers. Anything else in that
    // position is an attempt to append further arguments to the command line.
    private static readonly System.Text.RegularExpressions.Regex JavaClassNamePattern =
        new(@"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)*$",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns true when <paramref name="version"/> is shaped like a Minecraft version.
    /// <para>
    /// Version strings reach a process command line and an installer URL. An imported
    /// server folder supplies its own <c>version.json</c>, so the value is untrusted:
    /// without this check a value such as <c>1.20.1 --help</c> or one containing a quote
    /// appends extra arguments to the launcher (for example <c>-javaagent:</c>).
    /// </para>
    /// </summary>
    public static bool IsSafeVersionString(string? version) =>
        !string.IsNullOrWhiteSpace(version) &&
        version.Length <= 64 &&
        McVersionPattern.IsMatch(version);

    /// <summary>
    /// Returns true when <paramref name="className"/> is a valid, fully qualified Java class name.
    /// </summary>
    public static bool IsSafeJavaClassName(string? className) =>
        !string.IsNullOrWhiteSpace(className) &&
        className.Length <= 512 &&
        JavaClassNamePattern.IsMatch(className);
    /// <summary>
    /// Splits a Minecraft version into major.minor (for example "1.20.1" → major=1, minor=20).
    /// </summary>
    public static bool TryParseMcVersion(string version, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        try
        {
            var parts = version.Split('.');
            if (parts.Length >= 2)
            {
                major = int.Parse(parts[0]);
                minor = int.Parse(parts[1]);
                return true;
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return false;
    }

    /// <summary>
    /// Determines whether a version is a snapshot (pre-release, weekly, etc.).
    /// </summary>
    public static bool IsSnapshot(string version)
    {
        var snapshotMarkers = new[] { "w", "-pre", "-rc", "-snapshot", "Pre-Release", " pre", "inf" };
        if (snapshotMarkers.Any(m => version.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return true;

        var snapshotPrefixes = new[] { "a", "b", "c", "rd" };
        if (snapshotPrefixes.Any(p => version.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (version.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Determines whether a NeoForge version is a snapshot.
    /// </summary>
    public static bool IsNeoForgeSnapshot(string fullVersion)
    {
        if (string.IsNullOrEmpty(fullVersion))
            return false;

        if (fullVersion.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("-alpha.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("+snapshot", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("+pre", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Determines whether a Quilt version is a snapshot.
    /// </summary>
    public static bool IsQuiltSnapshot(string fullVersion)
    {
        if (string.IsNullOrEmpty(fullVersion))
            return false;

        if (fullVersion.Contains("-beta", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("-pre.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("+build.", StringComparison.OrdinalIgnoreCase))
            return true;

        if (fullVersion.Contains("+local.", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
