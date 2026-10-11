using System.IO;
using System.Text;

namespace Konserva.Utilities;

/// <summary>
/// Path validation to prevent path traversal attacks.
/// Checks that a path lies inside the allowed base directory.
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Checks that the path is inside baseDir and contains no escape sequences.
    /// </summary>
    /// <param name="path">The path to check</param>
    /// <param name="baseDir">The base directory (for example, the application directory or Servers)</param>
    /// <returns>true if the path is safe</returns>
    public static bool IsPathSafe(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullBaseDir = Path.GetFullPath(baseDir);

            // Normalize separators for comparison
            fullPath = fullPath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            fullBaseDir = fullBaseDir.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

            // Remove the trailing slash of baseDir for a correct comparison
            fullBaseDir = fullBaseDir.TrimEnd('/');

            return fullPath.StartsWith(fullBaseDir + "/", StringComparison.OrdinalIgnoreCase) ||
                   fullPath.Equals(fullBaseDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks that the path contains no suspicious escape sequences.
    /// </summary>
    public static bool ContainsTraversalSequences(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Check explicit attempts to escape the directory
        // (we do not block ~ — it is a valid character on Windows, e.g. C:\Users\ADMINI~1)
        return path.Contains("..") ||
               path.StartsWith("\\\\") ||
               path.StartsWith("//");
    }

    /// <summary>
    /// Safely combines baseDir and relativePath, verifying that the result does not escape baseDir.
    /// </summary>
    /// <returns>The full path, or null if relativePath tries to escape baseDir</returns>
    public static string? SafeCombine(string baseDir, string relativePath)
    {
        try
        {
            var combined = Path.Combine(baseDir, relativePath);
            var fullCombined = Path.GetFullPath(combined);
            var fullBaseDir = Path.GetFullPath(baseDir);

            fullCombined = fullCombined.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            fullBaseDir = fullBaseDir.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            fullBaseDir = fullBaseDir.TrimEnd('/');

            if (fullCombined.StartsWith(fullBaseDir + "/", StringComparison.OrdinalIgnoreCase) ||
                fullCombined.Equals(fullBaseDir, StringComparison.OrdinalIgnoreCase))
            {
                return fullCombined;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sanitizes a path for use in batch files — escapes special characters.
    /// Important: with enabledelayedexpansion the ! character must be escaped first, because it gets removed.
    /// </summary>
    public static string SanitizeForBatch(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var sb = new StringBuilder(path.Length + 10);
        foreach (var ch in path)
        {
            sb.Append(ch switch
            {
                '!' => $"^{ch}",                    // With enabledelayedexpansion ! is escaped via ^!
                '&' or '|' or '<' or '>' or '%' or '^' or '(' or ')' => $"^{ch}",  // Escape batch special characters via ^
                _ => ch.ToString()
            });
        }
        return sb.ToString();
    }
}
