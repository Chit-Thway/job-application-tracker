using System.Globalization;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace JobTracker.Web.Extraction;

public sealed class JobPostingHtmlExtractor(PastedJobTextExtractor textExtractor)
{
    private const int MaximumReviewTextLength = 100_000;
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64,
    };

    public PastedJobExtraction Extract(Uri requestedUri, Uri finalUri, string html)
    {
        ArgumentNullException.ThrowIfNull(requestedUri);
        ArgumentNullException.ThrowIfNull(finalUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        var document = new HtmlParser().ParseDocument(html);
        var structured = ExtractStructuredData(document);
        var semantic = ExtractSemanticJobData(document);
        var metadataTitle = MetaContent(document, "og:title")
            ?? MetaContent(document, "twitter:title")
            ?? document.Title;
        var metadataDescription = MetaContent(document, "og:description")
            ?? MetaContent(document, "description");
        var visibleText = ExtractVisibleText(document);
        var sourceParts = semantic.HasJobDetails
            ? new[]
            {
                semantic.RoleTitle,
                semantic.CompanyName,
                semantic.CompanyLocation,
                semantic.Classification,
                semantic.EmploymentType,
                semantic.SalaryText,
                metadataDescription,
                semantic.Description,
                structured.Description,
            }
            : new[]
            {
                metadataTitle,
                metadataDescription,
                visibleText,
                structured.Description,
            };
        var sourceText = PastedJobTextExtractor.Normalize(string.Join(
            "\n\n",
            sourceParts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)));
        if (sourceText.Length == 0)
        {
            throw new JobPostingUrlException(
                JobPostingImportFailure.EmptyContent,
                "The job page did not contain readable text.");
        }

        var wasShortened = sourceText.Length > MaximumReviewTextLength;
        if (wasShortened)
        {
            sourceText = sourceText[..MaximumReviewTextLength].TrimEnd();
        }

        var textExtraction = textExtractor.Extract(sourceText);
        var sourceSite = MetaContent(document, "og:site_name")
            ?? FriendlyHostName(finalUri.Host);
        var fields = textExtraction.Fields with
        {
            RoleTitle = structured.RoleTitle ?? semantic.RoleTitle ?? textExtraction.Fields.RoleTitle,
            CompanyName = structured.CompanyName ?? semantic.CompanyName ?? textExtraction.Fields.CompanyName,
            CompanyLocation = structured.CompanyLocation
                ?? semantic.CompanyLocation
                ?? textExtraction.Fields.CompanyLocation,
            WorkplaceMode = structured.WorkplaceMode ?? textExtraction.Fields.WorkplaceMode,
            SourceUrl = requestedUri.AbsoluteUri,
            SourceSite = sourceSite,
            JobReference = structured.JobReference ?? textExtraction.Fields.JobReference,
            SalaryText = structured.SalaryText ?? semantic.SalaryText ?? textExtraction.Fields.SalaryText,
            EmploymentType = structured.EmploymentType
                ?? semantic.EmploymentType
                ?? textExtraction.Fields.EmploymentType,
            ClosingDate = structured.ClosingDate ?? textExtraction.Fields.ClosingDate,
            ContactName = structured.ContactName ?? textExtraction.Fields.ContactName,
            ContactEmail = structured.ContactEmail ?? textExtraction.Fields.ContactEmail,
            DescriptionText = structured.Description
                ?? semantic.Description
                ?? textExtraction.Fields.DescriptionText,
        };

        var evidence = new Dictionary<string, string>(textExtraction.Evidence, StringComparer.Ordinal);
        AddSemanticEvidence(evidence, "RoleTitle", semantic.RoleTitle, "job title");
        AddSemanticEvidence(evidence, "CompanyName", semantic.CompanyName, "employer");
        AddSemanticEvidence(evidence, "CompanyLocation", semantic.CompanyLocation, "location");
        AddSemanticEvidence(evidence, "SalaryText", semantic.SalaryText, "salary");
        AddSemanticEvidence(evidence, "EmploymentType", semantic.EmploymentType, "work type");
        AddSemanticEvidence(evidence, "DescriptionText", semantic.Description, "job description");
        AddStructuredEvidence(evidence, "RoleTitle", structured.RoleTitle, "job title");
        AddStructuredEvidence(evidence, "CompanyName", structured.CompanyName, "hiring organisation");
        AddStructuredEvidence(evidence, "CompanyLocation", structured.CompanyLocation, "job location");
        AddStructuredEvidence(evidence, "WorkplaceMode", structured.WorkplaceMode, "work arrangement");
        AddStructuredEvidence(evidence, "JobReference", structured.JobReference, "job identifier");
        AddStructuredEvidence(evidence, "SalaryText", structured.SalaryText, "base salary");
        AddStructuredEvidence(evidence, "EmploymentType", structured.EmploymentType, "employment type");
        AddStructuredEvidence(evidence, "ClosingDate", structured.ClosingDate, "closing date");
        AddStructuredEvidence(evidence, "ContactName", structured.ContactName, "application contact");
        AddStructuredEvidence(evidence, "ContactEmail", structured.ContactEmail, "application contact email");
        AddStructuredEvidence(evidence, "DescriptionText", structured.Description, "description");
        evidence["SourceUrl"] = "High confidence — this is the public URL you asked the importer to fetch.";
        evidence["SourceSite"] = MetaContent(document, "og:site_name") is not null
            ? "High confidence — read from the page’s site-name metadata."
            : "Medium confidence — derived from the final public page hostname.";

        var warnings = textExtraction.Warnings.ToList();
        RemoveResolvedWarnings(warnings, fields);
        if (wasShortened)
        {
            warnings.Add("The fetched page text was shortened to keep the review draft manageable.");
        }

        return new PastedJobExtraction(
            sourceText,
            sourceText,
            fields,
            evidence,
            warnings);
    }

    private static SemanticJobData ExtractSemanticJobData(IDocument document) => new()
    {
        RoleTitle = AutomationText(document, "job-detail-title"),
        CompanyName = AutomationText(document, "advertiser-name"),
        CompanyLocation = AutomationText(document, "job-detail-location"),
        Classification = AutomationText(document, "job-detail-classifications"),
        EmploymentType = AutomationText(document, "job-detail-work-type"),
        SalaryText = AutomationText(document, "job-detail-salary"),
        Description = AutomationText(document, "jobAdDetails"),
    };

    private static string? AutomationText(IDocument document, string automationName)
    {
        var element = document.QuerySelector($"[data-automation='{automationName}']");
        if (element is null)
        {
            return null;
        }

        var output = new StringBuilder();
        AppendVisible(element, output);
        var value = PastedJobTextExtractor.Normalize(output.ToString());
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static StructuredJobData ExtractStructuredData(IDocument document)
    {
        var result = new StructuredJobData();
        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            if (string.IsNullOrWhiteSpace(script.TextContent))
            {
                continue;
            }

            try
            {
                using var json = JsonDocument.Parse(script.TextContent, JsonOptions);
                foreach (var node in FindJobPostingNodes(json.RootElement))
                {
                    result.Merge(ReadJobPosting(node));
                }
            }
            catch (JsonException)
            {
                // Broken structured data is ignored; visible text remains available.
            }
        }

        return result;
    }

    private static IEnumerable<JsonElement> FindJobPostingNodes(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (IsJobPosting(element))
            {
                yield return element;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    foreach (var nested in FindJobPostingNodes(property.Value))
                    {
                        yield return nested;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in FindJobPostingNodes(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool IsJobPosting(JsonElement element)
    {
        if (!TryProperty(element, "@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => IsJobPostingType(type.GetString()),
            JsonValueKind.Array => type.EnumerateArray()
                .Any(item => item.ValueKind == JsonValueKind.String && IsJobPostingType(item.GetString())),
            _ => false,
        };
    }

    private static bool IsJobPostingType(string? value) =>
        string.Equals(value, "JobPosting", StringComparison.OrdinalIgnoreCase)
        || value?.EndsWith("/JobPosting", StringComparison.OrdinalIgnoreCase) == true;

    private static StructuredJobData ReadJobPosting(JsonElement posting)
    {
        var data = new StructuredJobData
        {
            RoleTitle = StringProperty(posting, "title"),
            Description = HtmlToText(StringProperty(posting, "description")),
            EmploymentType = EmploymentType(posting),
            WorkplaceMode = WorkplaceMode(posting),
            CompanyName = NestedString(posting, "hiringOrganization", "name"),
            CompanyLocation = JobLocations(posting),
            JobReference = Identifier(posting),
            SalaryText = Salary(posting),
            ClosingDate = DateProperty(posting, "validThrough"),
            ContactName = NestedString(posting, "applicationContact", "name"),
            ContactEmail = NestedString(posting, "applicationContact", "email"),
        };
        return data;
    }

    private static string? JobLocations(JsonElement posting)
    {
        if (!TryProperty(posting, "jobLocation", out var location))
        {
            return null;
        }

        var locations = Elements(location)
            .Select(LocationText)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return locations.Length == 0 ? null : string.Join("; ", locations);
    }

    private static string? LocationText(JsonElement location)
    {
        if (location.ValueKind == JsonValueKind.String)
        {
            return location.GetString()?.Trim();
        }

        if (location.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var address = TryProperty(location, "address", out var nested) ? nested : location;
        if (address.ValueKind == JsonValueKind.String)
        {
            return address.GetString()?.Trim();
        }

        var parts = new[]
        {
            StringProperty(address, "addressLocality"),
            StringProperty(address, "addressRegion"),
            StringProperty(address, "addressCountry"),
        };
        var result = parts
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", result);
    }

    private static string? WorkplaceMode(JsonElement posting)
    {
        var value = StringProperty(posting, "jobLocationType");
        return value?.Contains("TELECOMMUTE", StringComparison.OrdinalIgnoreCase) == true
            ? "Remote"
            : null;
    }

    private static string? EmploymentType(JsonElement posting)
    {
        if (!TryProperty(posting, "employmentType", out var value))
        {
            return null;
        }

        var items = Elements(value)
            .Select(StringValue)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => NormalizeEmploymentType(item!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return items.Length == 0 ? null : string.Join(", ", items);
    }

    private static string NormalizeEmploymentType(string value) =>
        value.Trim().Replace('_', '-').ToUpperInvariant() switch
        {
            "FULL-TIME" => "Full-time",
            "PART-TIME" => "Part-time",
            "CONTRACTOR" or "CONTRACT" => "Contract",
            "TEMPORARY" => "Temporary",
            "INTERN" or "INTERNSHIP" => "Internship",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant()),
        };

    private static string? Identifier(JsonElement posting)
    {
        if (!TryProperty(posting, "identifier", out var identifier))
        {
            return null;
        }

        return identifier.ValueKind == JsonValueKind.Object
            ? StringProperty(identifier, "value") ?? StringProperty(identifier, "name")
            : StringValue(identifier);
    }

    private static string? Salary(JsonElement posting)
    {
        if (!TryProperty(posting, "baseSalary", out var salary))
        {
            return null;
        }

        if (salary.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            return StringValue(salary);
        }

        if (salary.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var currency = StringProperty(salary, "currency");
        var value = TryProperty(salary, "value", out var nestedValue) ? nestedValue : salary;
        if (value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            return JoinSalary(currency, StringValue(value), null, null);
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var minimum = NumberText(value, "minValue");
        var maximum = NumberText(value, "maxValue");
        var exact = NumberText(value, "value");
        var unit = StringProperty(value, "unitText");
        return JoinSalary(currency, minimum ?? exact, maximum, unit);
    }

    private static string? JoinSalary(
        string? currency,
        string? minimum,
        string? maximum,
        string? unit)
    {
        if (minimum is null)
        {
            return null;
        }

        var range = maximum is null ? minimum : $"{minimum} - {maximum}";
        var prefix = string.IsNullOrWhiteSpace(currency) ? string.Empty : $"{currency.Trim().ToUpperInvariant()} ";
        var suffix = string.IsNullOrWhiteSpace(unit)
            ? string.Empty
            : $" / {CultureInfo.InvariantCulture.TextInfo.ToTitleCase(unit.Trim().ToLowerInvariant())}";
        return $"{prefix}{range}{suffix}";
    }

    private static string? NumberText(JsonElement element, string name)
    {
        if (!TryProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number.ToString("#,0.##", CultureInfo.InvariantCulture);
        }

        return StringValue(value);
    }

    private static DateOnly? DateProperty(JsonElement element, string name)
    {
        var value = StringProperty(element, name);
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var date))
        {
            return date;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var dateTime)
                ? DateOnly.FromDateTime(dateTime.Date)
                : null;
    }

    private static string? NestedString(JsonElement element, string parentName, string childName)
    {
        if (!TryProperty(element, parentName, out var parent))
        {
            return null;
        }

        if (parent.ValueKind == JsonValueKind.String)
        {
            return parent.GetString()?.Trim();
        }

        foreach (var candidate in Elements(parent))
        {
            var value = StringProperty(candidate, childName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? StringProperty(JsonElement element, string name) =>
        TryProperty(element, name, out var value) ? StringValue(value) : null;

    private static string? StringValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()?.Trim(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };

    private static IEnumerable<JsonElement> Elements(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value];

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? MetaContent(IDocument document, string key)
    {
        foreach (var meta in document.QuerySelectorAll("meta"))
        {
            var name = meta.GetAttribute("property") ?? meta.GetAttribute("name");
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return meta.GetAttribute("content")?.Trim();
            }
        }

        return null;
    }

    private static string ExtractVisibleText(IDocument document)
    {
        var output = new StringBuilder();
        AppendVisible(document.Body, output);
        return PastedJobTextExtractor.Normalize(output.ToString());
    }

    private static void AppendVisible(INode? node, StringBuilder output)
    {
        if (node is null)
        {
            return;
        }

        if (node is IElement element
            && element.LocalName is "script" or "style" or "noscript" or "template" or "svg")
        {
            return;
        }

        if (node is IText text)
        {
            output.Append(text.Data);
            return;
        }

        var isBlock = node is IElement block
            && block.LocalName is "address" or "article" or "aside" or "blockquote" or "br"
                or "dd" or "div" or "dl" or "dt" or "footer" or "form" or "h1" or "h2"
                or "h3" or "h4" or "h5" or "h6" or "header" or "hr" or "li" or "main"
                or "nav" or "ol" or "p" or "pre" or "section" or "table" or "tr" or "ul";
        if (isBlock)
        {
            output.AppendLine();
        }

        foreach (var child in node.ChildNodes)
        {
            AppendVisible(child, output);
        }

        if (isBlock)
        {
            output.AppendLine();
        }
        else
        {
            output.Append(' ');
        }
    }

    private static string? HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var document = new HtmlParser().ParseDocument($"<body>{html}</body>");
        return ExtractVisibleText(document);
    }

    private static string FriendlyHostName(string host)
    {
        var normalized = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host[4..]
            : host;
        return normalized;
    }

    private static void AddStructuredEvidence(
        IDictionary<string, string> evidence,
        string field,
        object? value,
        string label)
    {
        if (value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
        {
            evidence[field] = $"High confidence — read from the page’s official JobPosting {label} field.";
        }
    }

    private static void AddSemanticEvidence(
        IDictionary<string, string> evidence,
        string field,
        object? value,
        string label)
    {
        if (value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
        {
            evidence[field] = $"High confidence — read from the page’s labelled job-page element for {label}.";
        }
    }

    private static void RemoveResolvedWarnings(
        ICollection<string> warnings,
        ExtractedJobFields fields)
    {
        var resolvedMarkers = new List<string>();
        if (fields.RoleTitle is not null)
        {
            resolvedMarkers.Add("job-title");
        }

        if (fields.CompanyName is not null)
        {
            resolvedMarkers.Add("company label");
        }

        if (fields.CompanyLocation is not null)
        {
            resolvedMarkers.Add("location label");
        }

        foreach (var warning in warnings
                     .Where(item => resolvedMarkers.Any(marker => item.Contains(marker, StringComparison.OrdinalIgnoreCase)))
                     .ToArray())
        {
            warnings.Remove(warning);
        }
    }

    private sealed class StructuredJobData
    {
        public string? RoleTitle { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyLocation { get; set; }
        public string? WorkplaceMode { get; set; }
        public string? JobReference { get; set; }
        public string? SalaryText { get; set; }
        public string? EmploymentType { get; set; }
        public DateOnly? ClosingDate { get; set; }
        public string? ContactName { get; set; }
        public string? ContactEmail { get; set; }
        public string? Description { get; set; }

        public void Merge(StructuredJobData other)
        {
            RoleTitle ??= other.RoleTitle;
            CompanyName ??= other.CompanyName;
            CompanyLocation ??= other.CompanyLocation;
            WorkplaceMode ??= other.WorkplaceMode;
            JobReference ??= other.JobReference;
            SalaryText ??= other.SalaryText;
            EmploymentType ??= other.EmploymentType;
            ClosingDate ??= other.ClosingDate;
            ContactName ??= other.ContactName;
            ContactEmail ??= other.ContactEmail;
            Description ??= other.Description;
        }
    }

    private sealed class SemanticJobData
    {
        public string? RoleTitle { get; init; }
        public string? CompanyName { get; init; }
        public string? CompanyLocation { get; init; }
        public string? Classification { get; init; }
        public string? EmploymentType { get; init; }
        public string? SalaryText { get; init; }
        public string? Description { get; init; }

        public bool HasJobDetails => RoleTitle is not null
            && (CompanyName is not null || Description is not null);
    }
}
