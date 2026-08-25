namespace JobTracker.Web.Data;

public sealed class ExtensionCaptureHandoff
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TokenHash { get; set; } = string.Empty;

    public string ProtectedPayload { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
