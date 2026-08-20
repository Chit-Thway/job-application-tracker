using JobTracker.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using System.Data;

namespace JobTracker.Web.Identity;

public sealed record InvitationRegistrationResult(
    bool Succeeded,
    ApplicationUser? User,
    IReadOnlyCollection<IdentityError> IdentityErrors)
{
    public static InvitationRegistrationResult InvalidInvitation() =>
        new(false, null, []);

    public static InvitationRegistrationResult IdentityFailure(
        IEnumerable<IdentityError> errors) =>
        new(false, null, errors.ToArray());

    public static InvitationRegistrationResult Success(ApplicationUser user) =>
        new(true, user, []);
}

public sealed class InvitationRegistrationService(
    ApplicationDbContext database,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider)
{
    public async Task<InvitationRegistrationResult> RegisterAsync(
        string invitationCode,
        string displayName,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;

        try
        {
            if (database.Database.IsRelational())
            {
                transaction = await database.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var now = timeProvider.GetUtcNow();
            var codeHash = InvitationCode.Hash(invitationCode);
            var invitation = await database.Invitations.SingleOrDefaultAsync(
                item => item.CodeHash == codeHash
                    && item.UsedAt == null
                    && item.RevokedAt == null
                    && item.ExpiresAt > now,
                cancellationToken);

            if (invitation is null)
            {
                return InvitationRegistrationResult.InvalidInvitation();
            }

            var normalizedEmail = userManager.NormalizeEmail(email.Trim());
            if (invitation.RecipientEmailNormalized is not null
                && !string.Equals(
                    invitation.RecipientEmailNormalized,
                    normalizedEmail,
                    StringComparison.Ordinal))
            {
                return InvitationRegistrationResult.InvalidInvitation();
            }

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = email.Trim(),
                Email = email.Trim(),
                DisplayName = displayName.Trim(),
                TimeZoneId = "Australia/Perth",
                CreatedAt = now,
            };

            var identityResult = await userManager.CreateAsync(user, password);
            if (!identityResult.Succeeded)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return InvitationRegistrationResult.IdentityFailure(identityResult.Errors);
            }

            invitation.UsedAt = now;
            invitation.UsedByUserId = user.Id;
            invitation.ConcurrencyStamp = Guid.NewGuid();
            await database.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return InvitationRegistrationResult.Success(user);
        }
        catch (DbUpdateConcurrencyException)
        {
            await TryRollbackAsync(transaction, cancellationToken);
            return InvitationRegistrationResult.InvalidInvitation();
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException postgres
                && postgres.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            await TryRollbackAsync(transaction, cancellationToken);
            return InvitationRegistrationResult.InvalidInvitation();
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            await TryRollbackAsync(transaction, cancellationToken);
            return InvitationRegistrationResult.InvalidInvitation();
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static async Task TryRollbackAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // A database can already have rolled back a serialization failure.
        }
    }
}
