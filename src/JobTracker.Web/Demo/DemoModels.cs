using JobTracker.Web.Data;

namespace JobTracker.Web.Demo;

public sealed record DemoDashboardViewModel(
    DateOnly Today,
    DateOnly StartsOn,
    DateOnly EndsOn,
    IReadOnlyList<DemoApplication> Applications);

public sealed record DemoApplicationIndexViewModel(
    IReadOnlyList<DemoApplication> Applications,
    string? Query,
    PipelineStage? Stage);

public sealed record DemoActionCentreViewModel(
    DateOnly Today,
    IReadOnlyList<DemoApplication> Applications);

public sealed record DemoApplication(
    string Slug,
    string CompanyName,
    string RoleTitle,
    string Location,
    string WorkArrangement,
    DateOnly AppliedOn,
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    DemoRetentionState RetentionState,
    DateTimeOffset? DeletionScheduledAt,
    string EmploymentType,
    string Salary,
    string SourceSite,
    string JobReference,
    string Summary,
    IReadOnlyList<DemoDescriptionSection> Description,
    IReadOnlyList<DemoContact> Contacts,
    IReadOnlyList<DemoActivity> Activity,
    IReadOnlyList<DemoTask> Tasks,
    IReadOnlyList<DemoAppointment> Appointments);

public sealed record DemoDescriptionSection(
    string Heading,
    IReadOnlyList<string> Paragraphs,
    IReadOnlyList<string> Bullets);

public sealed record DemoContact(
    string Name,
    string Title,
    string Email,
    string Notes);

public sealed record DemoActivity(
    string Kind,
    DateTimeOffset OccurredAt,
    string Summary,
    bool IsEmployerResponse = false);

public sealed record DemoTask(
    string Title,
    DateTimeOffset? DueAt,
    bool IsCompleted,
    string Notes);

public sealed record DemoAppointment(
    string Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    string Notes);

public enum DemoRetentionState
{
    Recent,
    Saved,
    DeletionScheduled,
}
