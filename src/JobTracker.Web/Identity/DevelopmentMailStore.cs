using System.Collections.Concurrent;

namespace JobTracker.Web.Identity;

public sealed record DevelopmentMailMessage(
    string Recipient,
    string Subject,
    string ActionUrl,
    string? OneTimeCode,
    DateTimeOffset SentAt);

public sealed class DevelopmentMailStore
{
    private readonly ConcurrentQueue<DevelopmentMailMessage> messages = new();

    public IReadOnlyList<DevelopmentMailMessage> Messages =>
        messages.Reverse().ToArray();

    public void Add(DevelopmentMailMessage message) => messages.Enqueue(message);
}
