using System.Text.RegularExpressions;

namespace JobTracker.Web.Extraction;

internal static partial class JobBoardLayoutHeuristics
{
    private const int HeaderLineLimit = 18;

    private static readonly Dictionary<string, string> MultiLineLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["job title"] = "RoleTitle",
            ["position title"] = "RoleTitle",
            ["company"] = "CompanyName",
            ["company name"] = "CompanyName",
            ["location"] = "CompanyLocation",
            ["job location"] = "CompanyLocation",
            ["work arrangement"] = "WorkplaceMode",
            ["working arrangement"] = "WorkplaceMode",
            ["employment type"] = "EmploymentType",
            ["job type"] = "EmploymentType",
            ["base pay range"] = "SalaryText",
            ["salary range"] = "SalaryText",
            ["salary"] = "SalaryText",
            ["pay"] = "SalaryText",
            ["job reference"] = "JobReference",
            ["requisition id"] = "JobReference",
            ["closing date"] = "ClosingDate",
            ["applications close"] = "ClosingDate",
            ["apply by"] = "ClosingDate",
        };

    public static void Enrich(
        string normalizedText,
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence,
        ICollection<string> warnings)
    {
        ExtractMultiLineLabels(lines, values, evidence);
        ExtractPlatformTitlePhrases(lines, values, evidence);
        ExtractLinkedInJoinPhrase(lines, values, evidence);
        ExtractStackedHeader(lines, values, evidence);
        ExtractHeaderLocation(lines, values, evidence);
        ExtractStandaloneSalary(lines, values, evidence);
        ExtractEmploymentType(lines, values, evidence);
        ExtractWorkplaceMode(normalizedText, lines, values, evidence);
        ExtractSourceSite(lines, values, evidence);
        ExtractClosingDatePhrase(normalizedText, values, evidence);
        ExtractCompanyIntroduction(lines, values, evidence);

        if (!values.ContainsKey("ClosingDate")
            && (PartialClosingDatePattern().IsMatch(normalizedText)
                || PartialApplyByDatePattern().IsMatch(normalizedText)))
        {
            warnings.Add(
                "A closing day and month were found, but no year was stated. The closing date was left blank for you to confirm.");
        }
    }

    private static void ExtractPlatformTitlePhrases(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        foreach (var rawLine in lines.Take(6))
        {
            var line = CleanHeading(rawLine);
            var hiring = HiringTitlePattern().Match(line);
            if (hiring.Success && LooksLikeLocation(hiring.Groups["location"].Value))
            {
                AddIfMissing("CompanyName", hiring.Groups["company"].Value, "Medium confidence — identified from a ‘company hiring role’ job-board title.", values, evidence);
                AddIfMissing("RoleTitle", hiring.Groups["title"].Value, "Medium confidence — identified from a ‘company hiring role’ job-board title.", values, evidence);
                AddIfMissing("CompanyLocation", hiring.Groups["location"].Value, "Medium confidence — identified from the location suffix in the job-board title.", values, evidence);
                return;
            }

            var roleAtCompany = RoleAtCompanyTitlePattern().Match(line);
            if (roleAtCompany.Success && LooksLikeLocation(roleAtCompany.Groups["location"].Value))
            {
                AddIfMissing("RoleTitle", roleAtCompany.Groups["title"].Value, "Medium confidence — identified from a ‘role at company’ job-board title.", values, evidence);
                AddIfMissing("CompanyName", roleAtCompany.Groups["company"].Value, "Medium confidence — identified from a ‘role at company’ job-board title.", values, evidence);
                AddIfMissing("CompanyLocation", roleAtCompany.Groups["location"].Value, "Medium confidence — identified from the location suffix in the job-board title.", values, evidence);
                return;
            }
        }
    }

    private static void ExtractMultiLineLabels(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        for (var index = 0; index < lines.Count - 1; index++)
        {
            var label = CleanHeading(lines[index]);
            if (!MultiLineLabels.TryGetValue(label, out var field) || values.ContainsKey(field))
            {
                continue;
            }

            var next = CleanHeading(lines[index + 1]);
            if (next.Length == 0
                || IsInterfaceNoise(next)
                || IsSectionHeading(next)
                || !IsPlausibleFieldValue(field, next))
            {
                continue;
            }

            values[field] = next;
            evidence[field] = $"High confidence — the value immediately followed the ‘{label}’ heading.";
        }
    }

    private static void ExtractLinkedInJoinPhrase(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        foreach (var line in lines.Take(HeaderLineLimit))
        {
            var match = JoinRolePattern().Match(CleanHeading(line));
            if (!match.Success)
            {
                continue;
            }

            AddIfMissing(
                "RoleTitle",
                match.Groups["title"].Value,
                "Medium confidence — identified from the job board’s ‘apply for this role’ header.",
                values,
                evidence);
            AddIfMissing(
                "CompanyName",
                match.Groups["company"].Value,
                "Medium confidence — identified from the job board’s ‘role at company’ header.",
                values,
                evidence);
            return;
        }
    }

    private static void ExtractStackedHeader(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey("RoleTitle") && values.ContainsKey("CompanyName"))
        {
            return;
        }

        var header = lines.Take(HeaderLineLimit).Select(CleanHeading).ToList();
        var titleIndex = header.FindIndex(line =>
            LooksLikeHeading(line)
            && !IsInterfaceNoise(line)
            && !IsSectionHeading(line)
            && !LooksLikeLocation(line)
            && !LooksLikeSalary(line)
            && !LooksLikeEmploymentType(line)
            && !LooksLikeWorkplaceMode(line));
        if (titleIndex < 0)
        {
            return;
        }

        var companyIndex = -1;
        for (var index = titleIndex + 1; index < Math.Min(header.Count, titleIndex + 5); index++)
        {
            var candidate = header[index];
            if (IsSeparator(candidate) || IsInterfaceNoise(candidate))
            {
                continue;
            }

            if (LooksLikeLocation(candidate)
                || LooksLikeSalary(candidate)
                || LooksLikeEmploymentType(candidate)
                || LooksLikeWorkplaceMode(candidate))
            {
                break;
            }

            if (LooksLikeCompany(candidate))
            {
                companyIndex = index;
            }

            break;
        }

        var hasLocation = values.ContainsKey("CompanyLocation") || header.Any(LooksLikeLocation);
        var hasSalary = values.ContainsKey("SalaryText") || header.Any(LooksLikeSalary);
        var hasPlatformByline = header.Any(HasPlatformByline);
        var hasPostingMetadata = header.Any(IsPostingMetadata);
        var supportCount = (companyIndex >= 0 ? 1 : 0)
            + (hasLocation ? 1 : 0)
            + (hasSalary ? 1 : 0)
            + (hasPlatformByline ? 1 : 0)
            + (hasPostingMetadata ? 1 : 0);
        if (supportCount < 2)
        {
            return;
        }

        AddIfMissing(
            "RoleTitle",
            header[titleIndex],
            "Medium confidence — inferred from the first line of a corroborated job-board header.",
            values,
            evidence);
        if (companyIndex >= 0)
        {
            AddIfMissing(
                "CompanyName",
                CleanCompanyCandidate(header[companyIndex]),
                "Medium confidence — inferred from the company position directly below the job title.",
                values,
                evidence);
        }
    }

    private static void ExtractHeaderLocation(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey("CompanyLocation"))
        {
            return;
        }

        foreach (var rawLine in lines.Take(HeaderLineLimit))
        {
            var line = CleanHeading(rawLine);
            if (!LooksLikeLocation(line) || LooksLikeWorkplaceMode(line))
            {
                continue;
            }

            values["CompanyLocation"] = RemoveTrailingWorkplaceMode(line);
            evidence["CompanyLocation"] =
                "Medium confidence — matched an Australian location in the job-board header.";
            return;
        }
    }

    private static void ExtractStandaloneSalary(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey("SalaryText"))
        {
            return;
        }

        foreach (var rawLine in lines.Take(HeaderLineLimit))
        {
            var line = CleanHeading(rawLine);
            if (!LooksLikeSalary(line))
            {
                continue;
            }

            values["SalaryText"] = line;
            evidence["SalaryText"] =
                "Medium confidence — matched a standalone currency and pay-period line in the job-board header.";
            return;
        }
    }

    private static void ExtractEmploymentType(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey("EmploymentType"))
        {
            return;
        }

        foreach (var rawLine in lines.Take(HeaderLineLimit))
        {
            var line = CleanHeading(rawLine);
            if (!LooksLikeEmploymentType(line))
            {
                continue;
            }

            values["EmploymentType"] = line;
            evidence["EmploymentType"] =
                "Medium confidence — matched a standalone employment-type line in the job-board header.";
            return;
        }
    }

    private static void ExtractWorkplaceMode(
        string normalizedText,
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey("WorkplaceMode"))
        {
            return;
        }

        foreach (var rawLine in lines.Take(HeaderLineLimit))
        {
            var line = CleanHeading(rawLine);
            var titleMode = ParentheticalWorkplacePattern().Match(line);
            if (titleMode.Success)
            {
                values["WorkplaceMode"] = CanonicalWorkplaceMode(titleMode.Groups["mode"].Value);
                evidence["WorkplaceMode"] =
                    "Medium confidence — the work mode was stated in the job-board title or location header.";
                return;
            }

            if (LooksLikeWorkplaceMode(line))
            {
                values["WorkplaceMode"] = CanonicalWorkplaceMode(line);
                evidence["WorkplaceMode"] =
                    "Medium confidence — matched a standalone work-mode line in the job-board header.";
                return;
            }
        }

        var explicitMode = ExplicitWorkplaceSentencePattern().Match(normalizedText);
        if (explicitMode.Success)
        {
            values["WorkplaceMode"] = CanonicalWorkplaceMode(explicitMode.Groups["mode"].Value);
            evidence["WorkplaceMode"] =
                "Medium confidence — matched an explicit remote, hybrid, or on-site work statement.";
        }
    }

    private static void ExtractSourceSite(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey("SourceSite"))
        {
            return;
        }

        foreach (var rawLine in lines.Take(HeaderLineLimit))
        {
            var match = PlatformBylinePattern().Match(CleanHeading(rawLine));
            if (!match.Success)
            {
                continue;
            }

            values["SourceSite"] = CanonicalSource(match.Groups["site"].Value);
            evidence["SourceSite"] =
                "Medium confidence — matched a recognised job-board name in the posting byline.";
            return;
        }
    }

    private static void ExtractClosingDatePhrase(
        string normalizedText,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey("ClosingDate"))
        {
            return;
        }

        var match = ClosingDatePhrasePattern().Match(normalizedText);
        if (!match.Success)
        {
            match = ApplyByDatePattern().Match(normalizedText);
        }

        if (!match.Success || !FourDigitYearPattern().IsMatch(match.Groups["date"].Value))
        {
            return;
        }

        values["ClosingDate"] = match.Groups["date"].Value.Trim().TrimEnd('.');
        evidence["ClosingDate"] =
            "High confidence — matched an explicit applications-close sentence with a stated year.";
    }

    private static void ExtractCompanyIntroduction(
        IReadOnlyList<string> lines,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey("CompanyName"))
        {
            return;
        }

        foreach (var rawLine in lines)
        {
            var match = CompanyIntroductionPattern().Match(CleanHeading(rawLine));
            if (!match.Success)
            {
                continue;
            }

            var company = match.Groups["company"].Value.Trim();
            if (company.StartsWith("This ", StringComparison.OrdinalIgnoreCase)
                || company.StartsWith("The ", StringComparison.OrdinalIgnoreCase)
                || company.StartsWith("Our ", StringComparison.OrdinalIgnoreCase)
                || string.Equals(company, "We", StringComparison.OrdinalIgnoreCase)
                || string.Equals(company, "I", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            values["CompanyName"] = company;
            evidence["CompanyName"] =
                "Medium confidence — inferred from an organisation-introduction sentence such as ‘Company is …’.";
            return;
        }
    }

    private static void AddIfMissing(
        string field,
        string value,
        string note,
        IDictionary<string, string> values,
        IDictionary<string, string> evidence)
    {
        if (values.ContainsKey(field))
        {
            return;
        }

        var cleaned = CleanHeading(value).TrimEnd('.', ';');
        if (cleaned.Length == 0)
        {
            return;
        }

        values[field] = cleaned;
        evidence[field] = note;
    }

    private static string CleanHeading(string value) =>
        MarkdownHeadingPrefixPattern().Replace(value.Trim(), string.Empty).Trim();

    private static bool LooksLikeHeading(string value)
    {
        var wordCount = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return value.Length is >= 3 and <= 200
            && wordCount is >= 1 and <= 22
            && !value.EndsWith('.')
            && !value.EndsWith(';')
            && !value.Contains(" is ", StringComparison.OrdinalIgnoreCase)
            && !value.Contains(" are ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCompany(string value)
    {
        var wordCount = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return LooksLikeHeading(value)
            && wordCount <= 12
            && !MultiLineLabels.ContainsKey(value)
            && !IsSectionHeading(value)
            && !IsInterfaceNoise(value)
            && !IsPostingMetadata(value)
            && !HasPlatformByline(value);
    }

    private static bool LooksLikeLocation(string value) =>
        AustralianLocationPattern().IsMatch(value)
        || string.Equals(value, "Australia", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPlausibleFieldValue(string field, string value)
    {
        var cleaned = CleanHeading(value);
        if (cleaned.Length == 0
            || RatingOnlyPattern().IsMatch(cleaned)
            || ReviewsOnlyPattern().IsMatch(cleaned))
        {
            return false;
        }

        return field switch
        {
            "SalaryText" => LooksLikeSalary(cleaned),
            "EmploymentType" => LooksLikeEmploymentType(cleaned),
            "WorkplaceMode" => LooksLikeWorkplaceMode(cleaned)
                || ParentheticalWorkplacePattern().IsMatch(cleaned),
            "RoleTitle" => LooksLikeHeading(cleaned) && !IsPostingMetadata(cleaned),
            "CompanyName" => LooksLikeCompany(cleaned),
            "CompanyLocation" => cleaned.Length <= 300 && cleaned.Any(char.IsLetter),
            _ => true,
        };
    }

    private static bool LooksLikeSalary(string value)
    {
        if (value.Length > 180)
        {
            return false;
        }

        return (CurrencyAmountPattern().IsMatch(value)
                && SalaryRangeOrPeriodPattern().IsMatch(value))
            || AnnualSalaryPattern().IsMatch(value)
            || BareKSalaryPattern().IsMatch(value);
    }

    private static bool LooksLikeEmploymentType(string value) =>
        EmploymentTypePattern().IsMatch(value);

    private static bool LooksLikeWorkplaceMode(string value) =>
        WorkplaceOnlyPattern().IsMatch(value);

    private static bool HasPlatformByline(string value) =>
        PlatformBylinePattern().IsMatch(value);

    private static bool IsPostingMetadata(string value) =>
        PostingMetadataPattern().IsMatch(value)
        || ReviewMetadataPattern().IsMatch(value);

    private static bool IsInterfaceNoise(string value) =>
        value.Length == 0
        || IsSeparator(value)
        || InterfaceNoisePattern().IsMatch(value)
        || IsPostingMetadata(value);

    private static bool IsSectionHeading(string value) =>
        SectionHeadingPattern().IsMatch(value);

    private static bool IsSeparator(string value) =>
        SeparatorPattern().IsMatch(value);

    private static string RemoveTrailingWorkplaceMode(string value) =>
        TrailingWorkplaceModePattern().Replace(value, string.Empty).Trim().TrimEnd(',', '·', '-').Trim();

    private static string CanonicalWorkplaceMode(string value)
    {
        if (value.Contains("hybrid", StringComparison.OrdinalIgnoreCase))
        {
            return "Hybrid";
        }

        if (value.Contains("remote", StringComparison.OrdinalIgnoreCase)
            || value.Contains("work from home", StringComparison.OrdinalIgnoreCase))
        {
            return "Remote";
        }

        return "On-site";
    }

    private static string CleanCompanyCandidate(string value) =>
        CompanyRatingSuffixPattern().Replace(value, string.Empty).Trim();

    private static string CanonicalSource(string value)
    {
        if (value.StartsWith("seek grad", StringComparison.OrdinalIgnoreCase))
        {
            return "SEEK Grad";
        }

        return value.ToLowerInvariant() switch
        {
            "seek" => "SEEK",
            "linkedin" => "LinkedIn",
            "indeed" => "Indeed",
            "jora" => "Jora",
            "adzuna" => "Adzuna",
            "gradconnection" => "GradConnection",
            "prosple" => "Prosple",
            _ => value,
        };
    }

    [GeneratedRegex(@"^\s*(?:#{1,6}|[-*•])\s*")]
    private static partial Regex MarkdownHeadingPrefixPattern();

    [GeneratedRegex(@"^join to apply for (?:the )?(?<title>.+?) role at (?<company>.+?)\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JoinRolePattern();

    [GeneratedRegex(@"^(?<company>.+?)\s+hiring\s+(?<title>.+?)\s+in\s+(?<location>.+?)(?:\s+\|\s+.+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HiringTitlePattern();

    [GeneratedRegex(@"^(?<title>.+?)\s+at\s+(?<company>.+?)\s+[—–|-]\s+(?<location>.+?)(?:\s+\|\s+.+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoleAtCompanyTitlePattern();

    [GeneratedRegex(@"^(?:(?:[A-Za-z][A-Za-z'.-]*\s+){0,4}[A-Za-z][A-Za-z'.-]*,?\s+(?:NSW|VIC|QLD|WA|SA|TAS|ACT|NT)(?:,?\s*Australia)?|(?:[A-Za-z][A-Za-z'.-]*\s+){0,4}[A-Za-z][A-Za-z'.-]*,\s*(?:New South Wales|Victoria|Queensland|Western Australia|South Australia|Tasmania|Australian Capital Territory|Northern Territory)(?:,?\s*Australia)?)(?:\s+\d{4})?(?:\s*\((?:hybrid|remote|on[- ]?site)\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AustralianLocationPattern();

    [GeneratedRegex(@"(?:A\$|AUD\s*|\$)\s*\d[\d,.]*(?:\s*[kK])?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyAmountPattern();

    [GeneratedRegex(@"(?:[-–—]|\bto\b|/\s*(?:hr|hour|day|week|month|yr|year)|\bper\s+(?:hour|day|week|month|year|annum)|\ba\s+(?:year|week|day|hour)|\b(?:package|superannuation|super|p\.?a\.?)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SalaryRangeOrPeriodPattern();

    [GeneratedRegex(@"^(?:(?:A\$|AUD|\$)\s*)?(?:\d{1,3}(?:,\d{3})+|\d{5,7})(?:\.\d{1,2})?(?:\s*[-–—]\s*(?:(?:A\$|AUD|\$)\s*)?(?:\d{1,3}(?:,\d{3})+|\d{5,7})(?:\.\d{1,2})?)?(?:\s*(?:/\s*(?:yr|year)|per\s+(?:year|annum)|a\s+year|p\.?a\.?|package|plus\s+super(?:annuation)?))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnnualSalaryPattern();

    [GeneratedRegex(@"^(?:(?:A\$|AUD|\$)\s*)?\d{2,3}(?:\.\d+)?\s*[kK](?:(?:\s*[-–—]\s*|\s+to\s+)(?:(?:A\$|AUD|\$)\s*)?\d{2,3}(?:\.\d+)?\s*[kK])?(?:\s*(?:/\s*(?:yr|year)|per\s+(?:year|annum)|a\s+year|p\.?a\.?|package|plus\s+super(?:annuation)?))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareKSalaryPattern();

    [GeneratedRegex(@"^\s*[0-5](?:\.\d{1,2})?(?:\s+(?:out\s+of\s+)?5(?:\s+stars?)?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RatingOnlyPattern();

    [GeneratedRegex(@"^\s*\d[\d,]*\s+reviews?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReviewsOnlyPattern();

    [GeneratedRegex(@"\s+\d(?:\.\d)?(?:\s+(?:out of\s+)?5\s+stars?)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CompanyRatingSuffixPattern();

    [GeneratedRegex(@"^(?:full[- ]?time|part[- ]?time|casual|permanent|contract|fixed[- ]term(?: contract)?|maximum[- ]term(?: contract)?|temporary|internship)(?:\s*[·|/]\s*(?:full[- ]?time|part[- ]?time|casual|permanent|contract|fixed[- ]term|temporary|internship))*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmploymentTypePattern();

    [GeneratedRegex(@"^(?:fully\s+)?(?:remote|hybrid|on[- ]?site|onsite|work from home)(?:\s+(?:role|working|work))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WorkplaceOnlyPattern();

    [GeneratedRegex(@"\((?<mode>hybrid|remote|on[- ]?site|onsite)(?:\s*[^)]*)?\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalWorkplacePattern();

    [GeneratedRegex(@"(?:^|\n)\s*(?:(?:this (?:role|position)|the role) is\s+|(?:flexible\s+)?(?<mode>hybrid|remote|on[- ]?site|onsite)\s+(?:ways?\s+of\s+working|working|role)\b|work(?:ing)? (?:arrangement|model)(?: is)?\s+)(?<mode>hybrid|remote|on[- ]?site|onsite)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitWorkplaceSentencePattern();

    [GeneratedRegex(@"\b(?:from|via|at)\s+(?<site>SEEK(?:\s+Grad)?|Indeed|LinkedIn|Jora|Adzuna|GradConnection|Prosple)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlatformBylinePattern();

    [GeneratedRegex(@"(?:^|\n)\s*applications?\s+(?:close|closing)(?:\s+date)?(?:\s+is)?(?:\s+on)?\s*(?::|[-–—])?\s*(?<date>(?:(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday),?\s+)?\d{1,2}\s+[A-Za-z]+(?:\s*,?\s*\d{4})?)\.?\s*(?:$|\n)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClosingDatePhrasePattern();

    [GeneratedRegex(@"(?:^|\n)\s*applications?\s+(?:close|closing)(?:\s+date)?(?:\s+is)?(?:\s+on)?\s*(?::|[-–—])?\s*(?:(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday),?\s+)?\d{1,2}\s+[A-Za-z]+\.?\s*(?:$|\n)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartialClosingDatePattern();

    [GeneratedRegex(@"(?:^|\n)\s*apply\s+by\s*(?::|[-–—])?\s*(?<date>(?:(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday),?\s+)?\d{1,2}\s+[A-Za-z]+(?:\s*,?\s*\d{4})?)\.?\s*(?:$|\n)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApplyByDatePattern();

    [GeneratedRegex(@"(?:^|\n)\s*apply\s+by\s*(?::|[-–—])?\s*(?:(?:Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday),?\s+)?\d{1,2}\s+[A-Za-z]+\.?\s*(?:$|\n)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PartialApplyByDatePattern();

    [GeneratedRegex(@"\b\d{4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex FourDigitYearPattern();

    [GeneratedRegex(@"^(?<company>[A-Z][A-Za-z0-9&'’.+\-]*(?:\s+[A-Z0-9][A-Za-z0-9&'’.+\-]*){0,7})\s+(?:is|are)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompanyIntroductionPattern();

    [GeneratedRegex(@"^(?:\d+(?:\.\d+)?\s+from\s+\d+\s+reviews?|\d+\s*(?:m|h|d|w|hours?|days?|weeks?|months?)\s+ago\b|posted\s+\d+|be among the first|\d+\s+applicants?\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PostingMetadataPattern();

    [GeneratedRegex(@"\breviews?\s+at\s+(?:SEEK|Indeed|LinkedIn)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReviewMetadataPattern();

    [GeneratedRegex(@"^(?:apply|apply now|apply on company site|save|no experience required|see who .+ hired|get notified|join or sign in|sign in with|show more|no longer accepting applications)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InterfaceNoisePattern();

    [GeneratedRegex(@"^(?:about(?: the)? (?:role|job|position|company|us)|what you(?:'|’)ll do|what you will do|how you(?:'|’)ll grow|what you(?:'|’)ll need|requirements?|responsibilities|benefits|our benefits|job description|the opportunity|summary|how to apply)\s*:?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionHeadingPattern();

    [GeneratedRegex(@"^[\p{P}\p{S}]+$")]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"\s*\((?:hybrid|remote|on[- ]?site|onsite)[^)]*\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingWorkplaceModePattern();
}
