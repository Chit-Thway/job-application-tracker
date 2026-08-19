using System.Net;
using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    [Fact]
    public async Task ApplicationLibrary_SupportsListStatusEditingAndFiveBulkActions()
    {
        var client = await CreateAuthenticatedClientAsync();
        var firstId = await CreateApplicationAsync(client, "Synthetic Bulk First");
        var secondId = await CreateApplicationAsync(client, "Synthetic Bulk Second");

        var index = await client.GetAsync("/applications");
        var indexContent = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("data-application-view=\"cards\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("data-application-view=\"list\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("data-application-view=\"list\" aria-pressed=\"true\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("application-results is-list-view", indexContent, StringComparison.Ordinal);
        Assert.Contains("data-selection-toggle", indexContent, StringComparison.Ordinal);
        Assert.Contains("class=\"application-rank\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("formaction=\"/applications/bulk/saved\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("data-inline-status-form", indexContent, StringComparison.Ordinal);
        Assert.Contains("Save status", indexContent, StringComparison.Ordinal);
        Assert.Contains("data-status-discard", indexContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<option value=\"RecruiterContact\"", indexContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<option value=\"ReferenceCheck\"", indexContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<option value=\"OfferDeclined\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("All saved", indexContent, StringComparison.Ordinal);
        Assert.Contains("All unsaved", indexContent, StringComparison.Ordinal);
        Assert.Contains("All deletion scheduled", indexContent, StringComparison.Ordinal);
        Assert.Contains("/js/application-index.js", indexContent, StringComparison.Ordinal);
        var token = ExtractAntiforgeryToken(indexContent);

        var stageResponse = await client.PostAsync(
            "/applications/bulk/stage",
            Form(
                ("SelectedApplicationIds", firstId.ToString()),
                ("SelectedApplicationIds", secondId.ToString()),
                ("Stage", PipelineStage.Screening.ToString()),
                ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, stageResponse.StatusCode);

        index = await client.GetAsync("/applications");
        token = ExtractAntiforgeryToken(await index.Content.ReadAsStringAsync());
        var noteResponse = await client.PostAsync(
            "/applications/bulk/note",
            Form(
                ("SelectedApplicationIds", firstId.ToString()),
                ("SelectedApplicationIds", secondId.ToString()),
                ("Note", "Shared bulk workflow note."),
                ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, noteResponse.StatusCode);

        index = await client.GetAsync("/applications");
        token = ExtractAntiforgeryToken(await index.Content.ReadAsStringAsync());
        var saveResponse = await client.PostAsync(
            "/applications/bulk/saved",
            Form(
                ("SelectedApplicationIds", firstId.ToString()),
                ("SelectedApplicationIds", secondId.ToString()),
                ("isSavedForever", "true"),
                ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);

        index = await client.GetAsync("/applications");
        token = ExtractAntiforgeryToken(await index.Content.ReadAsStringAsync());
        var unsaveResponse = await client.PostAsync(
            "/applications/bulk/saved",
            Form(
                ("SelectedApplicationIds", secondId.ToString()),
                ("isSavedForever", "false"),
                ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, unsaveResponse.StatusCode);

        index = await client.GetAsync("/applications");
        token = ExtractAntiforgeryToken(await index.Content.ReadAsStringAsync());
        var inlineStatusResponse = await client.PostAsync(
            $"/applications/{firstId}/status",
            Form(
                ("Status.Stage", PipelineStage.Interview.ToString()),
                ("Status.Outcome", ApplicationOutcome.Active.ToString()),
                ("Status.Note", "Interview booked from the application list."),
                ("returnTo", "index"),
                ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, inlineStatusResponse.StatusCode);
        Assert.Equal("/applications", inlineStatusResponse.Headers.Location?.OriginalString);

        index = await client.GetAsync("/applications");
        token = ExtractAntiforgeryToken(await index.Content.ReadAsStringAsync());
        var deleteReview = await client.PostAsync(
            "/applications/bulk/delete",
            Form(
                ("SelectedApplicationIds", secondId.ToString()),
                ("__RequestVerificationToken", token)));
        var deleteContent = await deleteReview.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, deleteReview.StatusCode);
        Assert.Contains("Delete 1 selected application?", deleteContent, StringComparison.Ordinal);
        Assert.Contains("Synthetic Bulk Second", deleteContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic Bulk First", deleteContent, StringComparison.Ordinal);
        var deleteToken = ExtractAntiforgeryToken(deleteContent);

        var deleteResponse = await client.PostAsync(
            "/applications/bulk/delete/confirm",
            Form(
                ("SelectedApplicationIds", secondId.ToString()),
                ("__RequestVerificationToken", deleteToken)));
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var first = await database.JobApplications.SingleAsync(item => item.Id == firstId);
            Assert.Equal(PipelineStage.Interview, first.Stage);
            Assert.True(first.IsSavedForever);
            Assert.Null(first.DeletionScheduledAt);
            Assert.False(await database.JobApplications.AnyAsync(item => item.Id == secondId));
            Assert.Contains(database.StatusHistory, item =>
                item.JobApplicationId == firstId
                && item.NewStage == PipelineStage.Screening);
            Assert.Contains(database.StatusHistory, item =>
                item.JobApplicationId == firstId
                && item.Note == "Shared bulk workflow note."
                && item.NewStage == null);
            Assert.Contains(database.StatusHistory, item =>
                item.JobApplicationId == firstId
                && item.NewStage == PipelineStage.Interview
                && item.Note == "Interview booked from the application list.");
        }

        var details = await client.GetAsync($"/applications/{firstId}");
        var detailsContent = await details.Content.ReadAsStringAsync();
        Assert.Contains("Note added", detailsContent, StringComparison.Ordinal);
        Assert.Contains("Shared bulk workflow note.", detailsContent, StringComparison.Ordinal);
    }

    private async Task<Guid> CreateApplicationAsync(HttpClient client, string roleTitle)
    {
        var page = await client.GetAsync("/applications/new");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/applications/new",
            Form(
                ("RoleTitle", roleTitle),
                ("AppliedOn", "2026-08-14"),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return IdFromLocation(response.Headers.Location!.OriginalString);
    }
}
