using System.Text.RegularExpressions;
using JobTracker.Web.Extraction;

namespace JobTracker.Web.Applications;

public enum JobDescriptionBlockKind
{
    Heading,
    Paragraph,
    List,
}

public sealed record JobDescriptionBlock(
    JobDescriptionBlockKind Kind,
    IReadOnlyList<string> Lines);

public static partial class JobDescriptionDisplay
{
    public static IReadOnlyList<JobDescriptionBlock> Format(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return [];
        }

        var blocks = new List<JobDescriptionBlock>();
        foreach (var rawBlock in BlankLine().Split(PastedJobTextExtractor.Normalize(description)))
        {
            var lines = rawBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
            if (lines.Count == 0)
            {
                continue;
            }

            if (JobDescriptionText.IsSectionHeading(lines[0]))
            {
                blocks.Add(new JobDescriptionBlock(
                    JobDescriptionBlockKind.Heading,
                    [lines[0].TrimEnd(':')]));
                lines.RemoveAt(0);
            }

            if (lines.Count == 0)
            {
                continue;
            }

            if (lines.All(line => BulletLine().IsMatch(line)))
            {
                blocks.Add(new JobDescriptionBlock(
                    JobDescriptionBlockKind.List,
                    lines.Select(line => BulletLine().Replace(line, string.Empty).Trim()).ToList()));
                continue;
            }

            blocks.Add(new JobDescriptionBlock(
                JobDescriptionBlockKind.Paragraph,
                [string.Join(" ", lines)]));
        }

        return blocks;
    }

    public static string Preview(string description, int maximumLength = 240)
    {
        var text = string.Join(
            " ",
            Format(description)
                .Where(block => block.Kind != JobDescriptionBlockKind.Heading)
                .SelectMany(block => block.Lines));
        if (text.Length <= maximumLength)
        {
            return text;
        }

        var shortened = text[..maximumLength].TrimEnd();
        var lastSpace = shortened.LastIndexOf(' ');
        return $"{(lastSpace > maximumLength / 2 ? shortened[..lastSpace] : shortened)}…";
    }

    [GeneratedRegex(@"\n\s*\n", RegexOptions.CultureInvariant)]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[.)])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex BulletLine();
}
