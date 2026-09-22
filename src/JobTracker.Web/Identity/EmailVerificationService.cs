using System.Globalization;
using System.Security.Cryptography;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Identity;

public sealed record PendingEmailVerification(
    Guid ChallengeId,
    string Email,
    DateTimeOffset? CodeExpiresAt,
    int ResendAvailableInSeconds);

public sealed record EmailVerificationIssueResult(
    bool Succeeded,
    Guid? ChallengeId,
    int ResendAvailableInSeconds,
    string Message,
    bool MonthlyLimitReached = false);

public sealed record EmailVerificationResult(bool Succeeded, string Message);

public sealed class EmailVerificationService(
    ApplicationDbContext database,
    UserManager<ApplicationUser> userManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IAccountEmailSender emailSender,
    TimeProvider timeProvider)
{
    private const string InMemoryDatabaseProviderName = "Microsoft.EntityFrameworkCore.InMemory";

    public const int CodeLength = 6;
    public const int MaxFailedAttempts = 5;
    public const int MaxMonthlyVerificationEmails = 25;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(30);

    public async Task<PendingEmailVerification?> FindPendingAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default)
    {
        var user = await database.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.EmailVerificationChallengeId == challengeId,
                cancellationToken);
        if (user is null || user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        return new PendingEmailVerification(
            challengeId,
            user.Email,
            user.EmailVerificationCodeExpiresAt,
            SecondsUntilResend(user, timeProvider.GetUtcNow()));
    }

    public async Task<EmailVerificationIssueResult> IssueAsync(
        ApplicationUser user,
        Func<Guid, string> verificationUrlFactory,
        CancellationToken cancellationToken = default)
    {
        if (user.EmailConfirmed)
        {
            return new(false, null, 0, "That email address is already verified.");
        }

        var now = timeProvider.GetUtcNow();
        var resendAvailableIn = SecondsUntilResend(user, now);
        if (resendAvailableIn > 0)
        {
            return new(
                false,
                user.EmailVerificationChallengeId,
                resendAvailableIn,
                $"You can request another code in {resendAvailableIn} seconds.");
        }

        var monthlyLimitReserved = await TryReserveMonthlySendAsync(
            now,
            cancellationToken);
        if (!monthlyLimitReserved)
        {
            return new(
                false,
                user.EmailVerificationChallengeId,
                0,
                "The monthly verification email limit has been reached. New codes will be available next month.",
                MonthlyLimitReached: true);
        }

        var challengeId = user.EmailVerificationChallengeId ?? Guid.NewGuid();
        var code = RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString($"D{CodeLength}", CultureInfo.InvariantCulture);
        var expiresAt = now.Add(CodeLifetime);

        user.EmailVerificationChallengeId = challengeId;
        user.EmailVerificationCodeHash = passwordHasher.HashPassword(user, code);
        user.EmailVerificationCodeExpiresAt = expiresAt;
        user.EmailVerificationCodeLastSentAt = now;
        user.EmailVerificationFailedAttempts = 0;

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return new(false, challengeId, 0, "A verification code could not be prepared. Try again.");
        }

        try
        {
            await emailSender.SendVerificationCodeAsync(
                user,
                code,
                verificationUrlFactory(challengeId),
                expiresAt);
        }
        catch
        {
            user.EmailVerificationCodeHash = null;
            user.EmailVerificationCodeExpiresAt = null;
            user.EmailVerificationCodeLastSentAt = null;
            await userManager.UpdateAsync(user);
            throw;
        }

        return new(
            true,
            challengeId,
            (int)Math.Ceiling(ResendDelay.TotalSeconds),
            "A new verification code was sent.");
    }

    public async Task<EmailVerificationResult> VerifyAsync(
        Guid challengeId,
        string code,
        CancellationToken cancellationToken = default)
    {
        var user = await database.Users.SingleOrDefaultAsync(
            candidate => candidate.EmailVerificationChallengeId == challengeId,
            cancellationToken);
        if (user is null || user.EmailConfirmed)
        {
            return new(false, "That verification request is no longer available.");
        }

        var now = timeProvider.GetUtcNow();
        if (string.IsNullOrWhiteSpace(user.EmailVerificationCodeHash)
            || user.EmailVerificationCodeExpiresAt is null
            || user.EmailVerificationCodeExpiresAt <= now)
        {
            ClearCode(user);
            await userManager.UpdateAsync(user);
            return new(false, "That code has expired. Request a new code and try again.");
        }

        var verification = passwordHasher.VerifyHashedPassword(
            user,
            user.EmailVerificationCodeHash,
            code.Trim());
        if (verification == PasswordVerificationResult.Failed)
        {
            user.EmailVerificationFailedAttempts = Math.Min(
                MaxFailedAttempts,
                user.EmailVerificationFailedAttempts + 1);
            var attemptsRemaining = MaxFailedAttempts - user.EmailVerificationFailedAttempts;
            if (attemptsRemaining == 0)
            {
                ClearCode(user);
            }

            await userManager.UpdateAsync(user);
            return attemptsRemaining == 0
                ? new(false, "Too many incorrect attempts. Request a new code.")
                : new(false, $"That code is incorrect. {attemptsRemaining} attempts remain.");
        }

        user.EmailConfirmed = true;
        user.EmailVerificationChallengeId = null;
        user.EmailVerificationCodeLastSentAt = null;
        ClearCode(user);
        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? new(true, "Your email address has been verified.")
            : new(false, "Your email address could not be verified. Try again.");
    }

    private static int SecondsUntilResend(ApplicationUser user, DateTimeOffset now)
    {
        if (user.EmailVerificationCodeLastSentAt is null)
        {
            return 0;
        }

        var remaining = user.EmailVerificationCodeLastSentAt.Value.Add(ResendDelay) - now;
        return remaining <= TimeSpan.Zero
            ? 0
            : (int)Math.Ceiling(remaining.TotalSeconds);
    }

    private static void ClearCode(ApplicationUser user)
    {
        user.EmailVerificationCodeHash = null;
        user.EmailVerificationCodeExpiresAt = null;
        user.EmailVerificationFailedAttempts = 0;
    }

    private async Task<bool> TryReserveMonthlySendAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTimeOffset(
            now.Year,
            now.Month,
            day: 1,
            hour: 0,
            minute: 0,
            second: 0,
            TimeSpan.Zero);

        if (database.Database.ProviderName == InMemoryDatabaseProviderName)
        {
            var inMemoryUsage = await database.EmailVerificationMonthlyUsages
                .SingleOrDefaultAsync(usage => usage.MonthStart == monthStart, cancellationToken);
            if (inMemoryUsage is null)
            {
                database.EmailVerificationMonthlyUsages.Add(new EmailVerificationMonthlyUsage
                {
                    MonthStart = monthStart,
                    SentCount = 1,
                });
                await database.SaveChangesAsync(cancellationToken);
                return true;
            }

            if (inMemoryUsage.SentCount >= MaxMonthlyVerificationEmails)
            {
                return false;
            }

            inMemoryUsage.SentCount++;
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updatedRows = await database.EmailVerificationMonthlyUsages
            .Where(usage => usage.MonthStart == monthStart
                && usage.SentCount < MaxMonthlyVerificationEmails)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    usage => usage.SentCount,
                    usage => usage.SentCount + 1),
                cancellationToken);
        if (updatedRows == 1)
        {
            return true;
        }

        var usageExists = await database.EmailVerificationMonthlyUsages
            .AnyAsync(usage => usage.MonthStart == monthStart, cancellationToken);
        if (usageExists)
        {
            return false;
        }

        database.EmailVerificationMonthlyUsages.Add(new EmailVerificationMonthlyUsage
        {
            MonthStart = monthStart,
            SentCount = 1,
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();
            return await TryReserveMonthlySendAsync(now, cancellationToken);
        }
    }
}
