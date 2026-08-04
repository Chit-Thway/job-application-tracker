using JobTracker.Web.Data;

namespace JobTracker.Web.Identity;

public sealed class DevelopmentAccountEmailSender(
    DevelopmentMailStore store,
    IHostEnvironment environment) : IAccountEmailSender
{
    public Task SendVerificationAsync(ApplicationUser user, string verificationUrl)
    {
        EnsureDevelopment();
        store.Add(new DevelopmentMailMessage(
            user.Email ?? string.Empty,
            "Verify your Job Application Tracker account",
            verificationUrl,
            DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(ApplicationUser user, string resetUrl)
    {
        EnsureDevelopment();
        store.Add(new DevelopmentMailMessage(
            user.Email ?? string.Empty,
            "Reset your Job Application Tracker password",
            resetUrl,
            DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

    private void EnsureDevelopment()
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "The development email sink cannot be used outside development or tests.");
        }
    }
}
