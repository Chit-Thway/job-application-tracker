using Azure;
using Azure.Communication.Email;
using JobTracker.Web.Data;
using Microsoft.Extensions.Options;

namespace JobTracker.Web.Identity;

public sealed class AzureCommunicationAccountEmailSender(
    EmailClient client,
    IOptions<AccountEmailOptions> options,
    ILogger<AzureCommunicationAccountEmailSender> logger) : IAccountEmailSender
{
    private readonly AccountEmailOptions settings = options.Value;

    public Task SendVerificationAsync(ApplicationUser user, string verificationUrl) =>
        SendAsync(user, AccountEmailTemplates.Verification(verificationUrl));

    public Task SendPasswordResetAsync(ApplicationUser user, string resetUrl) =>
        SendAsync(user, AccountEmailTemplates.PasswordReset(resetUrl));

    private async Task SendAsync(ApplicationUser user, AccountEmailContent content)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("The account does not have an email address.");
        }

        var operation = await client.SendAsync(
            WaitUntil.Started,
            settings.SenderAddress,
            user.Email,
            content.Subject,
            content.Html,
            content.PlainText);

        logger.LogInformation(
            "Account email accepted by Azure Communication Services. OperationId={OperationId}",
            operation.Id);
    }
}
