using JobTracker.Web.Data;

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
