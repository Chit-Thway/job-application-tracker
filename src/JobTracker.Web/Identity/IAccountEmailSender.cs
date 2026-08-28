using JobTracker.Web.Data;

namespace JobTracker.Web.Identity;

public interface IAccountEmailSender
{
    Task SendVerificationCodeAsync(
        ApplicationUser user,
        string verificationCode,
        string verificationUrl,
        DateTimeOffset expiresAt);

    Task SendPasswordResetAsync(ApplicationUser user, string resetUrl);
}
