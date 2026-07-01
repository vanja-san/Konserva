namespace Konserva.Utilities;

/// <summary>
/// Вспомогательные методы для работы с версиями Minecraft и модлоадеров.
/// </summary>
public static class McVersionHelper
{
  /// <summary>
  /// Разбирает версию Minecraft на major.minor (например "1.20.1" → major=1, minor=20).
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
  /// Определяет, является ли версия снапшотом (pre-release, weekly и т.д.).
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
  /// Определяет, является ли версия NeoForge снапшотом.
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
  /// Определяет, является ли версия Quilt снапшотом.
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
