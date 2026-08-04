using JobTracker.Web.Data;

namespace JobTracker.Web.Identity;

public interface IAccountEmailSender
{
    Task SendVerificationAsync(ApplicationUser user, string verificationUrl);

    Task SendPasswordResetAsync(ApplicationUser user, string resetUrl);
}
