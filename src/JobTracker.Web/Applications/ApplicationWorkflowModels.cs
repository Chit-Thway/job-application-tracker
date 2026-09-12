using JobTracker.Web.Data;

namespace JobTracker.Web.Applications;

public sealed record WorkflowStatusItem(
    Guid Id,
    PipelineStage? PreviousStage,
    PipelineStage? NewStage,
    ApplicationOutcome? PreviousOutcome,
    ApplicationOutcome? NewOutcome,
    DateTimeOffset EffectiveAt,
    string? Note);

public sealed record WorkflowContactItem(
    Guid Id,
    string Name,
    string? JobTitle,
    string? Email,
    string? Phone,
    string? Notes);

public sealed record WorkflowInteractionItem(
    Guid Id,
    Guid? ContactId,
    string? ContactName,
    InteractionType Type,
    DateTimeOffset OccurredAt,
    bool IsEmployerResponse,
    string? Notes);

public sealed record WorkflowTaskItem(
    Guid Id,
    string Title,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    string? Notes);

public sealed record WorkflowAppointmentItem(
    Guid Id,
    AppointmentType Type,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string TimeZoneId,
    string? LocationOrLink,
    string? Notes);

public sealed record ApplicationWorkflowDetails(
    ApplicationDetails Application,
    string TimeZoneId,
    DateTimeOffset CurrentTime,
    int RetentionMonths,
    int DeletionGraceDays,
    bool CanConfirmGhosted,
    DateTimeOffset? FirstEmployerResponseAt,
    IReadOnlyList<WorkflowStatusItem> StatusHistory,
    IReadOnlyList<WorkflowContactItem> Contacts,
    IReadOnlyList<WorkflowInteractionItem> Interactions,
    IReadOnlyList<WorkflowTaskItem> Tasks,
    IReadOnlyList<WorkflowAppointmentItem> Appointments);

public sealed record StatusTransitionInput(
    PipelineStage Stage,
    ApplicationOutcome Outcome,
    string? Note);

public sealed record ContactInput(
    string Name,
    string? JobTitle,
    string? Email,
    string? Phone,
    string? Notes);

public sealed record InteractionInput(
    Guid? ContactId,
    InteractionType Type,
    DateTime OccurredAtLocal,
    bool IsEmployerResponse,
    string? Notes);

public sealed record WorkflowTaskInput(
    string Title,
    DateTime? DueAtLocal,
    string? Notes);

public sealed record WorkflowAppointmentInput(
    AppointmentType Type,
    DateTime StartsAtLocal,
    DateTime EndsAtLocal,
    string? LocationOrLink,
    string? Notes);

public enum WorkflowWriteResult
{
    Success,
    NotFound,
    InvalidRelationship,
    InvalidTransition,
    InvalidLocalTime,
    ContactInUse,
}
