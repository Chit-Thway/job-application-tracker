using System.Net;
using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    [Fact]
    public async Task DeletionWarning_CanBeDismissedWithoutCancellingDeletion()
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
                ("RoleTitle", "Dismissible Retention Warning"),
                ("AppliedOn", today.AddMonths(-3).ToString("yyyy-MM-dd")),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", createToken)));
        var applicationId = IdFromLocation(createResponse.Headers.Location!.OriginalString);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var application = await database.JobApplications.SingleAsync(item => item.Id == applicationId);
            application.Outcome = ApplicationOutcome.Withdrawn;
            await database.SaveChangesAsync();
        }

        var actionPage = await client.GetAsync("/actions");
        var actionContent = await actionPage.Content.ReadAsStringAsync();
        Assert.Contains("Dismissible Retention Warning", actionContent, StringComparison.Ordinal);
        Assert.Contains("Dismiss", actionContent, StringComparison.Ordinal);
        var actionToken = ExtractAntiforgeryToken(actionContent);

        var dismissResponse = await client.PostAsync(
            $"/actions/applications/{applicationId}/dismiss-deletion-warning",
            Form(("__RequestVerificationToken", actionToken)));

        Assert.Equal(HttpStatusCode.Redirect, dismissResponse.StatusCode);
        Assert.Equal("/actions", dismissResponse.Headers.Location?.OriginalString);
        actionContent = await (await client.GetAsync("/actions")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Dismissible Retention Warning", actionContent, StringComparison.Ordinal);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var persisted = await verificationDatabase.JobApplications.SingleAsync(item => item.Id == applicationId);
        Assert.NotNull(persisted.DeletionScheduledAt);
        Assert.NotNull(persisted.DeletionWarningDismissedAt);
    }

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
        Assert.Contains("3 months, then a full 14-day warning", settingsContent, StringComparison.Ordinal);
        Assert.Contains("Save retention settings", settingsContent, StringComparison.Ordinal);
        Assert.Contains("Retention service", settingsContent, StringComparison.Ordinal);
        Assert.Contains("Privacy Policy", settingsContent, StringComparison.Ordinal);
        Assert.Contains("Cookie Consent", settingsContent, StringComparison.Ordinal);
        Assert.Contains("Terms of Service", settingsContent, StringComparison.Ordinal);
        Assert.Contains("strictly necessary cookies", settingsContent, StringComparison.Ordinal);

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

    [Fact]
    public async Task Settings_UpdateRetentionChoices_AndRecalculatePendingSchedule()
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
                ("RoleTitle", "Configurable Retention Test"),
                ("AppliedOn", today.AddMonths(-3).ToString("yyyy-MM-dd")),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", createToken)));
        var applicationId = IdFromLocation(createResponse.Headers.Location!.OriginalString);
        var settingsPage = await client.GetAsync("/settings");
        var settingsToken = ExtractAntiforgeryToken(
            await settingsPage.Content.ReadAsStringAsync());
        var beforeUpdate = TimeProvider.System.GetUtcNow();

        var updateResponse = await client.PostAsync(
            "/settings/retention",
            Form(
                ("retentionMonths", "1"),
                ("deletionGraceDays", "3"),
                ("__RequestVerificationToken", settingsToken)));

        Assert.Equal(HttpStatusCode.Redirect, updateResponse.StatusCode);
        Assert.Equal("/settings", updateResponse.Headers.Location?.OriginalString);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var application = await database.JobApplications
            .SingleAsync(item => item.Id == applicationId);
        var owner = await database.Users.SingleAsync(item => item.Id == application.OwnerId);
        Assert.Equal(1, owner.RetentionMonths);
        Assert.Equal(3, owner.DeletionGraceDays);
        Assert.NotNull(application.DeletionScheduledAt);
        Assert.InRange(
            application.DeletionScheduledAt.Value,
            beforeUpdate.AddDays(3),
            TimeProvider.System.GetUtcNow().AddDays(3).AddSeconds(1));

        var refreshedSettings = await client.GetAsync("/settings");
        var refreshedContent = await refreshedSettings.Content.ReadAsStringAsync();
        Assert.Contains("1 month, then a full 3-day warning", refreshedContent, StringComparison.Ordinal);
        Assert.Contains("Pending schedules were recalculated from today", refreshedContent, StringComparison.Ordinal);
    }
}
