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
        var parts = mcVersion.Split('.');
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor))
        {
            // MC 26.1+ — требует Java 25 (и все будущие мажорные версии 27+, 28+ и т.д.)
            if (major >= 26) return 25;

            // MC 1.x
            if (major == 1 && minor >= 21) return 21;
            if (major == 1 && minor == 20 && parts.Length >= 3 && int.TryParse(parts[2], out var build) && build >= 5) return 21;
            if (major == 1 && minor >= 18) return 17;
            if (major == 1 && minor == 17) return 16;
        }

        // Forge/NeoForge: для старых версий MC повышаем минимум
        if (launchType is McServerInstaller.ServerLaunchType.Forge or McServerInstaller.ServerLaunchType.NeoForge)
            return 17;

        return 8;
    }

    /// <summary>
    /// Извлекает требуемую версию Java из сообщения об ошибке.
    /// Поддерживает форматы: "Java X+", "class file version XX.0", "Java X",
    /// "Current Java is X but we require at least Y" (Forge bootstrap).
    /// </summary>
    public static int ParseRequiredJavaVersion(string msg)
    {
        // Формат 1: "Требуется Java X+" (наш формат)
        var match = Regex.Match(msg, @"Java (\d+)\+");
        if (match.Success)
            return int.Parse(match.Groups[1].Value);

        // Формат 2: "class file version XX.0" → переводим в версию Java
        var classVersionMatch = Regex.Match(msg, @"compiled by a more recent version.*?class file version (\d+)");
        if (classVersionMatch.Success)
        {
            var classVersion = int.Parse(classVersionMatch.Groups[1].Value);
            return ClassFileVersionToJavaVersion(classVersion);
        }

        // Формат 3: "Current Java is X but we require at least Y" (Forge bootstrap)
        var forgeMatch = Regex.Match(msg, @"require(?:s|d)? at least (\d+)", RegexOptions.IgnoreCase);
        if (forgeMatch.Success)
            return int.Parse(forgeMatch.Groups[1].Value);

        // Формат 4: "Java X" (общий fallback)
        var fallbackMatch = Regex.Match(msg, @"Java (\d+)");
        return fallbackMatch.Success ? int.Parse(fallbackMatch.Groups[1].Value) : 0;
    }

    /// <summary>
    /// Извлекает фактическую версию Java из сообщения об ошибке.
    /// Поддерживает формат: "versions up to XX.0", "найдена Java X", "found Java X",
    /// "Current Java is X but we require at least Y" (Forge bootstrap).
    /// </summary>
    public static int ParseFoundJavaVersion(string msg)
    {
        // Формат 1: "recognizes class file versions up to XX.0"
        var classVersionMatch = Regex.Match(msg, @"versions up to (\d+)");
        if (classVersionMatch.Success)
        {
            var classVersion = int.Parse(classVersionMatch.Groups[1].Value);
            return ClassFileVersionToJavaVersion(classVersion);
        }

        // Формат 2: "Current Java is X but we require at least Y" (Forge bootstrap)
        var forgeMatch = Regex.Match(msg, @"Current Java is (\d+)", RegexOptions.IgnoreCase);
        if (forgeMatch.Success)
            return int.Parse(forgeMatch.Groups[1].Value);

        // Формат 3: "найдена Java X" (наше кастомное сообщение, русский порядок)
        var russianMatch = Regex.Match(msg, @"найдена Java (\d+)");
        if (russianMatch.Success)
            return int.Parse(russianMatch.Groups[1].Value);

        // Формат 4: "found Java X" (английский вариант)
        var englishMatch = Regex.Match(msg, @"found Java (\d+)", RegexOptions.IgnoreCase);
        if (englishMatch.Success)
            return int.Parse(englishMatch.Groups[1].Value);

        return 0;
    }

    /// <summary>
    /// Переводит class file version в версию Java.
    /// </summary>
    public static int ClassFileVersionToJavaVersion(int classVersion) => classVersion switch
    {
        49 => 5,
        50 => 6,
        51 => 7,
        52 => 8,
        53 => 9,
        54 => 10,
        55 => 11,
        56 => 12,
        57 => 13,
        58 => 14,
        59 => 15,
        60 => 16,
        61 => 17,
        62 => 18,
        63 => 19,
        64 => 20,
        65 => 21,
        66 => 22,
        67 => 23,
        68 => 24,
        69 => 25,
        _ => 0
    };

    /// <summary>
    /// Извлекает путь к Java из сообщения об ошибке.
    /// </summary>
    public static string ParseJavaPath(string msg)
    {
        var match = Regex.Match(msg, @"(?:Путь|Path)[:\s]+(.+?)(?:\n|$)");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }
}