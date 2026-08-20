using JobTracker.Web.Data;

namespace JobTracker.Web.Identity;

public sealed class DevelopmentAccountEmailSender(
    DevelopmentMailStore store,
    IHostEnvironment environment,
    IConfiguration configuration) : IAccountEmailSender
{
    public Task SendInvitationAsync(
        string recipientAddress,
        string invitationCode,
        DateTimeOffset expiresAt)
    {
        EnsureDevelopment();
        var registrationUrl = BuildAccountUrl("account/register");
        store.Add(new DevelopmentMailMessage(
            recipientAddress,
            "Your Job Application Tracker invitation",
            registrationUrl,
            DateTimeOffset.UtcNow));
        return Task.CompletedTask;
    }

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

    private string BuildAccountUrl(string path)
    {
        var configuredBaseUrl = configuration["Email:PublicBaseUrl"];
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? "http://localhost:5261/"
            : configuredBaseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(baseUrl, UriKind.Absolute), path).AbsoluteUri;
    }
}
