using JobTracker.Web.Identity;

namespace JobTracker.UnitTests;

public sealed class AccountEmailTemplateTests
{
    private const string PublicBaseUrl = "https://tracker.example.test";
    private const string SupportAddress = "redacted@example.invalid";

    [Fact]
    public void VerificationCode_ContainsSixDigitCodeExpiryAndSafeActionUrl()
    {
        const string actionUrl = "https://tracker.example.test/account/verify-email?challengeId=<owner>&next=a";
        var expiresAt = new DateTimeOffset(2026, 8, 28, 12, 10, 0, TimeSpan.Zero);

        var message = AccountEmailTemplates.VerificationCode(
            "048219",
            actionUrl,
            expiresAt,
            PublicBaseUrl,
            SupportAddress);

        Assert.Contains("048219", message.PlainText, StringComparison.Ordinal);
        Assert.Contains(actionUrl, message.PlainText, StringComparison.Ordinal);
        Assert.Contains("Email verification code", message.Html, StringComparison.Ordinal);
        Assert.Contains("048219", message.Html, StringComparison.Ordinal);
        Assert.Contains("challengeId=&lt;owner&gt;&amp;next=a", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("challengeId=<owner>", message.Html, StringComparison.Ordinal);
        Assert.Contains("28 Aug 2026 at 12:10 UTC", message.Html, StringComparison.Ordinal);
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

}
