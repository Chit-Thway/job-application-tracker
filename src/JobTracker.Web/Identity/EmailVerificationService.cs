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
    string Message);

public sealed record EmailVerificationResult(bool Succeeded, string Message);

public sealed class EmailVerificationService(
    ApplicationDbContext database,
    UserManager<ApplicationUser> userManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IAccountEmailSender emailSender,
    TimeProvider timeProvider)
{
    public const int CodeLength = 6;
    public const int MaxFailedAttempts = 5;
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
}
