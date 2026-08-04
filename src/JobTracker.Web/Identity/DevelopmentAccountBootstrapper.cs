using JobTracker.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace JobTracker.Web.Identity;

public sealed class DevelopmentAccountBootstrapper(
    IConfiguration configuration,
    UserManager<ApplicationUser> userManager,
    IAccountEmailSender emailSender)
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
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var verificationUrl = $"/account/confirm-email?userId={Uri.EscapeDataString(user.Id)}&code={Uri.EscapeDataString(encodedToken)}";
            await emailSender.SendVerificationAsync(user, verificationUrl);
        }
    }
}
