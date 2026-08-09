using System.Net;
using JobTracker.Web.Data;
using JobTracker.Web.Extraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    private const string LabelledPosting = """
        Job Title: Platform Engineer
        Company: Synthetic Review Labs
        Location: Perth, WA
        Work Arrangement: Hybrid
        Employment Type: Full-time
        Salary: AUD 120,000 plus super
        Job Reference: SRL-204
        Closing Date: 28 August 2026
        Contact: Morgan Example
        Contact Email: morgan@example.test
        Source Site: Synthetic Careers
        Job Posting URL: https://example.test/jobs/srl-204

        Build reliable systems for a wholly fictional team.
        """;

    private const string SeekStylePosting = """
        2027 Technology Graduate Program - Cybersecurity (NSW)
        Synthetic Horizon Advisory
        –
        Sydney NSW
        4.1 from 12 reviews at SEEK
        $70,000 - $80,000 a year
        No experience required
        6d ago, from SEEK Grad Graduate Positions
        Apply on company site

        About The Role
        Support cybersecurity assessments for a wholly fictional advisory team.

        Applications close on Sunday, 16 August.
        """;

    [Fact]
    public async Task AuthenticatedUser_CanReviewCorrectAndConfirmPastedText()
    {
        var client = await CreateAuthenticatedClientAsync();
        var draftLocation = await PostPastedTextAsync(client, LabelledPosting);

        var review = await client.GetAsync(draftLocation);
        var reviewContent = await review.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("Check the extracted details.", reviewContent, StringComparison.Ordinal);
        Assert.Contains("Platform Engineer", reviewContent, StringComparison.Ordinal);
        Assert.Contains("Synthetic Review Labs", reviewContent, StringComparison.Ordinal);
        Assert.Contains("High confidence", reviewContent, StringComparison.Ordinal);
        var token = ExtractAntiforgeryToken(reviewContent);
        var draftId = DraftIdFromLocation(draftLocation);

        var confirm = await client.PostAsync(
            draftLocation,
            Form(
                ("DraftId", draftId.ToString()),
                ("RoleTitle", "Senior Platform Engineer"),
                ("CompanyName", "Synthetic Review Labs"),
                ("CompanyLocation", "Perth, WA"),
                ("AppliedOn", "2026-08-04"),
                ("WorkplaceMode", "Hybrid"),
                ("SourceUrl", "https://example.test/jobs/srl-204"),
                ("SourceSite", "Synthetic Careers"),
                ("JobReference", "SRL-204"),
                ("SalaryText", "AUD 125,000 plus super"),
                ("EmploymentType", "Full-time"),
                ("ClosingDate", "2026-08-28"),
                ("ContactName", "Morgan Example"),
                ("ContactEmail", "morgan@example.test"),
                ("Notes", "Corrected during review"),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", token)));

        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
        var applicationLocation = confirm.Headers.Location?.OriginalString;
        Assert.NotNull(applicationLocation);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var applicationId = IdFromLocation(applicationLocation);
            var application = await database.JobApplications.SingleAsync(item => item.Id == applicationId);
            var company = await database.Companies.SingleAsync(item => item.Id == application.CompanyId);
            var metadata = ExtractionDraftService.ReadMetadata(application.ExtractionMetadataJson);

            Assert.Equal("Senior Platform Engineer", application.RoleTitle);
            Assert.Equal(LabelledPosting, application.SourceText);
            Assert.Equal("Corrected during review", application.Notes);
            Assert.Equal("Synthetic Review Labs", company.Name);
            Assert.Equal("Perth, WA", company.Location);
            Assert.NotNull(metadata);
            Assert.Equal("AUD 125,000 plus super", metadata.SalaryText);
            Assert.Equal("SRL-204", metadata.JobReference);
            Assert.DoesNotContain(database.ExtractionDrafts, draft => draft.Id == draftId);
        }

        var details = await client.GetAsync(applicationLocation);
        var detailsContent = await details.Content.ReadAsStringAsync();
        Assert.Contains("Reviewed extraction", detailsContent, StringComparison.Ordinal);
        Assert.Contains("AUD 125,000 plus super", detailsContent, StringComparison.Ordinal);
        Assert.Contains("View original job source text", detailsContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SeekStyleUnlabelledPosting_PrefillsUsefulReviewWithoutCreatingApplication()
    {
        var client = await CreateAuthenticatedClientAsync();
        var draftLocation = await PostPastedTextAsync(client, SeekStylePosting);

        var review = await client.GetAsync(draftLocation);
        var content = await review.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("2027 Technology Graduate Program - Cybersecurity (NSW)", content, StringComparison.Ordinal);
        Assert.Contains("Synthetic Horizon Advisory", content, StringComparison.Ordinal);
        Assert.Contains("Sydney NSW", content, StringComparison.Ordinal);
        Assert.Contains("$70,000 - $80,000 a year", content, StringComparison.Ordinal);
        Assert.Contains("Medium confidence", content, StringComparison.Ordinal);
        Assert.Contains("no year was stated", content, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await database.JobApplications.AnyAsync(item => item.SourceText == SeekStylePosting));
    }

    [Fact]
    public async Task CancellingReview_CreatesNoApplicationOrCompany()
    {
        var client = await CreateAuthenticatedClientAsync();
        var uniqueCompany = $"Synthetic Cancel {Guid.NewGuid():N}";
        var source = $"Job Title: Cancel Test Engineer\nCompany: {uniqueCompany}\nLocation: Perth, WA";
        var draftLocation = await PostPastedTextAsync(client, source);
        var draftId = DraftIdFromLocation(draftLocation);
        var review = await client.GetAsync(draftLocation);
        var token = ExtractAntiforgeryToken(await review.Content.ReadAsStringAsync());

        var cancelled = await client.PostAsync(
            $"/applications/import/{draftId}/cancel",
            Form(("__RequestVerificationToken", token)));

        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        Assert.Equal("/applications", cancelled.Headers.Location?.OriginalString);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await database.JobApplications.AnyAsync(item => item.SourceText == source));
        Assert.False(await database.Companies.AnyAsync(item => item.Name == uniqueCompany));
        Assert.False(await database.ExtractionDrafts.AnyAsync(item => item.Id == draftId));
    }

    [Fact]
    public async Task InvalidReview_PreservesCorrectionsAndDoesNotCreateApplication()
    {
        var client = await CreateAuthenticatedClientAsync();
        const string source = "Job Title: Validation Engineer\nA deliberately sparse synthetic posting.";
        var draftLocation = await PostPastedTextAsync(client, source);
        var review = await client.GetAsync(draftLocation);
        var token = ExtractAntiforgeryToken(await review.Content.ReadAsStringAsync());

        var invalid = await client.PostAsync(
            draftLocation,
            Form(
                ("RoleTitle", ""),
                ("CompanyName", ""),
                ("CompanyLocation", "Preserved location"),
                ("AppliedOn", "2026-08-04"),
                ("ContactEmail", "not-an-email"),
                ("Notes", "Preserve this correction"),
                ("__RequestVerificationToken", token)));
        var invalidContent = await invalid.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Role title field is required", invalidContent, StringComparison.Ordinal);
        Assert.Contains("Enter a company name", invalidContent, StringComparison.Ordinal);
        Assert.Contains("Preserve this correction", invalidContent, StringComparison.Ordinal);
        Assert.Contains("Job Title: Validation Engineer", invalidContent, StringComparison.Ordinal);
        Assert.Contains("deliberately sparse synthetic posting", invalidContent, StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await database.JobApplications.AnyAsync(item => item.SourceText == source));
    }

    private async Task<string> PostPastedTextAsync(HttpClient client, string sourceText)
    {
        var page = await client.GetAsync("/applications/import/text");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/applications/import/text",
            Form(
                ("SourceText", sourceText),
                ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        return response.Headers.Location.OriginalString;
    }

    private static Guid DraftIdFromLocation(string location)
    {
        var segments = location.Trim('/').Split('/');
        Assert.True(segments.Length >= 2);
        Assert.True(Guid.TryParse(segments[^2], out var id));
        return id;
    }
}
