using System.IO;
using System.Text;

namespace Konserva.Utilities;

/// <summary>
/// Валидация путей для предотвращения path traversal атак.
/// Проверяет что путь находится внутри разрешённой базовой директории.
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Проверяет что путь находится внутри baseDir и не содержит escape-последовательностей.
    /// </summary>
    /// <param name="path">Путь для проверки</param>
    /// <param name="baseDir">Базовая директория (например, каталог приложения или Servers)</param>
    /// <returns>true если путь безопасен</returns>
    public static bool IsPathSafe(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullBaseDir = Path.GetFullPath(baseDir);

            // Нормализуем разделители для сравнения
            fullPath = fullPath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            fullBaseDir = fullBaseDir.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

            // Убираем trailing slash у baseDir для корректного сравнения
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
    /// Проверяет что путь не содержит подозрительных escape-последовательностей.
    /// </summary>
    public static bool ContainsTraversalSequences(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Проверяем явные попытки выхода за пределы директории
        return path.Contains("..") ||
               path.Contains("~") ||
               path.StartsWith("\\\\") ||
               path.StartsWith("//");
    }

    /// <summary>
    /// Безопасно комбинирует baseDir и relativePath, проверяя что результат не выходит за пределы baseDir.
    /// </summary>
    /// <returns>Полный путь или null если relativePath пытается выйти за пределы baseDir</returns>
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
    /// Санитизирует путь для использования в batch-файлах — экранирует специальные символы.
    /// Важно: при enabledelayedexpansion символ ! нужно экранировать первым, т.к. он удаляется.
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
                '!' => $"^{ch}",                    // При enabledelayedexpansion ! экранируется через ^!
                '&' or '|' or '<' or '>' or '%' or '^' or '(' or ')' => $"^{ch}",  // Экранируем спецсимволы batch через ^
                _ => ch.ToString()
            });
        }
        return sb.ToString();
    }
}
