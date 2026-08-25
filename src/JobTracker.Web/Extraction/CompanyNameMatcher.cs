using System.Globalization;
using System.Text;

namespace JobTracker.Web.Extraction;

public enum CompanyNameMatchKind
{
    None,
    MeaningfulPhrase,
    Exact,
}

public static class CompanyNameMatcher
{
    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.Ordinal)
    {
        "co",
        "corp",
        "corporation",
        "inc",
        "incorporated",
        "limited",
        "llc",
        "llp",
        "ltd",
        "plc",
        "pty",
    };

    public static CompanyNameMatchKind Match(string? extractedName, string? existingName)
    {
        var extractedTokens = NormalizeTokens(extractedName);
        var existingTokens = NormalizeTokens(existingName);
        if (extractedTokens.Count == 0 || existingTokens.Count == 0)
        {
            return CompanyNameMatchKind.None;
        }

        if (extractedTokens.SequenceEqual(existingTokens, StringComparer.Ordinal))
        {
            return CompanyNameMatchKind.Exact;
        }

        var extractedIsShorter = extractedTokens.Count <= existingTokens.Count;
        var shorter = extractedIsShorter ? extractedTokens : existingTokens;
        var longer = extractedIsShorter ? existingTokens : extractedTokens;
        if (shorter.Sum(token => token.Length) < 6 || !ContainsPhrase(longer, shorter))
        {
            return CompanyNameMatchKind.None;
        }

        return CompanyNameMatchKind.MeaningfulPhrase;
    }

    private static List<string> NormalizeTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : ' ');
        }

        var tokens = builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        while (tokens.Count > 1 && LegalSuffixes.Contains(tokens[^1]))
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        return tokens;
    }

    private static bool ContainsPhrase(
        IReadOnlyList<string> longer,
        IReadOnlyList<string> shorter)
    {
        for (var start = 0; start <= longer.Count - shorter.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < shorter.Count; offset++)
            {
                if (!string.Equals(
                        longer[start + offset],
                        shorter[offset],
                        StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }
}
