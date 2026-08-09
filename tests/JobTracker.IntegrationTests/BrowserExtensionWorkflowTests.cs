using System.Net;
using System.Text.Json;
using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    [Fact]
    public async Task AuthenticatedUser_CanReviewAndCancelBrowserCaptureWithoutSaving()
    {
        var pageUrl = $"https://jobs.example.test/browser-capture/{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new
        {
            version = 1,
            pageUrl,
            roleTitle = "Synthetic Support Engineer",
            companyName = "Synthetic Capture Company",
            companyLocation = "Perth, WA",
            workplaceMode = "Hybrid",
            sourceSite = "Synthetic Jobs",
            jobReference = "CAP-510",
            salaryText = "AUD 75,000 - 82,000 per year",
            employmentType = "Full time",
            closingDate = "2026-08-30",
            sourceText = "Synthetic Support Engineer\nSynthetic Capture Company\nA deterministic browser capture fixture.",
        });
        var client = await CreateAuthenticatedClientAsync();
        var landing = await client.GetAsync("/applications/import/extension");
        var landingContent = await landing.Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(landingContent);

        Assert.True(
            landing.StatusCode == HttpStatusCode.OK,
            $"Expected extension landing page but received {landing.StatusCode}: {landingContent}");
        Assert.Contains("active tab only", landingContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/js/extension-import", landingContent, StringComparison.Ordinal);

        var imported = await client.PostAsync(
            "/applications/import/extension",
            Form(("PayloadJson", payload), ("__RequestVerificationToken", token)));

        Assert.Equal(HttpStatusCode.Redirect, imported.StatusCode);
        Assert.NotNull(imported.Headers.Location);
        var review = await client.GetAsync(imported.Headers.Location);
        var reviewContent = await review.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Contains("Captured job-page text", reviewContent, StringComparison.Ordinal);
        Assert.Contains("Synthetic Support Engineer", reviewContent, StringComparison.Ordinal);
        Assert.Contains("Synthetic Capture Company", reviewContent, StringComparison.Ordinal);
        Assert.Contains("AUD 75,000 - 82,000 per year", reviewContent, StringComparison.Ordinal);

        var draftId = DraftIdFromLocation(imported.Headers.Location.OriginalString);
        await using (var beforeScope = factory.Services.CreateAsyncScope())
        {
            var database = beforeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var draft = await database.ExtractionDrafts.SingleAsync(item => item.Id == draftId);
            Assert.Equal(ExtractionSourceType.BrowserExtension, draft.SourceType);
            Assert.False(await database.JobApplications.AnyAsync(item => item.SourceUrl == pageUrl));
        }

        var reviewToken = ExtractAntiforgeryToken(reviewContent);
        var cancelled = await client.PostAsync(
            $"/applications/import/{draftId}/cancel",
            Form(("__RequestVerificationToken", reviewToken)));

        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        await using var afterScope = factory.Services.CreateAsyncScope();
        var afterDatabase = afterScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await afterDatabase.ExtractionDrafts.AnyAsync(item => item.Id == draftId));
        Assert.False(await afterDatabase.JobApplications.AnyAsync(item => item.SourceUrl == pageUrl));
    }

    [Fact]
    public async Task InvalidBrowserCapture_ShowsSafeRetryMessage()
    {
        var client = await CreateAuthenticatedClientAsync();
        var landing = await client.GetAsync("/applications/import/extension");
        var token = ExtractAntiforgeryToken(await landing.Content.ReadAsStringAsync());

        var imported = await client.PostAsync(
            "/applications/import/extension",
            Form(("PayloadJson", "not-json"), ("__RequestVerificationToken", token)));
        var content = await imported.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        Assert.Contains("capture could not be read", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Paste job text instead", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowserCapturePost_WithoutAntiforgeryToken_IsRejectedBeforeDraftCreation()
    {
        var marker = $"antiforgery-{Guid.NewGuid():N}";
        var payload = JsonSerializer.Serialize(new
        {
            version = 1,
            pageUrl = $"https://jobs.example.test/{marker}",
            roleTitle = "Synthetic Protected Role",
            sourceText = $"Synthetic Protected Role\n{marker}\nA valid capture that must not be accepted without its token.",
        });
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync(
            "/applications/import/extension",
            Form(("PayloadJson", payload)));

        Assert.InRange((int)response.StatusCode, 400, 499);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await database.ExtractionDrafts.AnyAsync(draft =>
            draft.SourceText.Contains(marker)));
    }
}
