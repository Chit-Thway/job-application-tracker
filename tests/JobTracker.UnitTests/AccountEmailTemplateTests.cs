using JobTracker.Web.Identity;

namespace JobTracker.UnitTests;

public sealed class AccountEmailTemplateTests
{
    private const string PublicBaseUrl = "https://tracker.example.test";
    private const string SupportAddress = "redacted@example.invalid";

    [Fact]
    public void Verification_ContainsActionInPlainTextAndSafeHtml()
    {
        const string actionUrl = "https://tracker.example.test/account/confirm-email?code=a&userId=<owner>";

        var message = AccountEmailTemplates.Verification(
            actionUrl,
            PublicBaseUrl,
            SupportAddress);

        Assert.Contains(actionUrl, message.PlainText, StringComparison.Ordinal);
        Assert.Contains("Verify email", message.Html, StringComparison.Ordinal);
        Assert.Contains("&amp;userId=&lt;owner&gt;", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("userId=<owner>", message.Html, StringComparison.Ordinal);
        Assert.Contains(
            "https://tracker.example.test/account/login",
            message.Html,
            StringComparison.Ordinal);
        Assert.Contains(
            "mailto:redacted@example.invalid",
            message.Html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordReset_DoesNotClaimThatAnUnrequestedChangeOccurred()
    {
        var message = AccountEmailTemplates.PasswordReset(
            "https://tracker.example.test/account/reset-password?code=safe",
            PublicBaseUrl,
            SupportAddress);

        Assert.Contains("choose a new password", message.PlainText, StringComparison.Ordinal);
        Assert.Contains("did not request", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Invitation_PresentsOneTimeCodeAndRegistrationLinkSafely()
    {
        const string invitationCode = "jit_safe-code-482<&";
        var expiresAt = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

        var message = AccountEmailTemplates.Invitation(
            invitationCode,
            expiresAt,
            PublicBaseUrl,
            SupportAddress);

        Assert.Contains(invitationCode, message.PlainText, StringComparison.Ordinal);
        Assert.Contains(
            "https://tracker.example.test/account/register",
            message.Html,
            StringComparison.Ordinal);
        Assert.Contains("One-time invitation code", message.Html, StringComparison.Ordinal);
        Assert.Contains("jit_safe-code-482&lt;&amp;", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain(invitationCode, message.Html, StringComparison.Ordinal);
        Assert.Contains("27 Aug 2026 at 12:00 UTC", message.Html, StringComparison.Ordinal);
    }
}
