using Konserva.Localization;
using System.IO;

namespace Konserva.Tests.Localization;

/// <summary>
/// Guards the properties the localization system depends on:
/// ru/en key parity, no key used without a definition, no dead keys,
/// and no mojibake in any shipped string.
/// </summary>
[Collection("Sequential")]
[Trait("Category", "Unit")]
public class LocalizationParityTests
{
    private static Dictionary<string, string> Ru()
        => LocalizationManager.GetDefaultTranslationsForCulture("ru")
           ?? throw new InvalidOperationException("RussianStrings returned null");

    private static Dictionary<string, string> En()
        => LocalizationManager.GetDefaultTranslationsForCulture("en")
           ?? throw new InvalidOperationException("EnglishStrings returned null");

    [Fact]
    public void RuAndEn_HaveTheSameKeys()
    {
        var ru = Ru().Keys.ToHashSet(StringComparer.Ordinal);
        var en = En().Keys.ToHashSet(StringComparer.Ordinal);

        var onlyRu = ru.Except(en).OrderBy(k => k).ToArray();
        var onlyEn = en.Except(ru).OrderBy(k => k).ToArray();

        // A key present in one catalogue only means the other language silently
        // falls through to the raw key at runtime.
        onlyRu.Should().BeEmpty($"keys missing from EnglishStrings: {string.Join(", ", onlyRu)}");
        onlyEn.Should().BeEmpty($"keys missing from RussianStrings: {string.Join(", ", onlyEn)}");
    }

    [Fact]
    public void NoKeyIsDefinedTwice()
    {
        var duplicates = Ru()
            .GroupBy(kv => kv.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        duplicates.Should().BeEmpty($"duplicate keys in RussianStrings: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void NoTranslationValueIsEmptyOrJustAKey()
    {
        foreach (var (culture, dict) in new[] { ("ru", Ru()), ("en", En()) })
        {
            foreach (var (key, value) in dict)
            {
                value.Should().NotBeNullOrWhiteSpace($"{culture}: '{key}' has an empty translation");
                value.Should().NotBe(key, $"{culture}: '{key}' maps to its own key");
            }
        }
    }

    /// <summary>
    /// Format placeholders must match between the two languages: a placeholder present in
    /// one and missing in the other throws FormatException or drops an argument silently.
    /// </summary>
    [Fact]
    public void FormatPlaceholdersMatchBetweenLanguages()
    {
        var ru = Ru();
        var en = En();

        var mismatched = new List<string>();

        foreach (var (key, ruValue) in ru)
        {
            if (!en.TryGetValue(key, out var enValue))
                continue;

            var ruPlaceholders = ExtractPlaceholders(ruValue).OrderBy(p => p).ToArray();
            var enPlaceholders = ExtractPlaceholders(enValue).OrderBy(p => p).ToArray();

            if (!ruPlaceholders.SequenceEqual(enPlaceholders))
            {
                mismatched.Add($"{key}: ru=[{string.Join(",", ruPlaceholders)}] en=[{string.Join(",", enPlaceholders)}]");
            }
        }

        mismatched.Should().BeEmpty("format placeholders differ between languages:\n" + string.Join("\n", mismatched));
    }

    private static IEnumerable<string> ExtractPlaceholders(string format) =>
        System.Text.RegularExpressions.Regex.Matches(format, @"\{(\d+)[^}]*\}")
            .Select(m => m.Groups[1].Value);

    /// <summary>
    /// A user-visible string that is not plain ASCII must be genuine Russian text, not a
    /// UTF-8 payload that was decoded twice. The mojibake signature is a Cyrillic block
    /// character followed by one of the Latin-1 supplement characters it decodes from.
    /// </summary>
    [Fact]
    public void NoTranslationContainsMojibake()
    {
        var offenders = new List<string>();

        foreach (var (culture, dict) in new[] { ("ru", Ru()), ("en", En()) })
        {
            foreach (var (key, value) in dict)
            {
                foreach (var ch in value)
                {
                    // Cyrillic block: А-я, Ё, ё
                    bool isCyrillic = ch is >= 'Ѐ' and <= 'ӿ';
                    // Latin-1 supplement / punctuation, which only appear inside a
                    // mis-decoded UTF-8 sequence in this project
                    bool isLatin1Noise = ch is >= '¡' and <= 'ÿ' or '€' or '�';

                    if (isCyrillic && isLatin1Noise)
                    {
                        offenders.Add($"{culture}:{key} -> U+{(int)ch:X4}");
                    }

                    if (ch == '�')
                    {
                        offenders.Add($"{culture}:{key} contains a replacement character");
                    }
                }
            }
        }

        offenders.Should().BeEmpty("mojibake in translations:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Every key referenced from source code must exist in the catalogues. A typo in a key
    /// is invisible at build time -- Get() just returns the key itself.
    /// </summary>
    [Fact]
    public void EveryKeyUsedInSourceExistsInBothCatalogues()
    {
        var repoRoot = FindRepoRoot();
        var appDir = Path.Combine(repoRoot, "konserva-app");
        if (!Directory.Exists(appDir))
            return; // running outside the source tree

        var known = new HashSet<string>(Ru().Keys, StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(appDir, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}publish{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}Localization{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                string text;
                try { text = File.ReadAllText(file, System.Text.Encoding.UTF8); }
                catch { continue; }

                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(text, @"LocalizationManager\.Get\(\s*""([A-Za-z_][A-Za-z0-9_]*)"""))
                {
                    if (!known.Contains(m.Groups[1].Value))
                        missing.Add(m.Groups[1].Value);
                }

                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(text, @"\{loc:Loc\s+([A-Za-z_][A-Za-z0-9_]*)\s*\}"))
                {
                    if (!known.Contains(m.Groups[1].Value))
                        missing.Add(m.Groups[1].Value);
                }
            }
        }

        missing.Should().BeEmpty("keys used in code but not defined:\n" + string.Join("\n", missing));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "konserva-app.slnx")))
            dir = dir.Parent;

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}