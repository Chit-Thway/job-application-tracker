using System.ComponentModel.DataAnnotations;

namespace JobTracker.Web.Data;

public sealed class AutofillProfile
{
    public string OwnerId { get; init; } = string.Empty;
    public string ProtectedProfile { get; set; } = string.Empty;
    [ConcurrencyCheck]
    public Guid Version { get; set; } = Guid.NewGuid();
    public string? PairingHash { get; set; }
    public string? PairingChallenge { get; set; }
    public DateTimeOffset? PairingExpiresAt { get; set; }
    public string? AccessHash { get; set; }
    public DateTimeOffset? AccessExpiresAt { get; set; }
    public string? SecurityStamp { get; set; }
}
