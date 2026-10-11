namespace Konserva.Models;

/// <summary>
/// Update channel taken into account when looking for a new mod version.
/// Corresponds to Modrinth's version_type (release / beta / alpha).
/// </summary>
public enum ModUpdateChannel
{
    /// <summary>Only stable releases (test versions are not offered).</summary>
    Release,

    /// <summary>Only beta versions.</summary>
    BetaOnly,

    /// <summary>Only alpha versions.</summary>
    AlphaOnly,

    /// <summary>All versions: releases, betas and alphas.</summary>
    All
}