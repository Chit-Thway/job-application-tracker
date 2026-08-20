using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Identity;

public sealed record CreatedInvitation(
    Guid Id,
    string Code,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record InvitationSummary(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? RevokedAt,
    string? RecipientEmail,
    string? CreatedByUserId,
    string Status);

public sealed class InvitationService(
    ApplicationDbContext database,
    TimeProvider timeProvider)
{
    public async Task<CreatedInvitation> CreateAsync(
        int validForDays,
        CancellationToken cancellationToken = default)
        => await CreateCoreAsync(validForDays, null, null, cancellationToken);

    public async Task<CreatedInvitation> CreateForRecipientAsync(
        int validForDays,
        string recipientEmail,
        string createdByUserId,
        CancellationToken cancellationToken = default)
        => await CreateCoreAsync(
            validForDays,
            recipientEmail.Trim(),
            createdByUserId,
            cancellationToken);

    private async Task<CreatedInvitation> CreateCoreAsync(
        int validForDays,
        string? recipientEmail,
        string? createdByUserId,
        CancellationToken cancellationToken)
    {
        if (validForDays is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validForDays),
                "Invitation lifetime must be between 1 and 30 days.");
        }

        var now = timeProvider.GetUtcNow();
        var code = InvitationCode.Create();
        var invitation = new Invitation
        {
            CodeHash = InvitationCode.Hash(code),
            CreatedAt = now,
            ExpiresAt = now.AddDays(validForDays),
            RecipientEmail = recipientEmail,
            RecipientEmailNormalized = recipientEmail?.ToUpperInvariant(),
            CreatedByUserId = createdByUserId,
        };

        database.Invitations.Add(invitation);
        await database.SaveChangesAsync(cancellationToken);

        return new CreatedInvitation(
            invitation.Id,
            code,
            invitation.CreatedAt,
            invitation.ExpiresAt);
    }

    public async Task<IReadOnlyList<InvitationSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var invitations = await database.Invitations
            .AsNoTracking()
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ToListAsync(cancellationToken);

        return invitations.Select(invitation => new InvitationSummary(
            invitation.Id,
            invitation.CreatedAt,
            invitation.ExpiresAt,
            invitation.UsedAt,
            invitation.RevokedAt,
            invitation.RecipientEmail,
            invitation.CreatedByUserId,
            StatusFor(invitation, now))).ToList();
    }

    public async Task<bool> RevokeAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var invitation = await database.Invitations
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (invitation is null || invitation.UsedAt is not null || invitation.RevokedAt is not null)
        {
            return false;
        }

        invitation.RevokedAt = timeProvider.GetUtcNow();
        invitation.ConcurrencyStamp = Guid.NewGuid();
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string StatusFor(Invitation invitation, DateTimeOffset now)
    {
        if (invitation.UsedAt is not null)
        {
            return "Used";
        }

        if (invitation.RevokedAt is not null)
        {
            return "Revoked";
        }

        return invitation.ExpiresAt <= now ? "Expired" : "Available";
    }
}
