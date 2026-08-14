using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Applications;

public sealed record DashboardMonthSummary(
    DashboardMonth Month,
    int ApplicationCount);

public sealed record DashboardGroupSummary(
    string Label,
    int Count);

public sealed record DashboardApplicationSummary(
    Guid Id,
    string RoleTitle,
    string? CompanyName,
    DateOnly AppliedOn,
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    bool IsSavedForever,
    DateTimeOffset? FirstEmployerResponseAt);

public sealed record DashboardTaskSummary(
    Guid Id,
    Guid ApplicationId,
    string RoleTitle,
    string? CompanyName,
    string Title,
    DateTimeOffset? DueAt,
    bool IsOverdue);

public sealed record DashboardAppointmentSummary(
    Guid Id,
    Guid ApplicationId,
    string RoleTitle,
    string? CompanyName,
    AppointmentType Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string TimeZoneId,
    string? LocationOrLink);

public sealed record DashboardGhostingSummary(
    Guid ApplicationId,
    string RoleTitle,
    string? CompanyName,
    DateOnly AppliedOn,
    GhostingAttentionKind Kind,
    int DaysSinceApplied);

public sealed record DashboardRetentionSummary(
    Guid ApplicationId,
    string RoleTitle,
    string? CompanyName,
    DateOnly AppliedOn,
    DateTimeOffset DeletionScheduledAt);

public sealed record DashboardActivitySummary(
    Guid ApplicationId,
    string RoleTitle,
    string? CompanyName,
    string Kind,
    string Summary,
    DateTimeOffset OccurredAt);

public sealed record DashboardSnapshot(
    string DisplayName,
    DashboardCalendarWindow Window,
    int ApplicationCount,
    int ResponseCount,
    int InterviewCount,
    IReadOnlyList<DashboardMonthSummary> Months,
    IReadOnlyList<DashboardGroupSummary> Stages,
    IReadOnlyList<DashboardGroupSummary> Outcomes,
    IReadOnlyList<DashboardApplicationSummary> RecentApplications,
    IReadOnlyList<DashboardActivitySummary> RecentActivity,
    IReadOnlyList<DashboardTaskSummary> OpenTasks,
    IReadOnlyList<DashboardAppointmentSummary> UpcomingAppointments,
    IReadOnlyList<DashboardGhostingSummary> GhostingAttention,
    IReadOnlyList<DashboardRetentionSummary> DeletionScheduled);

public sealed class DashboardService(
    ApplicationDbContext database,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider)
{
    public async Task<DashboardSnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var ownerId = RequireOwnerId();
        var user = await database.Users
            .AsNoTracking()
            .Where(item => item.Id == ownerId)
            .Select(item => new { item.DisplayName, item.TimeZoneId })
            .SingleAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var window = DashboardCalendar.Create(now, user.TimeZoneId);

        var applications = await database.JobApplications
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
        var companies = await database.Companies
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var interactions = await database.Interactions
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
        var tasks = await database.Tasks
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId && item.CompletedAt == null)
            .ToListAsync(cancellationToken);
        var appointments = await database.Appointments
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
        var statusHistory = await database.StatusHistory
            .AsNoTracking()
            .Where(item => item.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

        var applicationById = applications.ToDictionary(item => item.Id);
        var firstResponses = FirstResponses(applications, interactions, window.TimeZoneId);
        var windowApplications = applications
            .Where(item => window.Contains(item.AppliedOn))
            .OrderByDescending(item => item.AppliedOn)
            .ThenBy(item => item.RoleTitle)
            .ToList();
        var windowApplicationIds = windowApplications.Select(item => item.Id).ToHashSet();

        var openTasks = tasks
            .Where(item => applicationById.ContainsKey(item.JobApplicationId))
            .Select(item => TaskSummary(
                item,
                applicationById[item.JobApplicationId],
                companies,
                now))
            .OrderByDescending(item => item.IsOverdue)
            .ThenBy(item => item.DueAt is null)
            .ThenBy(item => item.DueAt)
            .ThenBy(item => item.Title)
            .ToList();
        var upcomingAppointments = appointments
            .Where(item => item.EndsAt >= now && applicationById.ContainsKey(item.JobApplicationId))
            .Select(item => AppointmentSummary(
                item,
                applicationById[item.JobApplicationId],
                companies))
            .OrderBy(item => item.StartsAt)
            .ToList();
        var ghostingAttention = applications
            .Select(item => new
            {
                Application = item,
                Assessment = DashboardCalendar.AssessGhosting(
                    item.AppliedOn,
                    window.Today,
                    item.Outcome,
                    firstResponses.ContainsKey(item.Id)),
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
        var deletionScheduled = applications
            .Where(item =>
                !item.IsSavedForever
                && item.DeletionScheduledAt is not null)
            .OrderBy(item => item.DeletionScheduledAt)
            .ThenBy(item => item.RoleTitle)
            .Select(item => new DashboardRetentionSummary(
                item.Id,
                item.RoleTitle,
                CompanyName(item, companies),
                item.AppliedOn,
                item.DeletionScheduledAt!.Value))
            .ToList();

        var recentActivity = BuildActivity(
            window,
            windowApplications,
            companies,
            interactions,
            statusHistory,
            applicationById,
            windowApplicationIds);

        return new DashboardSnapshot(
            user.DisplayName,
            window,
            windowApplications.Count,
            windowApplications.Count(item => firstResponses.ContainsKey(item.Id)),
            appointments.Count(item =>
                item.Type == AppointmentType.Interview
                && windowApplicationIds.Contains(item.JobApplicationId)
                && window.Contains(item.StartsAt)),
            window.Months.Select(month => new DashboardMonthSummary(
                month,
                windowApplications.Count(item =>
                    item.AppliedOn >= month.StartsOn && item.AppliedOn <= month.EndsOn)))
                .ToList(),
            Enum.GetValues<PipelineStage>()
                .Select(stage => new DashboardGroupSummary(
                    ApplicationDisplay.Stage(stage),
                    windowApplications.Count(item => item.Stage == stage)))
                .ToList(),
            Enum.GetValues<ApplicationOutcome>()
                .Select(outcome => new DashboardGroupSummary(
                    ApplicationDisplay.Outcome(outcome),
                    windowApplications.Count(item => item.Outcome == outcome)))
                .ToList(),
            windowApplications.Take(8).Select(item => new DashboardApplicationSummary(
                item.Id,
                item.RoleTitle,
                CompanyName(item, companies),
                item.AppliedOn,
                item.Stage,
                item.Outcome,
                item.IsSavedForever,
                firstResponses.GetValueOrDefault(item.Id)))
                .ToList(),
            recentActivity,
            openTasks,
            upcomingAppointments,
            ghostingAttention,
            deletionScheduled);
    }

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
        IReadOnlyList<JobApplication> windowApplications,
        IReadOnlyDictionary<Guid, string> companies,
        IReadOnlyList<Interaction> interactions,
        IReadOnlyList<StatusHistory> statusHistory,
        IReadOnlyDictionary<Guid, JobApplication> applicationById,
        IReadOnlySet<Guid> windowApplicationIds)
    {
        var activity = new List<DashboardActivitySummary>();
        foreach (var application in windowApplications)
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
                windowApplicationIds.Contains(item.JobApplicationId)
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
                windowApplicationIds.Contains(item.JobApplicationId)
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
            .Take(10)
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
}
