using System.Net;
using System.Text.RegularExpressions;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class AuthenticationJourneyTests
    : IClassFixture<JobTrackerWebApplicationFactory>
{
    private const string TestPassword = "A-Strong-Test-Password-482!";
    private readonly JobTrackerWebApplicationFactory factory;

    public AuthenticationJourneyTests(JobTrackerWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LoginRegistrationAndLegalPages_ArePublicAndDescribeOpenRegistration()
    {
        var client = CreateClient();

        var login = await client.GetStringAsync("/account/login");
        var registration = await client.GetStringAsync("/account/register");
        var privacy = await client.GetStringAsync("/privacy");
        var terms = await client.GetStringAsync("/terms");
        var cookies = await client.GetStringAsync("/cookies");

        Assert.Contains("Welcome back.", login, StringComparison.Ordinal);
        Assert.Contains("Registration is open", login, StringComparison.Ordinal);
        Assert.DoesNotContain("Resend verification code", login, StringComparison.Ordinal);
        Assert.Contains("Open registration", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("Phone number", registration, StringComparison.Ordinal);
        Assert.DoesNotContain("Invitation code", registration, StringComparison.Ordinal);
        Assert.Contains("How Job Application Tracker handles personal information", privacy, StringComparison.Ordinal);
        Assert.Contains("The agreement for using Job Application Tracker", terms, StringComparison.Ordinal);
        Assert.Contains("Only essential security cookies are used", cookies, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenRegistration_CreatesTierOneAccountAndEmailCodeVerifiesIt()
    {
        var email = $"open-{Guid.NewGuid():N}@example.test";
        var client = CreateClient();

        var registration = await RegisterAsync(client, email, acceptPolicies: true);
        Assert.Equal(HttpStatusCode.Redirect, registration.StatusCode);
        var verificationPath = Assert.IsType<Uri>(registration.Headers.Location).OriginalString;
        Assert.StartsWith("/account/verify-email?challengeId=", verificationPath, StringComparison.Ordinal);

        var message = FindMessage(email, "Your Job Application Tracker verification code");
        Assert.Matches("^[0-9]{6}$", Assert.IsType<string>(message.OneTimeCode));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await database.Users.SingleAsync(candidate => candidate.Email == email);
            Assert.False(user.EmailConfirmed);
            Assert.Null(user.PhoneNumber);
            Assert.Equal(AccountTier.Tier1, user.AccountTier);
            Assert.Equal(LegalDocumentVersions.Terms, user.TermsVersion);
            Assert.Equal(LegalDocumentVersions.Privacy, user.PrivacyVersion);
            Assert.NotNull(user.TermsAcceptedAt);
            Assert.NotNull(user.PrivacyAcknowledgedAt);
            Assert.NotNull(user.EmailVerificationCodeHash);
            Assert.DoesNotContain(message.OneTimeCode!, user.EmailVerificationCodeHash, StringComparison.Ordinal);
        }

        var verificationPage = await client.GetAsync(verificationPath);
        var verificationContent = await verificationPage.Content.ReadAsStringAsync();
        Assert.Contains(email, verificationContent, StringComparison.Ordinal);
        Assert.Contains("data-resend-seconds=\"30\"", verificationContent, StringComparison.Ordinal);
        Assert.Contains("/js/email-verification.js", verificationContent, StringComparison.Ordinal);

        var verifyResponse = await client.PostAsync(
            "/account/verify-email",
            Form(
                ("ChallengeId", ChallengeIdFrom(verificationPath).ToString()),
                ("Code", message.OneTimeCode!),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(verificationContent))));
        var verifiedContent = await verifyResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        Assert.Contains("Email verified", verifiedContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Resend an email verification code", verifiedContent, StringComparison.Ordinal);

        var loginResponse = await PostLoginAsync(client, email, TestPassword);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/dashboard", loginResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Registration_RequiresPolicyAgreement()
    {
        var email = $"policy-{Guid.NewGuid():N}@example.test";
        var client = CreateClient();

        var page = await client.GetAsync("/account/register");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/account/register",
            Form(
                ("DisplayName", "Policy Test User"),
                ("Email", email),
                ("Password", TestPassword),
                ("ConfirmPassword", TestPassword),
                ("AcceptPolicies", "false"),
                ("__RequestVerificationToken", token)));
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("must agree to the Terms of Service", content, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByEmailAsync(email));
    }

    [Fact]
    public async Task VerificationResend_EnforcesThirtySecondCooldown()
    {
        var email = $"cooldown-{Guid.NewGuid():N}@example.test";
        var client = CreateClient();
        var registration = await RegisterAsync(client, email, acceptPolicies: true);
        var verificationPath = Assert.IsType<Uri>(registration.Headers.Location).OriginalString;
        var challengeId = ChallengeIdFrom(verificationPath);
        var originalCount = MessageCount(email);

        var page = await client.GetAsync(verificationPath);
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        var resend = await client.PostAsync(
            $"/account/verify-email/resend?challengeId={challengeId:D}",
            Form(("__RequestVerificationToken", token)));

        Assert.Equal(HttpStatusCode.Redirect, resend.StatusCode);
        Assert.Equal(originalCount, MessageCount(email));

        var resultPage = await client.GetStringAsync(resend.Headers.Location!.OriginalString);
        Assert.Contains("You can request another code in", resultPage, StringComparison.Ordinal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await database.Users.SingleAsync(candidate => candidate.Email == email);
            user.EmailVerificationCodeLastSentAt = DateTimeOffset.UtcNow.AddSeconds(-31);
            await database.SaveChangesAsync();
        }

        var allowedResend = await client.PostAsync(
            $"/account/verify-email/resend?challengeId={challengeId:D}",
            Form(("__RequestVerificationToken", ExtractAntiforgeryToken(resultPage))));
        Assert.Equal(HttpStatusCode.Redirect, allowedResend.StatusCode);
        Assert.Equal(originalCount + 1, MessageCount(email));
    }

    [Fact]
    public async Task ExpiredVerificationCode_IsRejected()
    {
        var email = $"expired-{Guid.NewGuid():N}@example.test";
        var client = CreateClient();
        var registration = await RegisterAsync(client, email, acceptPolicies: true);
        var verificationPath = Assert.IsType<Uri>(registration.Headers.Location).OriginalString;
        var challengeId = ChallengeIdFrom(verificationPath);
        var code = FindMessage(email, "Your Job Application Tracker verification code").OneTimeCode!;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await database.Users.SingleAsync(candidate => candidate.Email == email);
            user.EmailVerificationCodeExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            await database.SaveChangesAsync();
        }

        var page = await client.GetStringAsync(verificationPath);
        var response = await client.PostAsync(
            "/account/verify-email",
            Form(
                ("ChallengeId", challengeId.ToString()),
                ("Code", code),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(page))));

        Assert.Contains("That code has expired", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FiveIncorrectCodes_InvalidateTheCurrentCode()
    {
        var email = $"attempts-{Guid.NewGuid():N}@example.test";
        var client = CreateClient();
        var registration = await RegisterAsync(client, email, acceptPolicies: true);
        var verificationPath = Assert.IsType<Uri>(registration.Headers.Location).OriginalString;
        var challengeId = ChallengeIdFrom(verificationPath);
        var correctCode = FindMessage(email, "Your Job Application Tracker verification code").OneTimeCode!;

        var pageContent = await client.GetStringAsync(verificationPath);
        for (var attempt = 0; attempt < EmailVerificationService.MaxFailedAttempts; attempt++)
        {
            var response = await client.PostAsync(
                "/account/verify-email",
                Form(
                    ("ChallengeId", challengeId.ToString()),
                    ("Code", "999999"),
                    ("__RequestVerificationToken", ExtractAntiforgeryToken(pageContent))));
            pageContent = await response.Content.ReadAsStringAsync();
        }

        Assert.Contains("Too many incorrect attempts", pageContent, StringComparison.Ordinal);

        var finalResponse = await client.PostAsync(
            "/account/verify-email",
            Form(
                ("ChallengeId", challengeId.ToString()),
                ("Code", correctCode),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(pageContent))));
        Assert.Contains(
            "That code has expired",
            await finalResponse.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnverifiedUserCannotSignIn_AndUnknownResendUsesGenericResponse()
    {
        var email = $"unverified-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, emailConfirmed: false);
        var client = CreateClient();

        var unverifiedResponse = await PostLoginAsync(client, email, TestPassword);
        var unverifiedContent = await unverifiedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Email verification required", unverifiedContent, StringComparison.Ordinal);
        Assert.Contains("Send a new verification code", unverifiedContent, StringComparison.Ordinal);

        var resendPage = await client.GetStringAsync("/account/resend-verification");
        var response = await client.PostAsync(
            "/account/resend-verification",
            Form(
                ("Email", $"missing-{Guid.NewGuid():N}@example.test"),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(resendPage))));
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("If an unverified account matches", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingUnverifiedUser_CanRequestAndUseEmailCode()
    {
        var email = $"existing-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, emailConfirmed: false);
        var client = CreateClient();

        var resendPage = await client.GetStringAsync("/account/resend-verification");
        var resend = await client.PostAsync(
            "/account/resend-verification",
            Form(
                ("Email", email),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(resendPage))));
        Assert.Contains(
            "If an unverified account matches",
            await resend.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);

        var message = FindMessage(email, "Your Job Application Tracker verification code");
        var verificationUri = new Uri(message.ActionUrl);
        var challengeId = ChallengeIdFrom(verificationUri.PathAndQuery);
        var verificationPage = await client.GetStringAsync(verificationUri.PathAndQuery);
        var verified = await client.PostAsync(
            "/account/verify-email",
            Form(
                ("ChallengeId", challengeId.ToString()),
                ("Code", message.OneTimeCode!),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(verificationPage))));

        Assert.Contains("Email verified", await verified.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifiedUser_CanResetPasswordAndUseNewPassword()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, emailConfirmed: true);
        var client = CreateClient();

        var forgotPage = await client.GetStringAsync("/account/forgot-password");
        var forgotResponse = await client.PostAsync(
            "/account/forgot-password",
            Form(
                ("Email", email),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(forgotPage))));
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

        var message = FindMessage(email, "Reset");
        var resetUri = new Uri(message.ActionUrl);
        var resetPage = await client.GetStringAsync(resetUri.PathAndQuery);
        var query = QueryHelpers.ParseQuery(resetUri.Query);
        const string newPassword = "A-New-Strong-Password-739!";
        var resetResponse = await client.PostAsync(
            "/account/reset-password",
            Form(
                ("UserId", query["userId"].ToString()),
                ("Code", query["code"].ToString()),
                ("Password", newPassword),
                ("ConfirmPassword", newPassword),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(resetPage))));

        Assert.Contains("Password changed", await resetResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await PostLoginAsync(client, email, newPassword)).StatusCode);
    }

    private async Task<ApplicationUser> CreateUserAsync(string email, bool emailConfirmed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            DisplayName = "Synthetic Test User",
            TimeZoneId = "Australia/Perth",
        };
        var result = await manager.CreateAsync(user, TestPassword);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
        return user;
    }

    private async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string email,
        bool acceptPolicies)
    {
        var page = await client.GetStringAsync("/account/register");
        return await client.PostAsync(
            "/account/register",
            Form(
                ("DisplayName", "Open Registration User"),
                ("Email", email),
                ("Password", TestPassword),
                ("ConfirmPassword", TestPassword),
                ("AcceptPolicies", acceptPolicies ? "true" : "false"),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(page))));
    }

    private async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var page = await client.GetStringAsync("/account/login");
        return await client.PostAsync(
            "/account/login",
            Form(
                ("Email", email),
                ("Password", password),
                ("RememberMe", "false"),
                ("__RequestVerificationToken", ExtractAntiforgeryToken(page))));
    }

    private DevelopmentMailMessage FindMessage(string recipient, string subjectPrefix) =>
        factory.Services.GetRequiredService<DevelopmentMailStore>().Messages.First(message =>
            string.Equals(message.Recipient, recipient, StringComparison.OrdinalIgnoreCase)
            && message.Subject.StartsWith(subjectPrefix, StringComparison.Ordinal));

    private int MessageCount(string recipient) =>
        factory.Services.GetRequiredService<DevelopmentMailStore>().Messages.Count(message =>
            string.Equals(message.Recipient, recipient, StringComparison.OrdinalIgnoreCase));

    private static Guid ChallengeIdFrom(string pathAndQuery)
    {
        var query = QueryHelpers.ParseQuery(new Uri("https://localhost" + pathAndQuery).Query);
        return Guid.Parse(query["challengeId"].ToString());
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "The page did not contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}
