using JobTracker.Web.Data;
using Microsoft.AspNetCore.Identity;

namespace JobTracker.Web.Identity;

public sealed class DevelopmentAccountBootstrapper(
    IConfiguration configuration,
    UserManager<ApplicationUser> userManager,
    EmailVerificationService emailVerification)
{
    public async Task InitializeAsync()
    {
        var email = configuration["BootstrapAccount:Email"];
        var displayName = configuration["BootstrapAccount:DisplayName"];
        var password = configuration["BootstrapAccount:Password"];

        if (string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                DisplayName = displayName,
                TimeZoneId = "Australia/Perth",
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                var errorCodes = string.Join(", ", result.Errors.Select(error => error.Code));
                throw new InvalidOperationException(
                    $"Development account creation failed: {errorCodes}");
            }
        }

        if (!user.EmailConfirmed)
        {
            await emailVerification.IssueAsync(
                user,
                challengeId => $"/account/verify-email?challengeId={challengeId:D}");
        }
    }
}
