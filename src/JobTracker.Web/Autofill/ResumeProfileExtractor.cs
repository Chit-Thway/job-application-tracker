using System.Globalization;
using System.Text.RegularExpressions;

namespace JobTracker.Web.Autofill;

internal static partial class ResumeProfileExtractor
{
    private const string MonthName = @"(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)";
    private const string MonthAndYear = MonthName + @"\.?\s+(?:19|20)\d{2}";
    private const string DateToken = @"(?:" + MonthAndYear + @"|\d{1,2}/(?:19|20)\d{2}|(?:19|20)\d{2}-\d{2}|(?:19|20)\d{2})";
    private static readonly string[] MonthFormats = ["MMM yyyy", "MMMM yyyy", "M/yyyy", "MM/yyyy", "yyyy-MM"];

    public static ApplicantProfile Extract(string text, CancellationToken cancellationToken)
    {
        var sections = new Dictionary<Section, List<string>>();
        var section = Section.Contact;
        foreach (var line in text.Split('\n').Select(Normalize).Where(line => line.Length is > 0 and <= 2500))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindSection(line) is { } heading)
            {
                section = heading;
                continue;
            }
            if (!sections.TryGetValue(section, out var lines))
            {
                sections[section] = lines = [];
            }
            lines.Add(line);
        }

        var profile = Contact(sections.GetValueOrDefault(Section.Contact) ?? []);
        ReadExperience(sections.GetValueOrDefault(Section.Experience) ?? [], profile.Experience, cancellationToken);
        ReadEducation(sections.GetValueOrDefault(Section.Education) ?? [], profile.Education, cancellationToken);
        ReadCertifications(sections.GetValueOrDefault(Section.Certifications) ?? [], profile.Certifications, cancellationToken);
        return profile;
    }

    private static ApplicantProfile Contact(List<string> header)
    {
        var profile = new ApplicantProfile
        {
            FullName = Field(header, 150, "full name", "name"),
            GivenName = Field(header, 100, "given name", "given names", "first name"),
            FamilyName = Field(header, 100, "family name", "surname", "last name"),
            PreferredName = Field(header, 100, "preferred name"),
            Email = Unique(header.SelectMany(line => EmailPattern().Matches(UrlPattern().Replace(line, ""))
                    .Select(match => match.Value))
                .Where(value => value.Length <= 254)),
            Phone = ReadPhone(header),
            AddressLine1 = Field(header, 150, "street address", "address line 1"),
            AddressLine2 = Field(header, 150, "address line 2", "unit", "apartment"),
            City = Field(header, 100, "suburb", "city", "locality"),
            Region = Field(header, 100, "state", "region", "province"),
            Postcode = Field(header, 24, "postcode", "postal code", "zip code"),
            Country = Field(header, 100, "country"),
            NoticePeriod = Field(header, 160, "notice period"),
            Availability = Field(header, 160, "availability"),
        };
        if (profile.FullName is null)
        {
            profile.FullName = profile.GivenName is not null || profile.FamilyName is not null
                ? string.Join(' ', new[] { profile.GivenName, profile.FamilyName }.OfType<string>())
                : header.Take(6).FirstOrDefault(line => line.Length <= 100 && char.IsUpper(line[0])
                    && PersonNamePattern().IsMatch(line) && !RoleTitlePattern().IsMatch(line)
                    && !NonNamePattern().IsMatch(line) && !AustralianLocalityPattern().IsMatch(line));
        }
        // Keep compound names intact when the source does not label their parts.
        if (profile.GivenName is null && profile.FamilyName is null
            && profile.FullName is not null && SimpleNamePattern().IsMatch(profile.FullName))
        {
            var parts = profile.FullName.Split(' ');
            profile.GivenName = parts[0];
            profile.FamilyName = parts[1];
        }

        ReadAddress(header, profile);
        var urls = header.SelectMany(line => UrlPattern().Matches(line).Select(match => WebUrl(match.Value)))
            .OfType<Uri>().ToArray();
        profile.LinkedIn = urls.FirstOrDefault(uri =>
            IsHost(uri, "linkedin.com") && uri.AbsolutePath.StartsWith("/in/", StringComparison.OrdinalIgnoreCase))?.AbsoluteUri;
        profile.GitHub = urls.FirstOrDefault(uri =>
            IsHost(uri, "github.com") && uri.AbsolutePath.Trim('/').Split('/').Length == 1
            && uri.AbsolutePath.Length > 1)?.AbsoluteUri;
        profile.Portfolio = WebUrl(Field(header, 500, "portfolio", "portfolio url", "personal website", "website"))?.AbsoluteUri;
        return profile;
    }

    private static void ReadAddress(List<string> header, ApplicantProfile profile)
    {
        var combined = Field(header, 300, "address", "postal address", "residential address");
        var locality = Field(header, 150, "location");
        if (combined is not null)
        {
            var comma = combined.IndexOf(',');
            if (comma > 0 && StreetPattern().IsMatch(combined[..comma]))
            {
                profile.AddressLine1 ??= Limited(combined[..comma], 150);
                locality ??= combined[(comma + 1)..].Trim();
            }
            else if (comma < 0 && StreetPattern().IsMatch(combined))
            {
                profile.AddressLine1 ??= Limited(combined, 150);
            }
            else
            {
                locality ??= combined;
            }
        }

        var place = new[] { locality }.OfType<string>().Concat(header.Take(12)
                .SelectMany(line => line.Split('|', StringSplitOptions.TrimEntries)))
            .Select(line => AustralianLocalityPattern().Match(line)).FirstOrDefault(match => match.Success);
        if (place is not null)
        {
            profile.City ??= place.Groups["city"].Value.Trim();
            profile.Region ??= place.Groups["region"].Value;
            profile.Postcode ??= Limited(place.Groups["postcode"].Value, 24);
            profile.Country ??= Limited(place.Groups["country"].Value, 100);
        }
    }

    private static void ReadExperience(List<string> lines, List<ExperienceEntry> roles, CancellationToken cancellationToken)
    {
        var employerFirst = InferDashOrder(lines, cancellationToken);
        foreach (var entry in ReadPairs(lines, (section, index) => ExperienceHeader(section, index, employerFirst), cancellationToken))
        {
            var dates = Dates(entry.Source, entry.Details);
            var responsibilities = entry.Details.Where(line => !DateRangePattern().IsMatch(line)
                && Field([line], 150, "location", "start date", "end date", "dates") is null);
            roles.Add(new ExperienceEntry
            {
                Title = entry.Pair.First,
                Employer = entry.Pair.Second,
                Location = Limited(entry.Pair.Location, 150) ?? Field(entry.Details, 150, "location"),
                StartMonth = dates.Start,
                EndMonth = dates.End,
                Current = dates.Current,
                Responsibilities = Limited(string.Join('\n', responsibilities), 2500),
            });
        }
    }

    private static bool? InferDashOrder(List<string> lines, CancellationToken cancellationToken)
    {
        bool? employerFirst = null;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsBullet(line) || !HasDatedDash(line))
            {
                continue;
            }
            var parts = Parts(line);
            if (parts.Length is not (2 or 3) || !Plausible(parts[0]) || !Plausible(parts[1]))
            {
                continue;
            }
            if (RoleOrder(parts[0], parts[1]) is { } order)
            {
                if (employerFirst.HasValue && employerFirst.Value != order)
                {
                    return null;
                }
                employerFirst = order;
            }
        }
        return employerFirst;
    }

    private static bool HasDatedDash(string line) =>
        DateRangePattern().IsMatch(line) && SpacedDashPattern().IsMatch(WithoutDates(line)) && !line.Contains('|');

    private static bool? RoleOrder(string first, string second)
    {
        var firstIsRole = RoleTitlePattern().IsMatch(first);
        var secondIsRole = RoleTitlePattern().IsMatch(second);
        return firstIsRole != secondIsRole ? secondIsRole : null;
    }

    private static Pair? ExperienceHeader(List<string> lines, int index, bool? employerFirst)
    {
        var line = lines[index];
        if (IsBullet(line))
        {
            return null;
        }
        var parts = Parts(line);
        if (parts.Length is 2 or 3 && Plausible(parts[0]) && Plausible(parts[1]))
        {
            var order = RoleOrder(parts[0], parts[1]);
            if (order is null && employerFirst.HasValue && HasDatedDash(line))
            {
                order = employerFirst;
            }
            if (order is { } companyFirst)
            {
                var location = parts.ElementAtOrDefault(2);
                if (location is not null && DateOnlyPattern().IsMatch(location))
                {
                    location = null;
                }
                return companyFirst
                    ? new Pair(parts[1], parts[0], 1, location)
                    : new Pair(parts[0], parts[1], 1, location);
            }
        }

        if (index + 1 >= lines.Count)
        {
            return null;
        }
        var labelledTitle = Field([line], 180, "job title", "position", "role");
        var employer = Field([lines[index + 1]], 180, "employer", "company", "organisation", "organization");
        var title = labelledTitle ?? WithoutDates(line);
        var second = WithoutDates(lines[index + 1]);
        if (employer is not null && Plausible(title) && (labelledTitle is not null || RoleTitlePattern().IsMatch(title)))
        {
            return new Pair(title, employer, 2, null);
        }
        if (Plausible(title) && Plausible(second) && RoleTitlePattern().IsMatch(title) && !RoleTitlePattern().IsMatch(second)
            && lines.Skip(index).Take(3).Any(candidate => DateRangePattern().IsMatch(candidate)))
        {
            return new Pair(title, second, 2, null);
        }
        return null;
    }

    private static void ReadEducation(List<string> lines, List<EducationEntry> education, CancellationToken cancellationToken)
    {
        foreach (var entry in ReadPairs(lines, EducationHeader, cancellationToken))
        {
            var dates = Dates(entry.Source, entry.Details);
            education.Add(new EducationEntry
            {
                Qualification = entry.Pair.First,
                Institution = entry.Pair.Second,
                FieldOfStudy = Field(entry.Details, 150, "major", "field of study", "specialisation", "specialization")
                    ?? DegreeSubject(entry.Pair.First),
                StartMonth = dates.Start,
                EndMonth = dates.End ?? ParseMonth(Field(entry.Details, 30, "graduated", "graduation date", "completed")),
            });
        }
    }

    private static string? DegreeSubject(string qualification)
    {
        var subject = Limited(DegreeSubjectPattern().Match(qualification).Groups["subject"].Value, 150);
        return subject?.ToLowerInvariant() is "honours" or "honors" or "hons" ? null : subject;
    }

    private static Pair? EducationHeader(List<string> lines, int index)
    {
        var parts = Parts(Unbullet(lines[index]));
        if (parts.Length >= 2)
        {
            return EducationPair(parts[0], parts[1], 1);
        }
        return index + 1 < lines.Count
            ? EducationPair(WithoutDates(Unbullet(lines[index])), WithoutDates(lines[index + 1]), 2)
            : null;
    }

    private static Pair? EducationPair(string first, string second, int consumed)
    {
        if (!Plausible(first) || !Plausible(second))
        {
            return null;
        }
        if (QualificationPattern().IsMatch(first) && InstitutionPattern().IsMatch(second))
        {
            return new Pair(first, second, consumed, null);
        }
        return InstitutionPattern().IsMatch(first) && QualificationPattern().IsMatch(second)
            ? new Pair(second, first, consumed, null)
            : null;
    }

    private static IEnumerable<(Pair Pair, string Source, List<string> Details)> ReadPairs(
        List<string> lines, Func<List<string>, int, Pair?> readHeader, CancellationToken cancellationToken)
    {
        var found = 0;
        for (var index = 0; index < lines.Count && found < 20; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (readHeader(lines, index) is not { } header)
            {
                continue;
            }
            var start = index + header.Consumed;
            var next = start;
            while (next < lines.Count && readHeader(lines, next) is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                next++;
            }
            // A date on the second header line belongs to the same entry.
            var dateAndDetails = lines.GetRange(index + 1, next - index - 1);
            if (header.Consumed == 2 && !DateRangePattern().IsMatch(dateAndDetails[0]))
            {
                dateAndDetails.RemoveAt(0);
            }
            yield return (header, lines[index], dateAndDetails);
            found++;
            index = next - 1;
        }
    }

    private static void ReadCertifications(List<string> lines, List<CertificationEntry> certifications, CancellationToken cancellationToken)
    {
        CertificationEntry? current = null;
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is not null && ApplyCertificationDetails(current, line))
            {
                continue;
            }
            var parts = WithoutDates(Unbullet(line)).Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is 1 or 2 && Plausible(parts[0]) && !DateOnlyPattern().IsMatch(parts[0])
                && certifications.Count < 20 && !UrlPattern().IsMatch(parts[0]))
            {
                current = new CertificationEntry { Name = parts[0], Issuer = Limited(parts.ElementAtOrDefault(1), 180) };
                certifications.Add(current);
            }
        }
    }

    private static bool ApplyCertificationDetails(CertificationEntry entry, string line)
    {
        var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return DateOnlyPattern().IsMatch(line) || DateRangePattern().IsMatch(line);
        }
        switch (parts[0].ToLowerInvariant())
        {
            case "issuer" or "issued by": entry.Issuer = Limited(parts[1], 180); break;
            case "credential id" or "certificate number": entry.CredentialId = Limited(parts[1], 140); break;
            case "credential url" or "certificate url": entry.CredentialUrl = WebUrl(parts[1])?.AbsoluteUri; break;
            case "issued" or "issue date": entry.IssuedMonth = ParseMonth(parts[1]); break;
            case "expires" or "expiry date" or "expiration date": entry.ExpiryMonth = ParseMonth(parts[1]); break;
            default: return false;
        }
        return true;
    }

    private static (string? Start, string? End, bool Current) Dates(string header, List<string> details)
    {
        var match = new[] { header }.Concat(details).Take(5)
            .Select(line => DateRangePattern().Match(line)).FirstOrDefault(value => value.Success);
        var start = ParseMonth(match?.Groups["start"].Value ?? Field(details, 30, "start date", "start month"));
        var endText = match?.Groups["end"].Value ?? Field(details, 30, "end date", "end month");
        var end = ParseMonth(endText);
        if (start is null && end is not null && match?.Groups["sharedStart"] is { Success: true } shared)
        {
            var sameYear = ParseMonth($"{shared.Value} {end[..4]}");
            if (string.CompareOrdinal(sameYear, end) <= 0)
            {
                start = sameYear;
            }
        }
        if (start is not null && end is not null && string.CompareOrdinal(end, start) < 0)
        {
            end = null;
        }
        return (start, end, endText?.ToLowerInvariant() is "present" or "current" or "now" or "ongoing");
    }

    private static string? ParseMonth(string? value) =>
        DateTime.TryParseExact(value?.Replace(".", "").Replace("Sept ", "Sep ", StringComparison.OrdinalIgnoreCase),
            MonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            ? month.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : null;

    private static string? ReadPhone(List<string> header)
    {
        var labelled = Field(header, 40, "phone", "mobile", "telephone", "phone number");
        if (labelled is not null && PhonePattern().IsMatch(labelled) && labelled.Count(char.IsAsciiDigit) is >= 8 and <= 15)
        {
            return labelled;
        }
        var matches = header.SelectMany(line => PhonePattern().Matches(UrlPattern().Replace(line, ""))
                .Select(match => match.Value.Trim()))
            .Where(value => value.Count(char.IsAsciiDigit) is >= 9 and <= 15)
            .DistinctBy(value => string.Concat(value.Where(char.IsAsciiDigit)));
        return Unique(matches);
    }

    private static string? Unique(IEnumerable<string> values)
    {
        var matches = values.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string? Field(IEnumerable<string> lines, int maxLength, params string[] names)
    {
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && names.Contains(line[..colon].Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return Limited(line[(colon + 1)..], maxLength);
            }
        }
        return null;
    }

    private static Uri? WebUrl(string? value)
    {
        if (value is null)
        {
            return null;
        }
        var address = value.Trim().TrimEnd('.', ',', ';', ')', ']');
        if (!address.Contains("://", StringComparison.Ordinal))
        {
            address = $"https://{address}";
        }
        return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.AbsoluteUri.Length <= 500
            && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.IdnHost.Contains('.')
            ? uri : null;
    }

    private static bool IsHost(Uri uri, string domain) =>
        uri.IdnHost.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || uri.IdnHost.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    private static string? Limited(string? value, int maximum) =>
        value?.Trim() is { Length: > 0 } text && text.Length <= maximum ? text : null;

    private static string Normalize(string line) => SpacesPattern().Replace(line, " ").Trim();
    private static string WithoutDates(string line) => DateRangePattern().Replace(line, "").Trim(' ', '-', '–', '—', '|', ',');
    private static string[] Parts(string line) => EntrySeparator().Split(WithoutDates(line))
        .Select(part => part.Trim()).Where(part => part.Length > 0).ToArray();
    private static string Unbullet(string line) => line.TrimStart(' ', '•', '▪', '●', '-');
    private static bool IsBullet(string line) => line.StartsWith('•') || line.StartsWith('▪') || line.StartsWith('●')
        || line.StartsWith("- ", StringComparison.Ordinal);
    private static bool Plausible(string line) => line.Length is > 1 and <= 180
        && !IsBullet(line) && !line.EndsWith('.') && line.Any(char.IsLetter) && !EmailPattern().IsMatch(line);

    private static Section? FindSection(string line) => line.TrimEnd(':').ToLowerInvariant() switch
    {
        "contact" or "contact details" or "contact information" or "personal details" or "links" => Section.Contact,
        "experience" or "work experience" or "professional experience" or "relevant experience"
            or "employment" or "employment experience" or "employment history" or "career history" or "work history" => Section.Experience,
        "education" or "education and training" or "education and qualifications"
            or "qualifications" or "academic qualifications" => Section.Education,
        "certifications" or "certificates" or "licenses & certifications" or "licences and certifications"
            or "licenses and certifications" or "certifications and licenses" or "professional certifications" => Section.Certifications,
        "resume" or "résumé" or "curriculum vitae" => Section.Contact,
        "profile" or "professional profile" or "summary" or "professional summary" or "career objective"
            or "objective" or "about me" or "skills" or "technical skills" or "key skills"
            or "projects" or "references" or "referees" or "interests" or "awards" or "languages"
            or "achievements" or "volunteering" => Section.Other,
        _ => null,
    };

    private enum Section { Contact, Experience, Education, Certifications, Other }
    private sealed record Pair(string First, string Second, int Consumed, string? Location);

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesPattern();
    [GeneratedRegex(@"^\p{L}[\p{L}\p{M}.'’\-]*(?: \p{L}[\p{L}\p{M}.'’\-]*){0,5}$")]
    private static partial Regex PersonNamePattern();
    [GeneratedRegex(@"^\p{Lu}[\p{L}\p{M}’'\-]+ \p{Lu}[\p{L}\p{M}’'\-]+$")]
    private static partial Regex SimpleNamePattern();
    [GeneratedRegex(@"\b(?:resume|résumé|cv|curriculum|contact|name|email|phone|address|telephone|application|portfolio|linkedin|github|student|professional|australia|summary|perth|sydney|melbourne|brisbane|adelaide|darwin|hobart|canberra)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonNamePattern();
    [GeneratedRegex(@"\b(?:engineer|manager|analyst|technician|consultant|coordinator|specialist|developer|director|lead|intern|administrator|representative|associate|assistant|officer|designer|architect|teacher|nurse|chef|technologist|principal|operator|advisor|adviser|trainee|apprentice|researcher|planner|attendant|supervisor|clerk|accountant|tester|support|crew member|team member|barista|cashier|driver)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RoleTitlePattern();
    [GeneratedRegex(@"\b(?:bachelor|master|doctor|phd|diploma|degree|certificate|certification|graduate|bsc|msc|beng|meng|mba|honours)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualificationPattern();
    [GeneratedRegex(@"\b(?:university|college|tafe|school|institute|academy|polytechnic)\b", RegexOptions.IgnoreCase)]
    private static partial Regex InstitutionPattern();
    [GeneratedRegex(@"\b(?<start>" + DateToken + @")\s*(?:[-–—]|\bto\b)\s*(?<end>" + DateToken + @"|present|current|now|ongoing)\b|\b(?<sharedStart>" + MonthName + @"\.?)\s*[-–—]\s*(?<end>" + MonthAndYear + @")\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateRangePattern();
    [GeneratedRegex(@"^" + DateToken + "$", RegexOptions.IgnoreCase)]
    private static partial Regex DateOnlyPattern();
    [GeneratedRegex(@"\s*\|\s*|\s+(?:at|@|-|–|—)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex EntrySeparator();
    [GeneratedRegex(@"\s[-–—]\s")]
    private static partial Regex SpacedDashPattern();
    [GeneratedRegex(@"^(?:BSc|MSc|BEng|MEng|BA|MA|BCom|MCom|BIT|MIT|PhD)\s*\((?<subject>[^()]+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex DegreeSubjectPattern();
    [GeneratedRegex(@"[A-Z0-9.!#$%&'*+/=?^_{|}~-]+@[A-Z0-9-]+(?:\.[A-Z0-9-]+)+", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();
    [GeneratedRegex(@"(?<![\p{L}\p{N}/])(?:\+?\d|\(\d{1,4}\))[\d ()-]{6,24}\d(?![\p{L}\p{N}/])")]
    private static partial Regex PhonePattern();
    [GeneratedRegex(@"(?:https?://[^\s<>]+|(?:www\.)?(?:linkedin\.com/in/|github\.com/)[^\s<>|]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();
    [GeneratedRegex(@"^(?<city>[\p{L}][\p{L}\p{M} .’'-]{0,79}?)[,\s]+(?<region>WA|NT|QLD|NSW|VIC|SA|TAS|ACT|Western Australia|Northern Territory|Queensland|New South Wales|Victoria|South Australia|Tasmania|Australian Capital Territory)(?:[,\s]+(?<postcode>\d{4}))?(?:[,\s]+(?<country>Australia))?$", RegexOptions.IgnoreCase)]
    private static partial Regex AustralianLocalityPattern();
    [GeneratedRegex(@"\d.+\b(?:street|st|road|rd|avenue|ave|lane|drive|court|place|way|terrace|crescent|boulevard)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StreetPattern();
}
