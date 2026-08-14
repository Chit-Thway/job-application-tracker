using System.Net;
using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    [Fact]
    public async Task ApplicationLibrary_SupportsViewSelectionAndThreeBulkActions()
    {
        var client = await CreateAuthenticatedClientAsync();
        var firstId = await CreateApplicationAsync(client, "Synthetic Bulk First");
        var secondId = await CreateApplicationAsync(client, "Synthetic Bulk Second");

        var index = await client.GetAsync("/applications");
        var indexContent = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("data-application-view=\"cards\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("data-application-view=\"list\"", indexContent, StringComparison.Ordinal);
        Assert.Contains("data-selection-toggle", indexContent, StringComparison.Ordinal);
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
            Assert.Equal(PipelineStage.Screening, first.Stage);
            Assert.False(await database.JobApplications.AnyAsync(item => item.Id == secondId));
            Assert.Contains(database.StatusHistory, item =>
                item.JobApplicationId == firstId
                && item.NewStage == PipelineStage.Screening);
            Assert.Contains(database.StatusHistory, item =>
                item.JobApplicationId == firstId
                && item.Note == "Shared bulk workflow note."
                && item.NewStage == null);
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
