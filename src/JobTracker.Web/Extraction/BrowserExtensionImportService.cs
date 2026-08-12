using System.Globalization;
using System.Text.Json;

namespace JobTracker.Web.Extraction;

public sealed record BrowserExtensionCapture(
    int Version,
    string? PageUrl,
    string? RoleTitle,
    string? CompanyName,
    string? CompanyLocation,
    string? WorkplaceMode,
    string? SourceSite,
    string? JobReference,
    string? SalaryText,
    string? EmploymentType,
    string? ClosingDate,
    string? DescriptionText,
    string? SourceText);

public sealed record BrowserExtensionImportResult(
    PastedJobExtraction? Extraction,
    string? Error)
{
    public bool IsSuccess => Extraction is not null;

    public static BrowserExtensionImportResult Failed(string error) => new(null, error);
}

public sealed class BrowserExtensionImportService(PastedJobTextExtractor extractor)
{
    private const int MaxPayloadLength = 120_000;
    private const int MaxSourceTextLength = 60_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 16,
    };

    public BrowserExtensionImportResult Import(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson.Length > MaxPayloadLength)
        {
            return BrowserExtensionImportResult.Failed(
                "The browser capture was empty or too large. Return to the job page and capture it again.");
        }

        BrowserExtensionCapture? capture;
        try
        {
            capture = JsonSerializer.Deserialize<BrowserExtensionCapture>(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return BrowserExtensionImportResult.Failed(
                "The browser capture could not be read. Return to the job page and capture it again.");
        }

        if (capture is null || capture.Version != 1)
        {
            return BrowserExtensionImportResult.Failed(
                "This browser capture version is not supported. Reload the extension and try again.");
        }

        if (!TryPageUrl(capture.PageUrl, out var pageUrl))
        {
            return BrowserExtensionImportResult.Failed(
                "The browser capture did not include a valid HTTP or HTTPS job-page address.");
        }

        var sourceText = Limit(capture.SourceText, MaxSourceTextLength)
            ?? BuildSourceText(capture);
        if (sourceText.Length < 20)
        {
            return BrowserExtensionImportResult.Failed(
                "The current page did not contain enough readable job information. Paste the advertisement or use manual entry instead.");
        }

        var extraction = extractor.Extract(sourceText);
        var capturedClosingDate = ParseClosingDate(capture.ClosingDate);
        var fields = extraction.Fields with
        {
            RoleTitle = Prefer(capture.RoleTitle, extraction.Fields.RoleTitle, 200),
            CompanyName = Prefer(capture.CompanyName, extraction.Fields.CompanyName, 200),
            CompanyLocation = Prefer(capture.CompanyLocation, extraction.Fields.CompanyLocation, 300),
            WorkplaceMode = Prefer(capture.WorkplaceMode, extraction.Fields.WorkplaceMode, 100),
            SourceUrl = pageUrl.AbsoluteUri,
            SourceSite = Prefer(capture.SourceSite, pageUrl.Host, 200),
            JobReference = Prefer(capture.JobReference, extraction.Fields.JobReference, 200),
            SalaryText = Prefer(capture.SalaryText, extraction.Fields.SalaryText, 500),
            EmploymentType = Prefer(capture.EmploymentType, extraction.Fields.EmploymentType, 200),
            ClosingDate = capturedClosingDate ?? extraction.Fields.ClosingDate,
            DescriptionText = Prefer(
                capture.DescriptionText,
                extraction.Fields.DescriptionText,
                MaxSourceTextLength),
        };
        var evidence = new Dictionary<string, string>(extraction.Evidence, StringComparer.Ordinal);
        AddCapturedEvidence(evidence, nameof(fields.RoleTitle), capture.RoleTitle, "job title");
        AddCapturedEvidence(evidence, nameof(fields.CompanyName), capture.CompanyName, "employer");
        AddCapturedEvidence(evidence, nameof(fields.CompanyLocation), capture.CompanyLocation, "location");
        AddCapturedEvidence(evidence, nameof(fields.WorkplaceMode), capture.WorkplaceMode, "work arrangement");
        AddCapturedEvidence(evidence, nameof(fields.SourceSite), capture.SourceSite, "source site");
        AddCapturedEvidence(evidence, nameof(fields.JobReference), capture.JobReference, "job reference");
        AddCapturedEvidence(evidence, nameof(fields.SalaryText), capture.SalaryText, "salary");
        AddCapturedEvidence(evidence, nameof(fields.EmploymentType), capture.EmploymentType, "employment type");
        AddCapturedEvidence(evidence, nameof(fields.DescriptionText), capture.DescriptionText, "job description");
        if (capturedClosingDate is not null)
        {
            AddCapturedEvidence(evidence, nameof(fields.ClosingDate), capture.ClosingDate, "closing date");
        }
        evidence[nameof(fields.SourceUrl)] =
            "High confidence — this is the active page address captured by the browser extension.";

        var warnings = extraction.Warnings
            .Where(warning => !ResolvedMissingWarning(warning, fields))
            .ToList();

        return new BrowserExtensionImportResult(
            new PastedJobExtraction(
                sourceText,
                PastedJobTextExtractor.Normalize(sourceText),
                fields,
                evidence,
                warnings),
            null);
    }

    private static bool TryPageUrl(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(parsed.Host)
            && string.IsNullOrEmpty(parsed.UserInfo)
            && parsed.AbsoluteUri.Length <= 2048)
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string BuildSourceText(BrowserExtensionCapture capture)
    {
        var lines = new List<string>();
        AddLabeledLine(lines, "Job title", capture.RoleTitle);
        AddLabeledLine(lines, "Company", capture.CompanyName);
        AddLabeledLine(lines, "Location", capture.CompanyLocation);
        AddLabeledLine(lines, "Work arrangement", capture.WorkplaceMode);
        AddLabeledLine(lines, "Source site", capture.SourceSite);
        AddLabeledLine(lines, "Job reference", capture.JobReference);
        AddLabeledLine(lines, "Salary", capture.SalaryText);
        AddLabeledLine(lines, "Employment type", capture.EmploymentType);
        AddLabeledLine(lines, "Closing date", capture.ClosingDate);
        return string.Join('\n', lines);
    }

    private static void AddLabeledLine(ICollection<string> lines, string label, string? value)
    {
        var clean = Limit(value, 2_000);
        if (clean is not null)
        {
            lines.Add($"{label}: {clean}");
        }
    }

    private static void AddCapturedEvidence(
        IDictionary<string, string> evidence,
        string field,
        string? value,
        string label)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            evidence[field] =
                $"High confidence — read from the active page's rendered job metadata for {label}.";
        }
    }

    private static bool ResolvedMissingWarning(string warning, ExtractedJobFields fields) =>
        (fields.RoleTitle is not null && warning.StartsWith("No supported job-title", StringComparison.Ordinal))
        || (fields.CompanyName is not null && warning.StartsWith("No supported company", StringComparison.Ordinal))
        || (fields.CompanyLocation is not null && warning.StartsWith("No supported Australian location", StringComparison.Ordinal));

    private static DateOnly? ParseClosingDate(string? value)
    {
        var candidate = Limit(value, 100);
        if (candidate is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                candidate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var timestamp))
        {
            return DateOnly.FromDateTime(timestamp.Date);
        }

        return DateOnly.TryParse(
            candidate,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var date)
            ? date
            : null;
    }

    private static string? Prefer(string? captured, string? fallback, int maxLength) =>
        Limit(captured, maxLength) ?? Limit(fallback, maxLength);

    private static string? Limit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }
}
