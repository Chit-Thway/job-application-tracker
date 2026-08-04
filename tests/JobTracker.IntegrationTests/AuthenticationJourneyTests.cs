using System.Net;
using System.Text.RegularExpressions;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.AspNetCore.Identity;
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
    public async Task LoginAndInvitationRegistrationPages_ArePubliclyAvailable()
    {
        var client = CreateClient();

        var loginResponse = await client.GetAsync("/account/login");
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        var registrationResponse = await client.GetAsync("/account/register");
        var registrationContent = await registrationResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Contains("Welcome back.", loginContent, StringComparison.Ordinal);
        Assert.Contains("one-time invitation code", loginContent, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, registrationResponse.StatusCode);
        Assert.Contains("Create your account.", registrationContent, StringComparison.Ordinal);
        Assert.Contains("Invitation code", registrationContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidInvitation_CreatesUnverifiedAccount_VerifiesAndCannotBeReused()
    {
        var invitation = await CreateInvitationAsync();
        var email = $"invited-{Guid.NewGuid():N}@example.test";
        var client = CreateClient();

        var registrationResponse = await PostRegistrationAsync(
            client,
            invitation.Code,
            email,
            TestPassword);
        var registrationContent = await registrationResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, registrationResponse.StatusCode);
        Assert.Contains("Your account was created", registrationContent, StringComparison.Ordinal);

        string userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await manager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.False(user.EmailConfirmed);
            userId = user.Id;

            var storedInvitation = await database.Invitations
                .SingleAsync(item => item.Id == invitation.Id);
            Assert.NotNull(storedInvitation.UsedAt);
            Assert.Equal(user.Id, storedInvitation.UsedByUserId);
            Assert.Equal(64, storedInvitation.CodeHash.Length);
            Assert.DoesNotContain(invitation.Code, storedInvitation.CodeHash, StringComparison.Ordinal);
        }

        var message = FindMessage(email, "Verify");
        var confirmResponse = await client.GetAsync(message.ActionUrl);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByIdAsync(userId);
            Assert.NotNull(user);
            Assert.True(user.EmailConfirmed);
        }

        var loginResponse = await PostLoginAsync(client, email, TestPassword);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var secondEmail = $"reuse-{Guid.NewGuid():N}@example.test";
        var secondClient = CreateClient();
        var reuseResponse = await PostRegistrationAsync(
            secondClient,
            invitation.Code,
            secondEmail,
            TestPassword);
        var reuseContent = await reuseResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, reuseResponse.StatusCode);
        Assert.Contains("Registration could not be completed", reuseContent, StringComparison.Ordinal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Null(await manager.FindByEmailAsync(secondEmail));
        }
    }

    [Fact]
    public async Task InvalidExpiredAndRevokedInvitations_AreRejectedWithoutCreatingAccounts()
    {
        var now = DateTimeOffset.UtcNow;
        var expiredCode = InvitationCode.Create();
        var revokedCode = InvitationCode.Create();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            database.Invitations.AddRange(
                new Invitation
                {
                    CodeHash = InvitationCode.Hash(expiredCode),
                    CreatedAt = now.AddDays(-3),
                    ExpiresAt = now.AddDays(-2),
                },
                new Invitation
                {
                    CodeHash = InvitationCode.Hash(revokedCode),
                    CreatedAt = now,
                    ExpiresAt = now.AddDays(2),
                    RevokedAt = now,
                });
            await database.SaveChangesAsync();
        }

        var rejectedCodes = new[] { InvitationCode.Create(), expiredCode, revokedCode };
        foreach (var code in rejectedCodes)
        {
            var email = $"rejected-{Guid.NewGuid():N}@example.test";
            var client = CreateClient();
            var response = await PostRegistrationAsync(client, code, email, TestPassword);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Registration could not be completed", content, StringComparison.Ordinal);

            await using var scope = factory.Services.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Null(await manager.FindByEmailAsync(email));
        }
    }

    [Fact]
    public async Task InvitationConcurrencyStamp_IsConfiguredAndAvailableCodeCanBeRevoked()
    {
        var invitation = await CreateInvitationAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var concurrencyProperty = database.Model
            .FindEntityType(typeof(Invitation))!
            .FindProperty(nameof(Invitation.ConcurrencyStamp));
        Assert.NotNull(concurrencyProperty);
        Assert.True(concurrencyProperty.IsConcurrencyToken);

        var service = scope.ServiceProvider.GetRequiredService<InvitationService>();
        Assert.True(await service.RevokeAsync(invitation.Id));
        Assert.False(await service.RevokeAsync(invitation.Id));

        var summary = Assert.Single(
            await service.ListAsync(),
            item => item.Id == invitation.Id);
        Assert.Equal("Revoked", summary.Status);
    }

    [Fact]
    public async Task TwoInvitationClaims_WithTheSameOriginalStamp_CannotBothBeSaved()
    {
        var invitation = await CreateInvitationAsync();
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var firstDatabase = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDatabase = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstClaim = await firstDatabase.Invitations.SingleAsync(item => item.Id == invitation.Id);
        var secondClaim = await secondDatabase.Invitations.SingleAsync(item => item.Id == invitation.Id);

        firstClaim.UsedAt = DateTimeOffset.UtcNow;
        firstClaim.ConcurrencyStamp = Guid.NewGuid();
        await firstDatabase.SaveChangesAsync();

        secondClaim.UsedAt = DateTimeOffset.UtcNow;
        secondClaim.ConcurrencyStamp = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondDatabase.SaveChangesAsync());
    }

    [Fact]
    public async Task UnverifiedUserCannotSignIn_ButVerifiedUserCanSignInAndOut()
    {
        var email = $"verified-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(email, emailConfirmed: false);
        var client = CreateClient();

        var unverifiedResponse = await PostLoginAsync(client, email, TestPassword);
        var unverifiedContent = await unverifiedResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, unverifiedResponse.StatusCode);
        Assert.Contains("Unable to sign in", unverifiedContent, StringComparison.Ordinal);

        await ConfirmUserAsync(user.Id);

        var verifiedResponse = await PostLoginAsync(client, email, TestPassword);
        Assert.Equal(HttpStatusCode.Redirect, verifiedResponse.StatusCode);
        Assert.Equal("/dashboard", verifiedResponse.Headers.Location?.OriginalString);

        var privatePage = await client.GetAsync("/dashboard");
        var privateContent = await privatePage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, privatePage.StatusCode);
        Assert.Contains("The signal, without the noise.", privateContent, StringComparison.Ordinal);

        var logoutToken = ExtractAntiforgeryToken(privateContent);
        var logoutResponse = await client.PostAsync(
            "/account/logout",
            Form(("__RequestVerificationToken", logoutToken)));
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);

        var afterLogout = await client.GetAsync("/dashboard");
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_UsesSameResponseForUnknownAccount()
    {
        var client = CreateClient();
        var page = await client.GetAsync("/account/forgot-password");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

        var response = await client.PostAsync(
            "/account/forgot-password",
            Form(
                ("Email", $"missing-{Guid.NewGuid():N}@example.test"),
                ("__RequestVerificationToken", token)));
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("If a verified account matches", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerificationMessage_ConfirmsAccountAndAllowsLogin()
    {
        var email = $"confirm-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, emailConfirmed: false);
        var client = CreateClient();

        var resendPage = await client.GetAsync("/account/resend-verification");
        var resendToken = ExtractAntiforgeryToken(await resendPage.Content.ReadAsStringAsync());
        var resendResponse = await client.PostAsync(
            "/account/resend-verification",
            Form(
                ("Email", email),
                ("__RequestVerificationToken", resendToken)));

        Assert.Equal(HttpStatusCode.OK, resendResponse.StatusCode);
        var message = FindMessage(email, "Verify");

        var confirmResponse = await client.GetAsync(message.ActionUrl);
        var confirmContent = await confirmResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        Assert.Contains("Email verified", confirmContent, StringComparison.Ordinal);

        var loginResponse = await PostLoginAsync(client, email, TestPassword);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
    }

    [Fact]
    public async Task VerifiedUser_CanResetPasswordAndUseNewPassword()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, emailConfirmed: true);
        var client = CreateClient();

        var forgotPage = await client.GetAsync("/account/forgot-password");
        var forgotToken = ExtractAntiforgeryToken(await forgotPage.Content.ReadAsStringAsync());
        var forgotResponse = await client.PostAsync(
            "/account/forgot-password",
            Form(
                ("Email", email),
                ("__RequestVerificationToken", forgotToken)));
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

        var message = FindMessage(email, "Reset");
        var resetPage = await client.GetAsync(message.ActionUrl);
        var resetContent = await resetPage.Content.ReadAsStringAsync();
        var resetToken = ExtractAntiforgeryToken(resetContent);
        var resetUri = new Uri(message.ActionUrl);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(resetUri.Query);
        var newPassword = "A-New-Strong-Password-739!";

        var resetResponse = await client.PostAsync(
            "/account/reset-password",
            Form(
                ("UserId", query["userId"].ToString()),
                ("Code", query["code"].ToString()),
                ("Password", newPassword),
                ("ConfirmPassword", newPassword),
                ("__RequestVerificationToken", resetToken)));
        var resultContent = await resetResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        Assert.Contains("Password changed", resultContent, StringComparison.Ordinal);

        var loginResponse = await PostLoginAsync(client, email, newPassword);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
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

    private async Task<CreatedInvitation> CreateInvitationAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();
        return await invitations.CreateAsync(validForDays: 7);
    }

    private async Task ConfirmUserAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByIdAsync(userId);
        Assert.NotNull(user);
        var token = await manager.GenerateEmailConfirmationTokenAsync(user);
        var result = await manager.ConfirmEmailAsync(user, token);
        Assert.True(result.Succeeded);
    }

    private DevelopmentMailMessage FindMessage(string recipient, string subjectPrefix)
    {
        var store = factory.Services.GetRequiredService<DevelopmentMailStore>();
        return store.Messages.First(message =>
            string.Equals(message.Recipient, recipient, StringComparison.OrdinalIgnoreCase)
            && message.Subject.StartsWith(subjectPrefix, StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        var page = await client.GetAsync("/account/login");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        return await client.PostAsync(
            "/account/login",
            Form(
                ("Email", email),
                ("Password", password),
                ("RememberMe", "false"),
                ("__RequestVerificationToken", token)));
    }

    private static async Task<HttpResponseMessage> PostRegistrationAsync(
        HttpClient client,
        string invitationCode,
        string email,
        string password)
    {
        var page = await client.GetAsync("/account/register");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        return await client.PostAsync(
            "/account/register",
            Form(
                ("DisplayName", "Invited Test User"),
                ("Email", email),
                ("Password", password),
                ("ConfirmPassword", password),
                ("InvitationCode", invitationCode),
                ("__RequestVerificationToken", token)));
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
