using System.Text.RegularExpressions;

namespace JobTracker.Web.Extraction;

public static partial class JobDescriptionText
{
    private const int MaximumLength = 100_000;

    private static readonly string[] DescriptionHeadings =
    [
        "full job description",
        "job description",
        "about the role",
        "about this role",
        "about the job",
        "the opportunity",
        "the role",
        "role overview",
        "position overview",
        "what you'll do",
        "what you’ll do",
        "what you will do",
        "who we are",
        "about us",
    ];

    public static string? Extract(string normalizedSourceText)
    {
        if (string.IsNullOrWhiteSpace(normalizedSourceText))
        {
            return null;
        }

        var text = PastedJobTextExtractor.Normalize(normalizedSourceText);
        var blocks = BlankLine().Split(text)
            .Select(block => block.Trim())
            .Where(block => block.Length > 0)
            .ToList();
        if (blocks.Count == 0)
        {
            return null;
        }

        var metadataBlocks = 0;
        while (metadataBlocks < blocks.Count && IsMetadataBlock(blocks[metadataBlocks]))
        {
            metadataBlocks++;
        }

        if (metadataBlocks > 0 && metadataBlocks < blocks.Count)
        {
            return Limit(string.Join("\n\n", blocks.Skip(metadataBlocks)));
        }

        for (var index = 0; index < blocks.Count; index++)
        {
            var firstLine = blocks[index].Split('\n', 2)[0];
            var heading = NormalizeHeading(firstLine);
            if (!DescriptionHeadings.Contains(heading, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = heading is "full job description" or "job description"
                ? index + 1
                : index;
            return start < blocks.Count
                ? Limit(string.Join("\n\n", blocks.Skip(start)))
                : null;
        }

        return text.Length >= 200 ? Limit(text) : null;
    }

    public static bool IsSectionHeading(string value)
    {
        var heading = NormalizeHeading(value);
        if (DescriptionHeadings.Contains(heading, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return HeadingPrefix().IsMatch(heading) && heading.Length <= 100;
    }

    private static bool IsMetadataBlock(string block)
    {
        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 && lines.All(line => MetadataLine().IsMatch(line.Trim()));
    }

    private static string NormalizeHeading(string value) =>
        value.Trim().TrimEnd(':').Trim().ToLowerInvariant();

    private static string Limit(string value) =>
        value.Length <= MaximumLength ? value : value[..MaximumLength].TrimEnd();

    [GeneratedRegex(@"\n\s*\n", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"^(job title|company|location|work arrangement|source site|job reference|salary|employment type|closing date)\s*:\s*.+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MetadataLine();

    [GeneratedRegex(@"^(about|what|who|why|how|our|your|key|role|position|responsibilities|requirements|qualifications|skills|experience|education|benefits|application|selection|working|life at|join)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPrefix();
}
