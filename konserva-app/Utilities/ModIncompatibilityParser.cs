using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Konserva.Utilities;

public enum RequirementKind
{
    Any,
    AtLeast,
    AtMost,
    Exactly,
    Range,
    AnyMajor,
    Unknown
}

/// <summary>
/// Требование к версии зависимости (из вывода Fabric: "version 0.160.0 or later" и т.п.)
/// </summary>
public sealed record DependencyRequirement(
    RequirementKind Kind,
    string? Version,
    string? VersionTo,
    string? Raw);

public enum SuggestionKind
{
    Install,
    Remove,
    Replace,
    Unknown
}

/// <summary>
/// Рекомендованное действие из блока "A potential solution has been determined"
/// </summary>
public sealed record DependencySuggestion(
    SuggestionKind Kind,
    string Name,
    string? Id,
    string? OldVersion,
    string? NewVersion,
    DependencyRequirement? Requirement,
    string Raw);

public enum IssueKind
{
    MissingDependency,
    WrongVersion,
    ConflictsWith,
    DependsOn,
    Unknown
}

/// <summary>
/// Структурированное описание проблемы мода из блока "More details"
/// </summary>
public sealed record ModIssue(
    IssueKind Kind,
    string ModName,
    string ModId,
    string ModVersion,
    string? DependencyName,
    string? DependencyId,
    DependencyRequirement? Requirement,
    string? PresentVersion,
    string Raw);

/// <summary>
/// Структурированные данные об ошибке несовместимости модов (Fabric "Incompatible mods found!" и т.п.)
/// </summary>
public sealed record ModIncompatibilityInfo(
    string Header,
    IReadOnlyList<DependencySuggestion> Solutions,
    IReadOnlyList<ModIssue> Issues,
    IReadOnlyList<string> RawSolutions,
    IReadOnlyList<string> RawDetails);

/// <summary>
/// Парсер ошибок несовместимости модов из вывода сервера.
/// Поддерживает формат Fabric Loader (FormattedException): блок решений
/// ("Install X, version ...", "Replace ...", "Remove ...") и блок подробностей
/// ("Mod 'X' (id) v requires ...").
/// </summary>
public static partial class ModIncompatibilityParser
{
    /// <summary>
    /// Признак того, что текст содержит известный паттерн несовместимости модов
    /// </summary>
    public static bool IsModIncompatibility(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Contains("Incompatible mods found", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("Some of your mods are incompatible with the game or each other", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Пытается извлечь структурированные данные об ошибке несовместимости модов.
    /// Возвращает null, если это не ошибка несовместимости модов.
    /// </summary>
    public static ModIncompatibilityInfo? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsModIncompatibility(text))
            return null;

        var lines = text
            .Split(Constants.NewLineChars, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        string? header = null;
        var solutions = new List<DependencySuggestion>();
        var rawSolutions = new List<string>();
        var issues = new List<ModIssue>();
        var rawDetails = new List<string>();
        var section = 0; // 0 = до блока, 1 = решения, 2 = подробности

        foreach (var line in lines)
        {
            // Стек вызовов — конец блока
            if (line.StartsWith("at ", System.StringComparison.Ordinal))
                break;

            // Заголовок блока
            if (header == null &&
                (line.Contains("Incompatible mods found", System.StringComparison.OrdinalIgnoreCase)
                 || line.Contains("Some of your mods are incompatible with the game or each other", System.StringComparison.OrdinalIgnoreCase)))
            {
                header = line;
                continue;
            }

            if (line.Contains("A potential solution has been determined", System.StringComparison.OrdinalIgnoreCase))
            {
                section = 1;
                continue;
            }

            if (line.Contains("More details", System.StringComparison.OrdinalIgnoreCase))
            {
                section = 2;
                continue;
            }

            if (section == 0)
                continue;

            if (!line.StartsWith("- ", System.StringComparison.Ordinal))
                continue;

            var bullet = line[2..].Trim();
            if (bullet.Length == 0)
                continue;

            if (section == 1)
            {
                var suggestion = TryParseSuggestion(bullet);
                if (suggestion != null)
                    solutions.Add(suggestion);
                else
                    rawSolutions.Add(bullet);
            }
            else
            {
                var issue = TryParseIssue(bullet);
                if (issue != null)
                    issues.Add(issue);
                else
                    rawDetails.Add(bullet);
            }
        }

        return new ModIncompatibilityInfo(
            header ?? lines.FirstOrDefault() ?? "",
            solutions,
            issues,
            rawSolutions,
            rawDetails);
    }

    private static DependencySuggestion? TryParseSuggestion(string bullet)
    {
        var install = InstallRegex().Match(bullet);
        if (install.Success)
        {
            var requirement = ParseRequirement(install.Groups["req"].Value.Trim());
            return new DependencySuggestion(
                SuggestionKind.Install,
                install.Groups["dep"].Value,
                install.Groups["dep"].Value,
                null,
                null,
                requirement,
                bullet);
        }

        var remove = RemoveRegex().Match(bullet);
        if (remove.Success)
        {
            return new DependencySuggestion(
                SuggestionKind.Remove,
                remove.Groups["dep"].Value,
                remove.Groups["dep"].Value,
                null,
                null,
                null,
                bullet);
        }

        var replace = ReplaceRegex().Match(bullet);
        if (replace.Success)
        {
            return new DependencySuggestion(
                SuggestionKind.Replace,
                replace.Groups["name"].Value,
                replace.Groups["id"].Value,
                replace.Groups["old"].Value,
                replace.Groups["new"].Value,
                null,
                bullet);
        }

        return null;
    }

    private static ModIssue? TryParseIssue(string bullet)
    {
        var detail = DetailRegex().Match(bullet);
        if (!detail.Success)
            return null;

        var modName = detail.Groups["modName"].Value;
        var modId = detail.Groups["modId"].Value;
        var modVersion = detail.Groups["modVer"].Value;
        var rest = detail.Groups["rest"].Value.Trim().TrimEnd('!');

        if (rest.StartsWith("conflicts with ", System.StringComparison.OrdinalIgnoreCase))
        {
            var dep = ParseDependency(rest["conflicts with ".Length..]);
            return new ModIssue(
                IssueKind.ConflictsWith, modName, modId, modVersion,
                dep.Name, dep.Id, null, null, bullet);
        }

        if (rest.StartsWith("depends on ", System.StringComparison.OrdinalIgnoreCase))
        {
            var depText = rest["depends on ".Length..];
            var comma = depText.IndexOf(", which is missing", System.StringComparison.OrdinalIgnoreCase);
            if (comma >= 0)
                depText = depText[..comma];
            var dep = ParseDependency(depText.Trim());
            return new ModIssue(
                IssueKind.DependsOn, modName, modId, modVersion,
                dep.Name, dep.Id, null, null, bullet);
        }

        if (rest.StartsWith("requires ", System.StringComparison.OrdinalIgnoreCase))
        {
            var reqText = rest["requires ".Length..];
            var reqMatch = RequirementOfRegex().Match(reqText);
            if (!reqMatch.Success)
            {
                return new ModIssue(IssueKind.Unknown, modName, modId, modVersion, null, null, null, null, bullet);
            }

            var requirement = ParseRequirement(reqMatch.Groups["req"].Value.Trim());
            var dep = ParseDependency(reqMatch.Groups["dep"].Value.Trim());
            var why = reqMatch.Groups["why"].Value.Trim().TrimEnd('!');

            if (why.Contains("which is missing", System.StringComparison.OrdinalIgnoreCase))
            {
                return new ModIssue(
                    IssueKind.MissingDependency, modName, modId, modVersion,
                    dep.Name, dep.Id, requirement, null, bullet);
            }

            var wrongVersion = WrongVersionRegex().Match(why);
            if (wrongVersion.Success)
            {
                return new ModIssue(
                    IssueKind.WrongVersion, modName, modId, modVersion,
                    dep.Name, dep.Id, requirement, wrongVersion.Groups["present"].Value, bullet);
            }
        }

        return new ModIssue(IssueKind.Unknown, modName, modId, modVersion, null, null, null, null, bullet);
    }

    private static (string Name, string Id) ParseDependency(string text)
    {
        var quoted = QuotedDependencyRegex().Match(text);
        if (quoted.Success)
            return (quoted.Groups["name"].Value, quoted.Groups["id"].Value);

        var word = text.Trim().Split(' ')[0].Trim();
        return (word, word);
    }

    /// <summary>
    /// Разбор текста требования к версии ("version 0.160.0 or later", "any version",
    /// "any version between 26.2 (inclusive) and 26.3- (exclusive)" и т.п.).
    /// Если формат не распознан — возвращает Unrecognized-требование с исходным текстом.
    /// </summary>
    public static DependencyRequirement ParseVersionRequirement(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new DependencyRequirement(RequirementKind.Unknown, null, null, text);

        return ParseRequirement(text);
    }

    private static DependencyRequirement ParseRequirement(string text)
    {
        text = text.Trim();

        if (string.Equals(text, "any version", System.StringComparison.OrdinalIgnoreCase))
            return new DependencyRequirement(RequirementKind.Any, null, null, text);

        var atLeast = AtLeastRegex().Match(text);
        if (atLeast.Success)
            return new DependencyRequirement(RequirementKind.AtLeast, atLeast.Groups["v"].Value, null, text);

        var atMost = AtMostRegex().Match(text);
        if (atMost.Success)
            return new DependencyRequirement(RequirementKind.AtMost, atMost.Groups["v"].Value, null, text);

        var anyMajor = AnyMajorRegex().Match(text);
        if (anyMajor.Success)
            return new DependencyRequirement(RequirementKind.AnyMajor, anyMajor.Groups["v"].Value, null, text);

        var range = RangeRegex().Match(text);
        if (range.Success)
            return new DependencyRequirement(RequirementKind.Range, range.Groups["from"].Value, range.Groups["to"].Value, text);

        var exact = ExactlyRegex().Match(text);
        if (exact.Success)
            return new DependencyRequirement(RequirementKind.Exactly, exact.Groups["v"].Value, null, text);

        return new DependencyRequirement(RequirementKind.Unknown, null, null, text);
    }

    // "Install fabric-api, version 0.160.0 or later."
    [GeneratedRegex(@"^Install\s+(?<dep>\S+?),\s*(?<req>.+?)\s*\.\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex InstallRegex();

    // "Remove some-mod."
    [GeneratedRegex(@"^Remove\s+(?<dep>\S+?)\s*\.\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveRegex();

    // "Replace 'Minecraft' (minecraft) 26.3 with 26.2."
    [GeneratedRegex(@"^Replace\s+'(?<name>.*?)'\s*\((?<id>[^)]+)\)\s+(?<old>\S+)\s+with\s+(?<new>.+?)\s*\.\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReplaceRegex();

    // "Mod 'Architectury' (architectury) 21.1.9 requires version 0.160.0 or later of fabric-api, which is missing!"
    [GeneratedRegex(@"^Mod\s+'(?<modName>.*?)'\s*\((?<modId>[^)]+)\)\s+(?<modVer>\S+)\s+(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRegex();

    // "version 0.160.0 or later of fabric-api, which is missing!"
    [GeneratedRegex(@"^(?<req>.*?)\s+of\s+(?<dep>.*?),\s+(?<why>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RequirementOfRegex();

    // "wrong version is present: 26.3"
    [GeneratedRegex(@"wrong version is present:\s*(?<present>\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex WrongVersionRegex();

    // "a specific version of 'Minecraft' (minecraft) (1.16.5)" или "'Minecraft' (minecraft)"
    [GeneratedRegex(@"'(?<name>.*?)'\s*\((?<id>[^)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedDependencyRegex();

    [GeneratedRegex(@"^version\s+(?<v>\S+)\s+or\s+later$", RegexOptions.IgnoreCase)]
    private static partial Regex AtLeastRegex();

    [GeneratedRegex(@"^version\s+(?<v>\S+)\s+or\s+earlier$", RegexOptions.IgnoreCase)]
    private static partial Regex AtMostRegex();

    [GeneratedRegex(@"^any\s+(?<v>\S+?)\.x\s+version$", RegexOptions.IgnoreCase)]
    private static partial Regex AnyMajorRegex();

    [GeneratedRegex(@"^any\s+version\s+(?:which\s+is\s+)?between\s+(?<from>\S+)\s+\(inclusive\)\s+and\s+(?<to>\S+)\s+\(exclusive\)$", RegexOptions.IgnoreCase)]
    private static partial Regex RangeRegex();

    [GeneratedRegex(@"^version\s+(?<v>\S+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExactlyRegex();
}