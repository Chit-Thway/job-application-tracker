using System.Net;
using JobTracker.Web.Data;
using JobTracker.Web.Extraction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    private const string StructuredUrlPosting = """
        <!doctype html>
        <html>
        <head>
          <meta property="og:site_name" content="Synthetic Graduate Board">
          <script type="application/ld+json">
          {
            "@context": "https://schema.org",
            "@type": "JobPosting",
            "title": "2027 Graduate Consultant",
            "description": "<p>Join a fictional graduate consulting program.</p>",
            "hiringOrganization": { "@type": "Organization", "name": "Escient" },
            "jobLocation": { "address": { "addressLocality": "Sydney", "addressRegion": "NSW", "addressCountry": "AU" } },
            "employmentType": "FULL_TIME",
            "baseSalary": { "currency": "AUD", "value": { "minValue": 70000, "maxValue": 75000, "unitText": "YEAR" } },
            "validThrough": "2026-08-16"
          }
          </script>
        </head>
        <body><h1>Graduate Program</h1><p>Public synthetic fixture.</p></body>
        </html>
        """;

    [Fact]
    public async Task AuthenticatedUser_CanReviewAndConfirmStructuredUrlImport()
    {
        var url = $"https://jobs.example.test/graduate/{Guid.NewGuid():N}";
        factory.JobPostingFetcher.AddSuccess(url, StructuredUrlPosting);
        var client = await CreateAuthenticatedClientAsync();
        var page = await client.GetAsync("/applications/import/url");
        var pageContent = await page.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(pageContent);

        var imported = await client.PostAsync(
            "/applications/import/url",
            Form(("SourceUrl", url), ("__RequestVerificationToken", token)));

        Assert.Equal(HttpStatusCode.Redirect, imported.StatusCode);
        Assert.NotNull(imported.Headers.Location);
        var review = await client.GetAsync(imported.Headers.Location);
        var reviewContent = await review.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("Fetched job-page text", reviewContent, StringComparison.Ordinal);
        Assert.Contains("2027 Graduate Consultant", reviewContent, StringComparison.Ordinal);
        Assert.Contains("Escient", reviewContent, StringComparison.Ordinal);
        Assert.Contains("Sydney, NSW, AU", reviewContent, StringComparison.Ordinal);
        Assert.Contains("AUD 70,000 - 75,000 / Year", reviewContent, StringComparison.Ordinal);
        Assert.Contains("2026-08-16", reviewContent, StringComparison.Ordinal);
        Assert.Contains("official JobPosting hiring organisation", reviewContent, StringComparison.Ordinal);

        var draftId = DraftIdFromLocation(imported.Headers.Location.OriginalString);
        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            var database = beforeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await database.ExtractionDrafts.SingleAsync(item => item.Id == draftId);
            Assert.Equal(ExtractionSourceType.JobPostingUrl, draft.SourceType);
            Assert.Contains("Public synthetic fixture", draft.SourceText, StringComparison.Ordinal);
            Assert.False(await database.JobApplications.AnyAsync(item => item.SourceUrl == url));
        }

        var reviewToken = ExtractAntiforgeryToken(reviewContent);
        var confirmed = await client.PostAsync(
            imported.Headers.Location,
            Form(
                ("DraftId", draftId.ToString()),
                ("RoleTitle", "2027 Graduate Consultant"),
                ("CompanyName", "Escient"),
                ("CompanyLocation", "Sydney, NSW, AU"),
                ("AppliedOn", "2026-08-05"),
                ("SourceUrl", url),
                ("SourceSite", "Synthetic Graduate Board"),
                ("SalaryText", "AUD 70,000 - 75,000 / Year"),
                ("EmploymentType", "Full-time"),
                ("ClosingDate", "2026-08-16"),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", reviewToken)));

        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        await using var afterScope = factory.Services.CreateAsyncScope();
        var afterDatabase = afterScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var application = await afterDatabase.JobApplications.SingleAsync(item => item.SourceUrl == url);
        var company = await afterDatabase.Companies.SingleAsync(item => item.Id == application.CompanyId);
        var history = await afterDatabase.StatusHistory.SingleAsync(item => item.JobApplicationId == application.Id);
        Assert.Equal("Escient", company.Name);
        Assert.Contains("reviewed public job URL", history.Note, StringComparison.Ordinal);
        Assert.False(await afterDatabase.ExtractionDrafts.AnyAsync(item => item.Id == draftId));
    }

    [Fact]
    public async Task BlockedOrUnavailablePage_ShowsPasteAndManualFallbacks()
    {
        var url = $"https://blocked.example.test/job/{Guid.NewGuid():N}";
        factory.JobPostingFetcher.AddFailure(url, JobPostingImportFailure.RequestFailed);
        var client = await CreateAuthenticatedClientAsync();
        var page = await client.GetAsync("/applications/import/url");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

        var imported = await client.PostAsync(
            "/applications/import/url",
            Form(("SourceUrl", url), ("__RequestVerificationToken", token)));
        var content = await imported.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Contains("could not safely read", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Paste job text", content, StringComparison.Ordinal);
        Assert.Contains("Use manual entry", content, StringComparison.Ordinal);
    }
}
