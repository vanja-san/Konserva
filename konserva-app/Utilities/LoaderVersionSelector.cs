namespace Konserva.Utilities;

/// <summary>
/// Selection rules for the loader update list. Pure functions, extracted from
/// <c>ServerSettingsSection.ApplyVersionFilter</c> so the "only newer versions"
/// rule can be unit-tested without instantiating WPF controls.
/// </summary>
public static class LoaderVersionSelector
{
    /// <summary>
    /// Returns the versions that can be selected for an update: everything newer
    /// than <paramref name="current"/>, or the whole list when the current version
    /// is unknown (a server without a recorded loader version accepts any build).
    /// </summary>
    public static string[] GetSelectableVersions(string[] all, string? current)
    {
        ArgumentNullException.ThrowIfNull(all);

        if (string.IsNullOrEmpty(current))
            return all;

        return [.. all.Where(v => VersionCompare.CompareLoaderVersions(v, current) > 0)];
    }

    /// <summary>
    /// Returns whether the list contains at least one version newer than
    /// <paramref name="current"/>. An unknown current version counts as updatable:
    /// nothing proves the server is already on the latest build.
    /// </summary>
    public static bool HasUpdates(string[] all, string? current)
    {
        ArgumentNullException.ThrowIfNull(all);

        if (string.IsNullOrEmpty(current))
            return all.Length > 0;

        return all.Any(v => VersionCompare.CompareLoaderVersions(v, current) > 0);
    }
}