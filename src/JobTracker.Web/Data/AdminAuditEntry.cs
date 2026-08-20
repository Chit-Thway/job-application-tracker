namespace JobTracker.Web.Data;

public sealed class AdminAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ActorUserId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? TargetUserId { get; set; }

    public Guid? InvitationId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Details { get; set; } = string.Empty;
}
