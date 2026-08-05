using System.Globalization;
using System.Text.RegularExpressions;

namespace JobTracker.Web.Extraction;

public sealed record ExtractedJobFields(
    string? RoleTitle,
    string? CompanyName,
    string? CompanyLocation,
    string? WorkplaceMode,
    string? SourceUrl,
    string? SourceSite,
    string? JobReference,
    string? SalaryText,
    string? EmploymentType,
    DateOnly? ClosingDate,
    string? ContactName,
    string? ContactEmail,
    DateOnly? AppliedOn);

public sealed record PastedJobExtraction(
    string OriginalText,
    string NormalizedText,
    ExtractedJobFields Fields,
    IReadOnlyDictionary<string, string> Evidence,
    IReadOnlyList<string> Warnings);

public sealed partial class PastedJobTextExtractor
{
    private static readonly string[] SupportedDateFormats =
    [
        "yyyy-MM-dd",
        "d/M/yyyy",
        "dd/MM/yyyy",
        "d MMM yyyy",
        "dd MMM yyyy",
        "d MMMM yyyy",
        "dd MMMM yyyy",
        "MMM d yyyy",
        "MMMM d yyyy",
        "MMM d, yyyy",
        "MMMM d, yyyy",
        "dddd, d MMMM yyyy",
        "dddd d MMMM yyyy",
        "dddd, MMMM d, yyyy",
    ];

    private static readonly LabelRule[] Rules =
    [
        new("RoleTitle", "job title", "position title", "role title", "position", "role", "title"),
        new("CompanyName", "company name", "organisation", "organization", "employer", "company"),
        new("CompanyLocation", "job location", "office location", "location"),
        new("WorkplaceMode", "work arrangement", "working arrangement", "workplace", "work mode", "location type"),
        new("SourceUrl", "job posting url", "posting url", "job url", "source url"),
        new("SourceSite", "source site", "job board", "source"),
        new("JobReference", "requisition number", "requisition id", "job reference", "reference number", "job number", "position number", "job id", "reference"),
        new("SalaryText", "salary range", "pay range", "compensation", "remuneration", "salary", "pay"),
        new("EmploymentType", "employment type", "contract type", "work type", "job type"),
        new("ClosingDate", "applications close", "application closing date", "closing date", "close date", "apply by"),
        new("ContactName", "hiring manager", "recruiter", "contact name", "contact person", "contact"),
        new("ContactEmail", "contact email", "recruiter email", "email"),
        new("AppliedOn", "application date", "date applied", "applied on"),
    ];

    public PastedJobExtraction Extract(string sourceText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceText);

        var normalized = Normalize(sourceText);
        var lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (var line in lines)
        {
            foreach (var rule in Rules)
            {
                if (values.ContainsKey(rule.Field))
                {
                    continue;
                }

                var match = rule.Pattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var value = CleanValue(match.Groups["value"].Value);
                if (string.IsNullOrWhiteSpace(value)
                    || !JobBoardLayoutHeuristics.IsPlausibleFieldValue(rule.Field, value))
                {
                    continue;
                }

                values[rule.Field] = value;
                evidence[rule.Field] =
                    $"High confidence — matched the explicit ‘{match.Groups["label"].Value}’ label.";
            }
        }

        JobBoardLayoutHeuristics.Enrich(normalized, lines, values, evidence, warnings);

        if (values.TryGetValue("ContactName", out var contactValue)
            && EmailPattern().IsMatch(contactValue))
        {
            values.Remove("ContactName");
            evidence.Remove("ContactName");
            if (!values.ContainsKey("ContactEmail"))
            {
                values["ContactEmail"] = contactValue;
                evidence["ContactEmail"] =
                    "High confidence — the explicit contact value was an email address.";
            }
        }

        if (!values.ContainsKey("ContactEmail"))
        {
            var emailMatch = EmailPattern().Match(normalized);
            if (emailMatch.Success)
            {
                values["ContactEmail"] = emailMatch.Value;
                evidence["ContactEmail"] =
                    "High confidence — found an explicit email address in the pasted text.";
            }
        }

        var closingDate = ParseDate(values, evidence, "ClosingDate", "closing date", warnings);
        var appliedOn = ParseDate(values, evidence, "AppliedOn", "application date", warnings);
        var sourceUrl = ValidateUrl(values, evidence, warnings);

        AddMissingWarning(values, "RoleTitle", "No supported job-title label or corroborated header pattern was found. Enter the role title before saving.", warnings);
        AddMissingWarning(values, "CompanyName", "No supported company label, header position, or organisation introduction was found. You can enter a company during review.", warnings);
        AddMissingWarning(values, "CompanyLocation", "No supported Australian location label or header pattern was found. The location was left blank.", warnings);

        return new PastedJobExtraction(
            sourceText,
            normalized,
            new ExtractedJobFields(
                Value(values, "RoleTitle"),
                Value(values, "CompanyName"),
                Value(values, "CompanyLocation"),
                Value(values, "WorkplaceMode"),
                sourceUrl,
                Value(values, "SourceSite"),
                Value(values, "JobReference"),
                Value(values, "SalaryText"),
                Value(values, "EmploymentType"),
                closingDate,
                Value(values, "ContactName"),
                Value(values, "ContactEmail"),
                appliedOn),
            evidence,
            warnings);
    }

    public static string Normalize(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);

        var normalizedNewlines = sourceText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace('\u00a0', ' ');
        var output = new List<string>();
        var previousWasBlank = true;

        foreach (var rawLine in normalizedNewlines.Split('\n'))
        {
            var line = HorizontalWhitespace().Replace(rawLine, " ").Trim();
            var isBlank = line.Length == 0;
            if (isBlank && previousWasBlank)
            {
                continue;
            }

            output.Add(line);
            previousWasBlank = isBlank;
        }

        while (output.Count > 0 && output[^1].Length == 0)
        {
            output.RemoveAt(output.Count - 1);
        }

        return string.Join('\n', output);
    }

    private static DateOnly? ParseDate(
        IReadOnlyDictionary<string, string> values,
        IDictionary<string, string> evidence,
        string field,
        string displayName,
        ICollection<string> warnings)
    {
        var value = Value(values, field);
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(
                value,
                SupportedDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            return parsed;
        }

        if (!Regex.IsMatch(value, @"\b\d{4}\b", RegexOptions.CultureInvariant)
            && Regex.IsMatch(
                value,
                @"(?:\b\d{1,2}\s+[A-Za-z]{3,9}\b|\b[A-Za-z]{3,9}\s+\d{1,2}\b)",
                RegexOptions.CultureInvariant))
        {
            evidence.Remove(field);
            warnings.Add(
                $"The posting states a {displayName} day and month but no year, so it was left blank for you to confirm.");
            return null;
        }

        evidence.Remove(field);
        warnings.Add($"The explicit {displayName} ‘{value}’ was not understood, so it was left blank.");
        return null;
    }

    private static string? ValidateUrl(
        IReadOnlyDictionary<string, string> values,
        IDictionary<string, string> evidence,
        ICollection<string> warnings)
    {
        var value = Value(values, "SourceUrl");
        if (value is null)
        {
            return null;
        }

        if (value.Length <= 2048
            && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return value;
        }

        evidence.Remove("SourceUrl");
        warnings.Add("The labeled posting URL was invalid, so it was left blank.");
        return null;
    }

    private static void AddMissingWarning(
        IReadOnlyDictionary<string, string> values,
        string field,
        string warning,
        ICollection<string> warnings)
    {
        if (!values.ContainsKey(field))
        {
            warnings.Add(warning);
        }
    }

    private static string? Value(IReadOnlyDictionary<string, string> values, string field) =>
        values.TryGetValue(field, out var value) ? value : null;

    private static string CleanValue(string value) =>
        value.Trim().TrimEnd('.', ';').Trim();

    [GeneratedRegex(@"[\t\v\f ]+")]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@"(?<![\w.+-])[\w.!#$%&'*+/=?^`{|}~-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+(?![\w.-])", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    private sealed class LabelRule
    {
        public LabelRule(string field, params string[] labels)
        {
            Field = field;
            var alternatives = string.Join('|', labels.Select(Regex.Escape));
            Pattern = new Regex(
                $@"^\s*(?<label>{alternatives})\s*(?::|[-–—])\s*(?<value>.+?)\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public string Field { get; }

        public Regex Pattern { get; }
    }
}
