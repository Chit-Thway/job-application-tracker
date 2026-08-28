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

    public Task SendVerificationCodeAsync(
        ApplicationUser user,
        string verificationCode,
        string verificationUrl,
        DateTimeOffset expiresAt) =>
        SendAsync(
            RequiredEmail(user),
            AccountEmailTemplates.VerificationCode(
                verificationCode,
                verificationUrl,
                expiresAt,
                settings.PublicBaseUrl,
                settings.SupportAddress));

    public Task SendPasswordResetAsync(ApplicationUser user, string resetUrl) =>
        SendAsync(
            RequiredEmail(user),
            AccountEmailTemplates.PasswordReset(
                resetUrl,
                settings.PublicBaseUrl,
                settings.SupportAddress));

    private async Task SendAsync(string recipientAddress, AccountEmailContent content)
    {
        var operation = await client.SendAsync(
            WaitUntil.Started,
            settings.SenderAddress,
            recipientAddress,
            content.Subject,
            content.Html,
            content.PlainText);

        logger.LogInformation(
            "Account email accepted by Azure Communication Services. OperationId={OperationId}",
            operation.Id);
    }

    private static string RequiredEmail(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.Email)
            ? throw new InvalidOperationException("The account does not have an email address.")
            : user.Email;
}
