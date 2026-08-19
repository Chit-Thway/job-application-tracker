using System.Net;
using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    [Fact]
    public async Task DashboardAndActionCentre_SupportAttentionActionsAndReversibleGhosting()
    {
        var client = await CreateAuthenticatedClientAsync();
        var today = DashboardCalendar.Create(
            TimeProvider.System.GetUtcNow(),
            "Australia/Perth").Today;
        var appliedOn = today.AddDays(-31);
        var dueAt = today.AddDays(-1).ToDateTime(new TimeOnly(9, 0));

        var createPage = await client.GetAsync("/applications/new");
        var createToken = ExtractAntiforgeryToken(await createPage.Content.ReadAsStringAsync());
        var createResponse = await client.PostAsync(
            "/applications/new",
            Form(
                ("RoleTitle", "Synthetic Dashboard Coordinator"),
                ("AppliedOn", appliedOn.ToString("yyyy-MM-dd")),
                ("Notes", "Milestone 7 dashboard workflow"),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", createToken)));
        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var applicationLocation = createResponse.Headers.Location?.OriginalString;
        Assert.NotNull(applicationLocation);
        var applicationId = IdFromLocation(applicationLocation);

        var detailsPage = await client.GetAsync(applicationLocation);
        var initialDetailsContent = await detailsPage.Content.ReadAsStringAsync();
        Assert.Contains("<span class=\"count-badge\">0</span>", initialDetailsContent, StringComparison.Ordinal);
        Assert.DoesNotContain("0 open", initialDetailsContent, StringComparison.Ordinal);
        var detailsToken = ExtractAntiforgeryToken(initialDetailsContent);
        var taskResponse = await client.PostAsync(
            $"/applications/{applicationId}/tasks",
            Form(
                ("Task.Title", "Send dashboard follow-up"),
                ("Task.DueAtLocal", dueAt.ToString("yyyy-MM-ddTHH:mm")),
                ("Task.Notes", "Synthetic overdue action"),
                ("__RequestVerificationToken", detailsToken)));
        Assert.Equal(HttpStatusCode.Redirect, taskResponse.StatusCode);

        Guid taskId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            taskId = await database.Tasks
                .Where(item => item.JobApplicationId == applicationId)
                .Select(item => item.Id)
                .SingleAsync();
        }

        detailsPage = await client.GetAsync(applicationLocation);
        var statusToken = ExtractAntiforgeryToken(await detailsPage.Content.ReadAsStringAsync());
        var recruiterContactResponse = await client.PostAsync(
            $"/applications/{applicationId}/status",
            Form(
                ("Status.Stage", PipelineStage.Screening.ToString()),
                ("Status.Outcome", ApplicationOutcome.Active.ToString()),
                ("Status.Note", "Recruiter made contact."),
                ("__RequestVerificationToken", statusToken)));
        Assert.Equal(HttpStatusCode.Redirect, recruiterContactResponse.StatusCode);

        var secondCreatePage = await client.GetAsync("/applications/new");
        var secondCreateToken = ExtractAntiforgeryToken(await secondCreatePage.Content.ReadAsStringAsync());
        var secondCreateResponse = await client.PostAsync(
            "/applications/new",
            Form(
                ("RoleTitle", "Synthetic Applied Analyst"),
                ("AppliedOn", today.ToString("yyyy-MM-dd")),
                ("Notes", "Keeps the dashboard pipeline multi-stage."),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", secondCreateToken)));
        Assert.Equal(HttpStatusCode.Redirect, secondCreateResponse.StatusCode);

        var dashboardPage = await client.GetAsync("/dashboard");
        var dashboardContent = await dashboardPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dashboardPage.StatusCode);
        Assert.Contains("Three-month dashboard", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("Synthetic Dashboard Coordinator", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("Send dashboard follow-up", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("class=\"pipeline-donut\"", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("data-pipeline-label=\"Applied\"", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("data-pipeline-label=\"Screening\"", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Applied: 1 application\"", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Screening: 1 application\"", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("<path class=\"pipeline-donut-segment pipeline-stage-0\"", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("<path class=\"pipeline-donut-segment pipeline-stage-1\"", dashboardContent, StringComparison.Ordinal);
        Assert.Contains("/js/dashboard-pipeline.js", dashboardContent, StringComparison.Ordinal);

        var actionPage = await client.GetAsync("/actions");
        var actionContent = await actionPage.Content.ReadAsStringAsync();
        var actionToken = ExtractAntiforgeryToken(actionContent);
        Assert.Equal(HttpStatusCode.OK, actionPage.StatusCode);
        Assert.Contains("30-day decision", actionContent, StringComparison.Ordinal);
        Assert.Contains("Confirm Ghosted", actionContent, StringComparison.Ordinal);
        Assert.Contains("Send dashboard follow-up", actionContent, StringComparison.Ordinal);

        var completeResponse = await client.PostAsync(
            $"/actions/applications/{applicationId}/tasks/{taskId}/completed",
            Form(("__RequestVerificationToken", actionToken)));
        Assert.Equal(HttpStatusCode.Redirect, completeResponse.StatusCode);
        Assert.Equal("/actions", completeResponse.Headers.Location?.OriginalString);

        actionPage = await client.GetAsync("/actions");
        actionContent = await actionPage.Content.ReadAsStringAsync();
        actionToken = ExtractAntiforgeryToken(actionContent);
        var ghostResponse = await client.PostAsync(
            $"/actions/applications/{applicationId}/confirm-ghosted",
            Form(("__RequestVerificationToken", actionToken)));
        Assert.Equal(HttpStatusCode.Redirect, ghostResponse.StatusCode);
        Assert.Equal("/actions", ghostResponse.Headers.Location?.OriginalString);

        detailsPage = await client.GetAsync(applicationLocation);
        var reversedToken = ExtractAntiforgeryToken(await detailsPage.Content.ReadAsStringAsync());
        var reverseResponse = await client.PostAsync(
            $"/applications/{applicationId}/status",
            Form(
                ("Status.Stage", PipelineStage.Applied.ToString()),
                ("Status.Outcome", ApplicationOutcome.Active.ToString()),
                ("Status.Note", "Employer replied after the Ghosted decision."),
                ("__RequestVerificationToken", reversedToken)));
        Assert.Equal(HttpStatusCode.Redirect, reverseResponse.StatusCode);

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        var application = await verificationDatabase.JobApplications
            .SingleAsync(item => item.Id == applicationId);
        var history = await verificationDatabase.StatusHistory
            .Where(item => item.JobApplicationId == applicationId)
            .OrderBy(item => item.EffectiveAt)
            .ToListAsync();
        var task = await verificationDatabase.Tasks.SingleAsync(item => item.Id == taskId);

        Assert.Equal(ApplicationOutcome.Active, application.Outcome);
        Assert.NotNull(task.CompletedAt);
        Assert.Contains(history, item => item.NewOutcome == ApplicationOutcome.Ghosted);
        Assert.Contains(history, item => item.NewOutcome == ApplicationOutcome.Active);
    }
}
