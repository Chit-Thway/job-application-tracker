using System.Net;

namespace JobTracker.Web.Identity;

public sealed record AccountEmailContent(
    string Subject,
    string PlainText,
    string Html);

public static class AccountEmailTemplates
{
    public static AccountEmailContent Verification(string actionUrl) => Create(
        "Verify your Job Application Tracker account",
        "Verify account",
        "Confirm your email address to finish setting up your Job Application Tracker account.",
        actionUrl,
        "If you did not create this account, you can ignore this message.");

    public static AccountEmailContent PasswordReset(string actionUrl) => Create(
        "Reset your Job Application Tracker password",
        "Reset password",
        "Use this link to choose a new password for your Job Application Tracker account.",
        actionUrl,
        "If you did not request a password reset, you can ignore this message.");

    private static AccountEmailContent Create(
        string subject,
        string actionLabel,
        string introduction,
        string actionUrl,
        string closing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionUrl);
        var encodedUrl = WebUtility.HtmlEncode(actionUrl);
        var plainText = $"{introduction}\n\n{actionLabel}: {actionUrl}\n\n{closing}";
        var html = $$"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;background:#f4f6fb;color:#111827;font-family:Arial,sans-serif;">
              <div style="max-width:600px;margin:0 auto;padding:32px 20px;">
                <div style="background:#ffffff;border:1px solid #dbe2f0;border-radius:16px;padding:32px;">
                  <p style="margin:0 0 8px;color:#3f63dd;font-size:13px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">Job Application Tracker</p>
                  <h1 style="margin:0 0 16px;font-size:26px;line-height:1.25;">{{subject}}</h1>
                  <p style="margin:0 0 24px;line-height:1.6;">{{introduction}}</p>
                  <p style="margin:0 0 24px;">
                    <a href="{{encodedUrl}}" style="display:inline-block;background:#3f63dd;color:#ffffff;text-decoration:none;font-weight:700;padding:13px 20px;border-radius:10px;">{{actionLabel}}</a>
                  </p>
                  <p style="margin:0;color:#5f6b85;font-size:14px;line-height:1.6;">{{closing}}</p>
                </div>
              </div>
            </body>
            </html>
            """;

        return new AccountEmailContent(subject, plainText, html);
    }
}
