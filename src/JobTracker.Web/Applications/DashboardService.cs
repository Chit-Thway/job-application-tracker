using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Applications;

public sealed class DashboardService(
    ApplicationDbContext database,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    public async Task<DashboardSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var user = await GetUserAsync(ownerId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var window = DashboardCalendar.Create(now, user.TimeZoneId);

        var applications = await GetApplicationsAsync(ownerId, cancellationToken);
        var companies = await GetCompaniesAsync(ownerId, cancellationToken);
        var interactions = await GetInteractionsAsync(ownerId, cancellationToken);
        var tasks = await GetOpenTasksAsync(ownerId, cancellationToken);
        var appointments = await GetAppointmentsAsync(ownerId, cancellationToken);
        var statusHistory = await GetStatusHistoryAsync(ownerId, cancellationToken);

        var applicationById = applications.ToDictionary(item => item.Id);
        var firstResponses = FirstResponses(applications, interactions, window.TimeZoneId);
        var windowApplications = applications
            .Where(item => window.Contains(item.AppliedOn))
            .OrderByDescending(item => item.AppliedOn)
            .ThenBy(item => item.RoleTitle)
            .ToList();
        var windowApplicationIds = windowApplications.Select(item => item.Id).ToHashSet();
        var pipelineApplications = applications
            .Where(item => window.Contains(item.AppliedOn) || item.IsSavedForever)
            .OrderByDescending(item => item.AppliedOn)
            .ThenBy(item => item.RoleTitle)
            .ToList();
        var pipelineApplicationIds = pipelineApplications.Select(item => item.Id).ToHashSet();

        var openTasks = BuildOpenTasks(tasks, applicationById, companies, now);
        var upcomingAppointments = BuildUpcomingAppointments(
            appointments,
            applicationById,
            companies,
            now);
        var ghostingAttention = BuildGhostingAttention(
            applications,
            companies,
            firstResponses,
            window.Today);
        var deletionScheduled = BuildDeletionScheduled(applications, companies);

        var recentActivity = BuildActivity(
            window,
            pipelineApplications,
            companies,
            interactions,
            statusHistory,
            applicationById,
            pipelineApplicationIds);

        return new DashboardSnapshot(
            user.DisplayName,
            window,
            windowApplications.Count,
            windowApplications.Count(item => firstResponses.ContainsKey(item.Id)),
            appointments.Count(item =>
                item.Type == AppointmentType.Interview
                && windowApplicationIds.Contains(item.JobApplicationId)
                && window.Contains(item.StartsAt)),
            BuildMonthSummaries(window, windowApplications),
            BuildStageSummaries(pipelineApplications),
            BuildOutcomeSummaries(pipelineApplications),
            BuildRecentApplications(windowApplications, companies, firstResponses),
            recentActivity,
            openTasks,
            upcomingAppointments,
            ghostingAttention,
            deletionScheduled);
    }

    private async Task<DashboardUser> GetUserAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => new DashboardUser(user.DisplayName, user.TimeZoneId))
            .SingleAsync(cancellationToken);

    private async Task<IReadOnlyList<JobApplication>> GetApplicationsAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.JobApplications
            .AsNoTracking()
            .Where(application => application.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, string>> GetCompaniesAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Companies
            .AsNoTracking()
            .Where(company => company.OwnerId == ownerId)
            .ToDictionaryAsync(company => company.Id, company => company.Name, cancellationToken);

    private async Task<IReadOnlyList<Interaction>> GetInteractionsAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Interactions
            .AsNoTracking()
            .Where(interaction => interaction.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<TaskItem>> GetOpenTasksAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Tasks
            .AsNoTracking()
            .Where(task => task.OwnerId == ownerId && task.CompletedAt == null)
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<Appointment>> GetAppointmentsAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<StatusHistory>> GetStatusHistoryAsync(
        string ownerId,
        CancellationToken cancellationToken) =>
        await database.StatusHistory
            .AsNoTracking()
            .Where(history => history.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

    private static IReadOnlyList<DashboardTaskSummary> BuildOpenTasks(
        IEnumerable<TaskItem> tasks,
        IReadOnlyDictionary<Guid, JobApplication> applicationsById,
        IReadOnlyDictionary<Guid, string> companies,
        DateTimeOffset currentTime) =>
        tasks
            .Where(task => applicationsById.ContainsKey(task.JobApplicationId))
            .Select(task => TaskSummary(
                task,
                applicationsById[task.JobApplicationId],
                companies,
                currentTime))
            .OrderByDescending(task => task.IsOverdue)
            .ThenBy(task => task.DueAt is null)
            .ThenBy(task => task.DueAt)
            .ThenBy(task => task.Title)
            .ToList();

    private static IReadOnlyList<DashboardAppointmentSummary> BuildUpcomingAppointments(
        IEnumerable<Appointment> appointments,
        IReadOnlyDictionary<Guid, JobApplication> applicationsById,
        IReadOnlyDictionary<Guid, string> companies,
        DateTimeOffset currentTime) =>
        appointments
            .Where(appointment =>
                appointment.EndsAt >= currentTime
                && applicationsById.ContainsKey(appointment.JobApplicationId))
            .Select(appointment => AppointmentSummary(
                appointment,
                applicationsById[appointment.JobApplicationId],
                companies))
            .OrderBy(appointment => appointment.StartsAt)
            .ToList();

    private static IReadOnlyList<DashboardGhostingSummary> BuildGhostingAttention(
        IEnumerable<JobApplication> applications,
        IReadOnlyDictionary<Guid, string> companies,
        IReadOnlyDictionary<Guid, DateTimeOffset> firstResponses,
        DateOnly today) =>
        applications
            .Select(application => new
            {
                Application = application,
                Assessment = DashboardCalendar.AssessGhosting(
                    application.AppliedOn,
                    today,
                    application.Outcome,
                    firstResponses.ContainsKey(application.Id)),
            })
            .Where(item => item.Assessment.Kind != GhostingAttentionKind.None)
            .OrderByDescending(item => item.Assessment.Kind)
            .ThenByDescending(item => item.Assessment.DaysSinceApplied)
            .Select(item => new DashboardGhostingSummary(
                item.Application.Id,
                item.Application.RoleTitle,
                CompanyName(item.Application, companies),
                item.Application.AppliedOn,
                item.Assessment.Kind,
                item.Assessment.DaysSinceApplied))
            .ToList();

    private static IReadOnlyList<DashboardRetentionSummary> BuildDeletionScheduled(
        IEnumerable<JobApplication> applications,
        IReadOnlyDictionary<Guid, string> companies) =>
        applications
            .Where(application =>
                !application.IsSavedForever
                && application.DeletionScheduledAt is not null
                && application.DeletionWarningDismissedAt is null)
            .OrderBy(application => application.DeletionScheduledAt)
            .ThenBy(application => application.RoleTitle)
            .Select(application => new DashboardRetentionSummary(
                application.Id,
                application.RoleTitle,
                CompanyName(application, companies),
                application.AppliedOn,
                application.DeletionScheduledAt!.Value))
            .ToList();

    private static IReadOnlyList<DashboardMonthSummary> BuildMonthSummaries(
        DashboardCalendarWindow window,
        IReadOnlyList<JobApplication> applications) =>
        window.Months
            .Select(month => new DashboardMonthSummary(
                month,
                applications.Count(application =>
                    application.AppliedOn >= month.StartsOn
                    && application.AppliedOn <= month.EndsOn)))
            .ToList();

    private static IReadOnlyList<DashboardGroupSummary> BuildStageSummaries(
        IReadOnlyList<JobApplication> applications) =>
        ApplicationDisplay.ActiveStages
            .Select(stage => new DashboardGroupSummary(
                ApplicationDisplay.Stage(stage),
                applications.Count(application =>
                    ApplicationDisplay.NormalizeStage(application.Stage) == stage)))
            .ToList();

    private static IReadOnlyList<DashboardGroupSummary> BuildOutcomeSummaries(
        IReadOnlyList<JobApplication> applications) =>
        ApplicationDisplay.ActiveOutcomes
            .Select(outcome => new DashboardGroupSummary(
                ApplicationDisplay.Outcome(outcome),
                applications.Count(application =>
                    ApplicationDisplay.NormalizeOutcome(application.Outcome) == outcome)))
            .ToList();

    private static IReadOnlyList<DashboardApplicationSummary> BuildRecentApplications(
        IEnumerable<JobApplication> applications,
        IReadOnlyDictionary<Guid, string> companies,
        IReadOnlyDictionary<Guid, DateTimeOffset> firstResponses) =>
        applications
            .Take(ApplicationRules.RecentApplicationsDisplayCount)
            .Select(application => new DashboardApplicationSummary(
                application.Id,
                application.RoleTitle,
                CompanyName(application, companies),
                application.AppliedOn,
                application.Stage,
                application.Outcome,
                application.IsSavedForever,
                firstResponses.GetValueOrDefault(application.Id)))
            .ToList();

    public async Task<bool> CanConfirmGhostedAsync(
        Guid applicationId,
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var application = await database.JobApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == applicationId && item.OwnerId == ownerId,
                cancellationToken);
        if (application is null || application.Outcome != ApplicationOutcome.Active)
        {
            return false;
        }

        var timeZoneId = await database.Users
            .AsNoTracking()
            .Where(item => item.Id == ownerId)
            .Select(item => item.TimeZoneId)
            .SingleAsync(cancellationToken);
        var window = DashboardCalendar.Create(timeProvider.GetUtcNow(), timeZoneId);
        var appliedStart = AppliedStart(application.AppliedOn, window.TimeZoneId);
        var hasResponse = await database.Interactions
            .AsNoTracking()
            .AnyAsync(item =>
                item.OwnerId == ownerId
                && item.JobApplicationId == applicationId
                && item.IsEmployerResponse
                && item.OccurredAt >= appliedStart,
                cancellationToken);

        return DashboardCalendar.AssessGhosting(
            application.AppliedOn,
            window.Today,
            application.Outcome,
            hasResponse).Kind == GhostingAttentionKind.ConfirmGhosted;
    }

    private static Dictionary<Guid, DateTimeOffset> FirstResponses(
        IReadOnlyList<JobApplication> applications,
        IReadOnlyList<Interaction> interactions,
        string timeZoneId)
    {
        var appliedStarts = applications.ToDictionary(
            item => item.Id,
            item => AppliedStart(item.AppliedOn, timeZoneId));
        return interactions
            .Where(item =>
                item.IsEmployerResponse
                && appliedStarts.TryGetValue(item.JobApplicationId, out var start)
                && item.OccurredAt >= start)
            .GroupBy(item => item.JobApplicationId)
            .ToDictionary(group => group.Key, group => group.Min(item => item.OccurredAt));
    }

    private static IReadOnlyList<DashboardActivitySummary> BuildActivity(
        DashboardCalendarWindow window,
        IReadOnlyList<JobApplication> pipelineApplications,
        IReadOnlyDictionary<Guid, string> companies,
        IReadOnlyList<Interaction> interactions,
        IReadOnlyList<StatusHistory> statusHistory,
        IReadOnlyDictionary<Guid, JobApplication> applicationById,
        IReadOnlySet<Guid> pipelineApplicationIds)
    {
        var activity = new List<DashboardActivitySummary>();
        foreach (var application in pipelineApplications.Where(item => window.Contains(item.AppliedOn)))
        {
            activity.Add(new DashboardActivitySummary(
                application.Id,
                application.RoleTitle,
                CompanyName(application, companies),
                "Application",
                "Application added",
                AppliedStart(application.AppliedOn, window.TimeZoneId)));
        }

        activity.AddRange(statusHistory
            .Where(item =>
                pipelineApplicationIds.Contains(item.JobApplicationId)
                && window.Contains(item.EffectiveAt)
                && applicationById.ContainsKey(item.JobApplicationId))
            .Select(item =>
            {
                var application = applicationById[item.JobApplicationId];
                var summary = item.NewOutcome is not null
                    ? $"Outcome changed to {ApplicationDisplay.Outcome(item.NewOutcome.Value)}"
                    : item.NewStage is not null
                        ? $"Stage changed to {ApplicationDisplay.Stage(item.NewStage.Value)}"
                        : "Status updated";
                return new DashboardActivitySummary(
                    application.Id,
                    application.RoleTitle,
                    CompanyName(application, companies),
                    "Status",
                    summary,
                    item.EffectiveAt);
            }));
        activity.AddRange(interactions
            .Where(item =>
                pipelineApplicationIds.Contains(item.JobApplicationId)
                && window.Contains(item.OccurredAt)
                && applicationById.ContainsKey(item.JobApplicationId))
            .Select(item =>
            {
                var application = applicationById[item.JobApplicationId];
                return new DashboardActivitySummary(
                    application.Id,
                    application.RoleTitle,
                    CompanyName(application, companies),
                    "Interaction",
                    item.IsEmployerResponse
                        ? $"Employer response recorded by {ApplicationDisplay.Interaction(item.Type).ToLowerInvariant()}"
                        : $"{ApplicationDisplay.Interaction(item.Type)} recorded",
                    item.OccurredAt);
            }));

        return activity
            .OrderByDescending(item => item.OccurredAt)
            .ThenBy(item => item.RoleTitle)
            .Take(ApplicationRules.RecentActivityDisplayCount)
            .ToList();
    }

    private static DashboardTaskSummary TaskSummary(
        TaskItem task,
        JobApplication application,
        IReadOnlyDictionary<Guid, string> companies,
        DateTimeOffset now) =>
        new(
            task.Id,
            application.Id,
            application.RoleTitle,
            CompanyName(application, companies),
            task.Title,
            task.DueAt,
            task.DueAt is not null && task.DueAt < now);

    private static DashboardAppointmentSummary AppointmentSummary(
        Appointment appointment,
        JobApplication application,
        IReadOnlyDictionary<Guid, string> companies) =>
        new(
            appointment.Id,
            application.Id,
            application.RoleTitle,
            CompanyName(application, companies),
            appointment.Type,
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.TimeZoneId,
            appointment.LocationOrLink);

    private static DateTimeOffset AppliedStart(DateOnly appliedOn, string timeZoneId) =>
        ApplicationTime.TryConvertToUtc(
            appliedOn.ToDateTime(TimeOnly.MinValue),
            timeZoneId,
            out var utcValue)
            ? utcValue
            : new DateTimeOffset(appliedOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    private static string? CompanyName(
        JobApplication application,
        IReadOnlyDictionary<Guid, string> companies) =>
        application.CompanyId is not null
        && companies.TryGetValue(application.CompanyId.Value, out var companyName)
            ? companyName
            : null;

    private string RequireOwnerId() =>
        currentUser.UserId
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private sealed record DashboardUser(string DisplayName, string TimeZoneId);
}
