using System.Globalization;
using System.Net;

namespace JobTracker.Web.Identity;

public sealed record AccountEmailContent(
    string Subject,
    string PlainText,
    string Html);

public static class AccountEmailTemplates
{
    public static AccountEmailContent VerificationCode(
        string verificationCode,
        string verificationUrl,
        DateTimeOffset expiresAt,
        string publicBaseUrl,
        string supportAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verificationCode);
        var expiresText = expiresAt
            .ToUniversalTime()
            .ToString("d MMM yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);

        return Create(
            "Your Job Application Tracker verification code",
            "Verify your email",
            "Enter the six-digit code below to finish setting up your Job Application Tracker account.",
            "Enter verification code",
            verificationUrl,
            publicBaseUrl,
            supportAddress,
            "This code expires " + expiresText + ". If you did not create this account, you can safely ignore this message.",
            verificationCode,
            "Email verification code");
    }

    public static AccountEmailContent PasswordReset(
        string actionUrl,
        string publicBaseUrl,
        string supportAddress) => Create(
        "Reset your Job Application Tracker password",
        "Choose a new password",
        "Use the secure link below to choose a new password for your account.",
        "Reset password",
        actionUrl,
        publicBaseUrl,
        supportAddress,
        "If you did not request a password reset, you can safely ignore this message.");

    private static AccountEmailContent Create(
        string subject,
        string heading,
        string introduction,
        string actionLabel,
        string actionUrl,
        string publicBaseUrl,
        string supportAddress,
        string closing,
        string? prominentCode = null,
        string? prominentCodeLabel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(supportAddress);

        var loginUrl = BuildAccountUrl(publicBaseUrl, "account/login");
        var encodedActionUrl = WebUtility.HtmlEncode(actionUrl);
        var encodedLoginUrl = WebUtility.HtmlEncode(loginUrl);
        var encodedSupportAddress = WebUtility.HtmlEncode(supportAddress);
        var encodedSubject = WebUtility.HtmlEncode(subject);
        var encodedHeading = WebUtility.HtmlEncode(heading);
        var encodedIntroduction = WebUtility.HtmlEncode(introduction);
        var encodedActionLabel = WebUtility.HtmlEncode(actionLabel);
        var encodedClosing = WebUtility.HtmlEncode(closing);
        var encodedCode = prominentCode is null
            ? null
            : WebUtility.HtmlEncode(prominentCode);
        var encodedCodeLabel = prominentCodeLabel is null
            ? null
            : WebUtility.HtmlEncode(prominentCodeLabel);

        var codePlainText = prominentCode is null
            ? string.Empty
            : $"\n\n{prominentCodeLabel}:\n{prominentCode}";
        var plainText =
            $"{heading}\n\n{introduction}{codePlainText}\n\n{actionLabel}: {actionUrl}" +
            $"\n\nSign in: {loginUrl}\n\n{closing}" +
            $"\n\nPlease report any bugs to {supportAddress}.";

        var codeHtml = encodedCode is null
            ? string.Empty
            : $$"""
                <p style="margin:0 0 8px;color:#5f6b85;font-size:12px;font-weight:700;letter-spacing:.08em;text-align:center;text-transform:uppercase;">{{encodedCodeLabel}}</p>
                <div style="margin:0 0 10px;background:#eef2ff;border-radius:12px;padding:18px 14px;text-align:center;">
                  <span style="color:#17399c;font-family:Consolas,'Courier New',monospace;font-size:20px;font-weight:700;letter-spacing:.035em;line-height:1.5;overflow-wrap:anywhere;word-break:break-word;">{{encodedCode}}</span>
                </div>
                """;

        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>{{encodedSubject}}</title>
            </head>
            <body style="margin:0;background:#f4f6fb;color:#111827;font-family:Arial,sans-serif;">
              <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{{encodedIntroduction}}</div>
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f6fb;border-collapse:collapse;">
                <tr>
                  <td align="center" style="padding:36px 16px;">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border:1px solid #dbe2f0;border-radius:18px;border-collapse:separate;overflow:hidden;">
                      <tr>
                        <td style="background:#182342;padding:24px 32px;">
                          <p style="margin:0;color:#9eb5ff;font-size:12px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;">Job Application Tracker</p>
                          <p style="margin:7px 0 0;color:#ffffff;font-size:15px;line-height:1.5;">A private place to organise your job search.</p>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <h1 style="margin:0 0 14px;color:#111827;font-size:28px;line-height:1.2;">{{encodedHeading}}</h1>
                          <p style="margin:0 0 24px;color:#35415c;font-size:16px;line-height:1.65;">{{encodedIntroduction}}</p>
                          {{codeHtml}}
                          <p style="margin:24px 0;text-align:center;">
                            <a href="{{encodedActionUrl}}" style="display:inline-block;background:#3f63dd;color:#ffffff;text-decoration:none;font-size:15px;font-weight:700;padding:14px 22px;border-radius:10px;">{{encodedActionLabel}}</a>
                          </p>
                          <p style="margin:0 0 22px;color:#5f6b85;font-size:14px;line-height:1.6;text-align:center;">
                            Already have a verified account? <a href="{{encodedLoginUrl}}" style="color:#2449c6;font-weight:700;">Sign in</a>
                          </p>
                          <div style="border-top:1px solid #e3e8f3;margin:0 0 20px;"></div>
                          <p style="margin:0 0 8px;color:#5f6b85;font-size:13px;line-height:1.6;">{{encodedClosing}}</p>
                          <p style="margin:0;color:#5f6b85;font-size:13px;line-height:1.6;">Please report any bugs to <a href="mailto:{{encodedSupportAddress}}" style="color:#2449c6;">{{encodedSupportAddress}}</a>.</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new AccountEmailContent(subject, plainText, html);
    }

    private static string BuildAccountUrl(string publicBaseUrl, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicBaseUrl);
        var normalizedBaseUrl = publicBaseUrl.TrimEnd('/') + "/";
        var baseUri = new Uri(normalizedBaseUrl, UriKind.Absolute);
        return new Uri(baseUri, path).AbsoluteUri;
    }
}
