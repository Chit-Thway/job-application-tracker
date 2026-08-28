using System.Net;
using System.Text.RegularExpressions;
using JobTracker.Web.Admin;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class AdminManagementTests
    : IClassFixture<JobTrackerWebApplicationFactory>
{
    private const string TestPassword = "A-Strong-Admin-Test-482!";
    private readonly JobTrackerWebApplicationFactory factory;

    public AdminManagementTests(JobTrackerWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AdminPages_RequireAdministratorRole()
    {
        var user = await CreateUserAsync(admin: false);
        var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, user.Email!)).StatusCode);

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            "/account/access-denied",
            response.Headers.Location?.OriginalString,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administrator_CanOpenPrivateOverviewAndNavigation()
    {
        var administrator = await CreateUserAsync(admin: true);
        var client = CreateClient();
        await LoginAsync(client, administrator.Email!);

        var response = await client.GetAsync("/admin");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Administration.", content, StringComparison.Ordinal);
        Assert.Contains("What admins cannot see", content, StringComparison.Ordinal);
        Assert.Contains("href=\"/admin\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain(TestPassword, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoleAndLockOperations_ProtectCurrentAdministratorAndWriteAudit()
    {
        var administrator = await CreateUserAsync(admin: true);
        var user = await CreateUserAsync(admin: false);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AdminManagementService>();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var selfDemotion = await service.RemoveAdminAsync(administrator.Id, administrator.Id);
        var selfLock = await service.SetLockedAsync(administrator.Id, administrator.Id, true);
        var promotion = await service.PromoteAsync(administrator.Id, user.Id);
        var lockResult = await service.SetLockedAsync(administrator.Id, user.Id, true);
        var unlockResult = await service.SetLockedAsync(administrator.Id, user.Id, false);
        var removal = await service.RemoveAdminAsync(administrator.Id, user.Id);

        Assert.False(selfDemotion.Succeeded);
        Assert.False(selfLock.Succeeded);
        Assert.True(promotion.Succeeded);
        Assert.True(lockResult.Succeeded);
        Assert.True(unlockResult.Succeeded);
        Assert.True(removal.Succeeded);
        Assert.True(await database.AdminAuditEntries.CountAsync() >= 4);
    }

    [Fact]
    public async Task Administrator_CanReviewAccountMetadataAndAuditTierChanges()
    {
        var administrator = await CreateUserAsync(admin: true);
        var user = await CreateUserAsync(
            admin: false,
            phoneNumber: "+61400000001");
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AdminManagementService>();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        database.JobApplications.AddRange(
            new JobApplication
            {
                OwnerId = user.Id,
                RoleTitle = "First private role",
                AppliedOn = new DateOnly(2026, 8, 27),
            },
            new JobApplication
            {
                OwnerId = user.Id,
                RoleTitle = "Second private role",
                AppliedOn = new DateOnly(2026, 8, 28),
            });
        await database.SaveChangesAsync();

        var accounts = await service.GetUsersAsync("400000001");
        var account = Assert.Single(accounts.Users);
        var upgraded = await service.SetTierAsync(
            administrator.Id,
            user.Id,
            AccountTier.Tier2);

        Assert.Equal("+61400000001", account.PhoneNumber);
        Assert.Equal(2, account.ApplicationCount);
        Assert.Equal(AccountTier.Tier1, account.AccountTier);
        Assert.True(upgraded.Succeeded);
        Assert.Equal(AccountTier.Tier2, (await database.Users.FindAsync(user.Id))!.AccountTier);
        Assert.Contains(await database.AdminAuditEntries.ToListAsync(), entry =>
            entry.ActorUserId == administrator.Id
            && entry.TargetUserId == user.Id
            && entry.Action == "Account tier changed");
    }

    [Fact]
    public async Task AdminVerificationResend_SendsCodeAndIsAudited()
    {
        var administrator = await CreateUserAsync(admin: true);
        var unverified = await CreateUserAsync(admin: false, confirmed: false);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AdminManagementService>();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var verificationResult = await service.ResendVerificationAsync(
            administrator.Id,
            unverified.Id,
            challengeId => $"https://tracker.example.test/account/verify-email?challengeId={challengeId:D}");

        var messages = factory.Services
            .GetRequiredService<DevelopmentMailStore>()
            .Messages;
        Assert.True(verificationResult.Succeeded);
        var message = Assert.Single(messages, item => item.Recipient == unverified.Email);
        Assert.Matches("^[0-9]{6}$", message.OneTimeCode);
        Assert.Equal(1, await database.AdminAuditEntries.CountAsync(item =>
            item.ActorUserId == administrator.Id
            && item.Action == "Verification resent"));
    }

    private async Task<ApplicationUser> CreateUserAsync(
        bool admin,
        bool confirmed = true,
        string? phoneNumber = null)
    {
        var email = $"admin-test-{Guid.NewGuid():N}@example.test";
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            PhoneNumber = phoneNumber,
            DisplayName = admin ? "Administrator Test" : "User Test",
            TimeZoneId = "Australia/Perth",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var result = await manager.CreateAsync(user, TestPassword);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
        if (admin)
        {
            var roleResult = await manager.AddToRoleAsync(user, AdminRole.Name);
            Assert.True(roleResult.Succeeded);
        }

        return user;
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email)
    {
        var page = await client.GetAsync("/account/login");
        var html = await page.Content.ReadAsStringAsync();
        var tokenMatch = AntiforgeryTokenRegex().Match(html);
        Assert.True(tokenMatch.Success);
        var token = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value);
        return await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Password"] = TestPassword,
                ["RememberMe"] = "false",
                ["__RequestVerificationToken"] = token,
            }));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}
