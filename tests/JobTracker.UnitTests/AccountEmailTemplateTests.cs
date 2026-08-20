using JobTracker.Web.Identity;

namespace JobTracker.UnitTests;

public sealed class AccountEmailTemplateTests
{
    [Fact]
    public void Verification_ContainsActionInPlainTextAndSafeHtml()
    {
        const string actionUrl = "https://tracker.example.test/account/confirm-email?code=a&userId=<owner>";

        var message = AccountEmailTemplates.Verification(actionUrl);

        Assert.Contains(actionUrl, message.PlainText, StringComparison.Ordinal);
        Assert.Contains("Verify account", message.Html, StringComparison.Ordinal);
        Assert.Contains("&amp;userId=&lt;owner&gt;", message.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("userId=<owner>", message.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordReset_DoesNotClaimThatAnUnrequestedChangeOccurred()
    {
        var message = AccountEmailTemplates.PasswordReset(
            "https://tracker.example.test/account/reset-password?code=safe");

        Assert.Contains("choose a new password", message.PlainText, StringComparison.Ordinal);
        Assert.Contains("did not request", message.Html, StringComparison.Ordinal);
    }
}
