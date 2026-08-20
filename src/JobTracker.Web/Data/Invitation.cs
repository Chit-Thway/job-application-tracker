namespace JobTracker.Web.Data;

public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RecipientEmail { get; set; }

    public string? RecipientEmailNormalized { get; set; }

    public string? CreatedByUserId { get; set; }

    public string? UsedByUserId { get; set; }

    public ApplicationUser? UsedByUser { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
