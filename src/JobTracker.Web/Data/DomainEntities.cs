namespace JobTracker.Web.Data;

public abstract class OwnedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string OwnerId { get; set; }
}

public sealed class Company : OwnedEntity
{
    public required string Name { get; set; }
    public string? Website { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
}

public sealed class JobApplication : OwnedEntity
{
    public Guid? CompanyId { get; set; }
    public required string RoleTitle { get; set; }
    public DateOnly AppliedOn { get; set; }
    public PipelineStage Stage { get; set; } = PipelineStage.Applied;
    public ApplicationOutcome Outcome { get; set; } = ApplicationOutcome.Active;
    public string? SourceUrl { get; set; }
    public string? SourceText { get; set; }
    public string? DescriptionText { get; set; }
    public string? ExtractionMetadataJson { get; set; }
    public string? Notes { get; set; }
    public bool IsSavedForever { get; set; }
    public DateTimeOffset? DeletionScheduledAt { get; set; }
}

public sealed class ExtractionDraft : OwnedEntity
{
    public ExtractionSourceType SourceType { get; set; }
    public required string SourceText { get; set; }
    public required string NormalizedText { get; set; }
    public required string ParsedFieldsJson { get; set; }
    public required string EvidenceJson { get; set; }
    public required string WarningsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class StatusHistory : OwnedEntity
{
    public Guid JobApplicationId { get; set; }
    public PipelineStage? PreviousStage { get; set; }
    public PipelineStage? NewStage { get; set; }
    public ApplicationOutcome? PreviousOutcome { get; set; }
    public ApplicationOutcome? NewOutcome { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public string? Note { get; set; }
}

public sealed class Contact : OwnedEntity
{
    public Guid? CompanyId { get; set; }
    public Guid? JobApplicationId { get; set; }
    public required string Name { get; set; }
    public string? JobTitle { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
}

public sealed class Interaction : OwnedEntity
{
    public Guid JobApplicationId { get; set; }
    public Guid? ContactId { get; set; }
    public InteractionType Type { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public bool IsEmployerResponse { get; set; }
    public string? Notes { get; set; }
}

public sealed class TaskItem : OwnedEntity
{
    public Guid JobApplicationId { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }
}

public sealed class Appointment : OwnedEntity
{
    public Guid JobApplicationId { get; set; }
    public AppointmentType Type { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public required string TimeZoneId { get; set; }
    public string? LocationOrLink { get; set; }
    public string? Notes { get; set; }
}

public sealed class RetentionRun
{
    public long Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public bool Succeeded { get; set; }
    public int ScheduledCount { get; set; }
    public int DeletedCount { get; set; }
    public string? ErrorCode { get; set; }
}
