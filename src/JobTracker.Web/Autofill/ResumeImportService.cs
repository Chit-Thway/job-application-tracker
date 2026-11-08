namespace JobTracker.Web.Autofill;

public sealed class ResumeImportService : IDisposable
{
    public const int MaximumUploadBytes = ResumeDocumentReader.MaximumBytes;
    private readonly SemaphoreSlim parser = new(1, 1);

    public async Task<ResumeImportReview> ImportAsync(
        IFormFile? file, ApplicantProfile draft, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new ResumeImportException("Choose a PDF or Word (.docx) résumé first.");
        }
        if (file.Length > MaximumUploadBytes)
        {
            throw new ResumeImportException("Choose a résumé that is 5 MB or smaller.");
        }
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx"))
        {
            throw new ResumeImportException("Choose a PDF or Word (.docx) résumé.");
        }
        if (!await parser.WaitAsync(0, cancellationToken))
        {
            throw new ResumeImportException("Another résumé is being processed. Try again in a moment.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await using var stream = file.OpenReadStream();
            var bytes = new byte[(int)file.Length];
            await stream.ReadExactlyAsync(bytes, deadline.Token);
            var text = ResumeDocumentReader.Read(bytes, extension, deadline.Token);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ResumeImportException("This file has no readable text. Export a text PDF or DOCX, or enter your details below.");
            }

            var extracted = ResumeProfileExtractor.Extract(text, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            var review = Merge(draft, extracted);
            if (review.MatchedDetails == 0)
            {
                throw new ResumeImportException("No profile details could be identified. Try a simpler résumé layout, or enter your details below.");
            }
            return review;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ResumeImportException("This résumé took too long to process. Try a smaller or simpler copy.");
        }
        catch (IOException)
        {
            throw new ResumeImportException("This résumé could not be read. Export a new copy and try again.");
        }
        finally
        {
            parser.Release();
        }
    }

    private static ResumeImportReview Merge(ApplicantProfile draft, ApplicantProfile source)
    {
        var fields = new List<string>();
        var differences = new List<ResumeFieldDifference>();
        var matchedFields = 0;
        var nameConflict = Different(draft.FullName, source.FullName)
            || Different(draft.GivenName, source.GivenName) || Different(draft.FamilyName, source.FamilyName);

        void Import(string label, string? saved, string? suggested, Action<string> set, bool keepBlank = false)
        {
            if (string.IsNullOrWhiteSpace(suggested))
            {
                return;
            }
            matchedFields++;
            if (string.IsNullOrWhiteSpace(saved) && !keepBlank)
            {
                set(suggested);
                fields.Add(label);
            }
            else if (!Same(saved, suggested))
            {
                differences.Add(new ResumeFieldDifference(label, saved, suggested));
            }
        }

        Import("Full name", draft.FullName, source.FullName, value => draft.FullName = value, nameConflict);
        Import("Given name", draft.GivenName, source.GivenName, value => draft.GivenName = value, nameConflict);
        Import("Family name", draft.FamilyName, source.FamilyName, value => draft.FamilyName = value, nameConflict);
        Import("Preferred name", draft.PreferredName, source.PreferredName, value => draft.PreferredName = value, nameConflict);
        Import("Application email", draft.Email, source.Email, value => draft.Email = value);
        Import("Phone", draft.Phone, source.Phone, value => draft.Phone = value);
        Import("Street address", draft.AddressLine1, source.AddressLine1, value => draft.AddressLine1 = value);
        Import("Address line 2", draft.AddressLine2, source.AddressLine2, value => draft.AddressLine2 = value);
        Import("Suburb or city", draft.City, source.City, value => draft.City = value);
        Import("State or territory", draft.Region, source.Region, value => draft.Region = value);
        Import("Postcode", draft.Postcode, source.Postcode, value => draft.Postcode = value);
        Import("Country", draft.Country, source.Country, value => draft.Country = value);
        Import("LinkedIn", draft.LinkedIn, source.LinkedIn, value => draft.LinkedIn = value);
        Import("GitHub", draft.GitHub, source.GitHub, value => draft.GitHub = value);
        Import("Portfolio", draft.Portfolio, source.Portfolio, value => draft.Portfolio = value);
        Import("Notice period", draft.NoticePeriod, source.NoticePeriod, value => draft.NoticePeriod = value);
        Import("Availability", draft.Availability, source.Availability, value => draft.Availability = value);

        var roles = AppendNew(draft.Experience, source.Experience, (left, right) =>
            Same(left.Employer, right.Employer) && Same(left.Title, right.Title)
            && !Different(left.StartMonth, right.StartMonth));
        var education = AppendNew(draft.Education, source.Education, (left, right) =>
            Same(left.Institution, right.Institution) && Same(left.Qualification, right.Qualification));
        var certificates = AppendNew(draft.Certifications, source.Certifications, (left, right) =>
            Same(left.Name, right.Name) && !Different(left.Issuer, right.Issuer));
        var detectedRecords = source.Experience.Count + source.Education.Count + source.Certifications.Count;
        return new ResumeImportReview(fields, differences, roles, education, certificates,
            detectedRecords - roles - education - certificates, matchedFields + detectedRecords);
    }

    private static int AppendNew<T>(List<T> draft, List<T> source, Func<T, T, bool> sameEntry)
    {
        var initial = draft.Count;
        foreach (var entry in source)
        {
            if (draft.Count >= 20)
            {
                break;
            }
            if (!draft.Any(existing => sameEntry(existing, entry)))
            {
                draft.Add(entry);
            }
        }
        return draft.Count - initial;
    }

    private static bool Same(string? first, string? second) =>
        string.Equals(first?.Trim(), second?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Different(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) && !Same(first, second);

    public void Dispose() => parser.Dispose();
}

public sealed record ResumeFieldDifference(string Field, string? Saved, string Suggested);

public sealed record ResumeImportReview(
    IReadOnlyList<string> FilledFields,
    IReadOnlyList<ResumeFieldDifference> Differences,
    int RolesAdded,
    int EducationAdded,
    int CertificatesAdded,
    int EntriesSkipped,
    int MatchedDetails);
