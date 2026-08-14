using System.Net;
using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    [Fact]
    public async Task DeletionScheduled_IsVisibleEverywhere_AndSaveThenUnsaveIsReversible()
    {
        var client = await CreateAuthenticatedClientAsync();
        var today = DashboardCalendar.Create(
            TimeProvider.System.GetUtcNow(),
            "Australia/Perth").Today;
        var createPage = await client.GetAsync("/applications/new");
        var createToken = ExtractAntiforgeryToken(await createPage.Content.ReadAsStringAsync());
        var createResponse = await client.PostAsync(
            "/applications/new",
            Form(
                ("RoleTitle", "Synthetic Retention Engineer"),
                ("AppliedOn", today.AddMonths(-3).ToString("yyyy-MM-dd")),
                ("Notes", "Milestone 8 retention acceptance"),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", createToken)));
        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var location = createResponse.Headers.Location?.OriginalString;
        Assert.NotNull(location);
        var applicationId = IdFromLocation(location);

        DateTimeOffset originalDeadline;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            originalDeadline = (await database.JobApplications
                .SingleAsync(item => item.Id == applicationId))
                .DeletionScheduledAt!.Value;
        }
        var deadlineText = ApplicationTime.Display(originalDeadline, "Australia/Perth");

        var details = await client.GetAsync(location);
        var detailsContent = await details.Content.ReadAsStringAsync();
        Assert.Contains("Deletion scheduled", detailsContent, StringComparison.Ordinal);
        Assert.Contains("Scheduled for automatic deletion", detailsContent, StringComparison.Ordinal);
        Assert.Contains(deadlineText, detailsContent, StringComparison.Ordinal);

        var filtered = await client.GetAsync("/applications?IsDeletionScheduled=true");
        var filteredContent = await filtered.Content.ReadAsStringAsync();
        Assert.Contains("Synthetic Retention Engineer", filteredContent, StringComparison.Ordinal);
        Assert.Contains(deadlineText, filteredContent, StringComparison.Ordinal);

        var dashboard = await client.GetAsync("/dashboard");
        var dashboardContent = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("Deletion scheduled after", dashboardContent, StringComparison.Ordinal);
        Assert.Contains(deadlineText, dashboardContent, StringComparison.Ordinal);

        var settings = await client.GetAsync("/settings");
        var settingsContent = await settings.Content.ReadAsStringAsync();
        Assert.Contains("Three months, then a full 14-day warning", settingsContent, StringComparison.Ordinal);
        Assert.Contains("Retention service", settingsContent, StringComparison.Ordinal);

        var actionCentre = await client.GetAsync("/actions");
        var actionContent = await actionCentre.Content.ReadAsStringAsync();
        Assert.Contains("Synthetic Retention Engineer", actionContent, StringComparison.Ordinal);
        Assert.Contains("Save and keep", actionContent, StringComparison.Ordinal);
        Assert.Contains(deadlineText, actionContent, StringComparison.Ordinal);
        var actionToken = ExtractAntiforgeryToken(actionContent);
        var saveResponse = await client.PostAsync(
            $"/applications/{applicationId}/saved",
            Form(
                ("isSavedForever", "true"),
                ("returnTo", "actions"),
                ("__RequestVerificationToken", actionToken)));
        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        Assert.Equal("/actions", saveResponse.Headers.Location?.OriginalString);

        details = await client.GetAsync(location);
        detailsContent = await details.Content.ReadAsStringAsync();
        Assert.Contains("This application is saved", detailsContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Scheduled for automatic deletion", detailsContent, StringComparison.Ordinal);
        var detailsToken = ExtractAntiforgeryToken(detailsContent);
        var beforeUnsave = TimeProvider.System.GetUtcNow();
        var unsaveResponse = await client.PostAsync(
            $"/applications/{applicationId}/saved",
            Form(
                ("isSavedForever", "false"),
                ("__RequestVerificationToken", detailsToken)));
        Assert.Equal(HttpStatusCode.Redirect, unsaveResponse.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        var application = await verificationDatabase.JobApplications
            .SingleAsync(item => item.Id == applicationId);
        Assert.False(application.IsSavedForever);
        Assert.NotNull(application.DeletionScheduledAt);
        Assert.InRange(
            application.DeletionScheduledAt.Value,
            beforeUnsave.AddDays(14),
            TimeProvider.System.GetUtcNow().AddDays(14).AddSeconds(1));
    }
}
