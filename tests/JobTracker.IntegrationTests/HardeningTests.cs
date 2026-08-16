using System.Net;
using System.Text.RegularExpressions;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class HardeningTests : IClassFixture<JobTrackerWebApplicationFactory>
{
    private const string Password = "Hardening-Test-Password-482!";
    private readonly JobTrackerWebApplicationFactory factory;

    public HardeningTests(JobTrackerWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("/account/login")]
    [InlineData("/demo")]
    public async Task BrowserResponses_IncludeLaunchSecurityAndCorrelationHeaders(string path)
    {
        var response = await CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Resource-Policy"));
        Assert.Equal("none", Header(response, "X-Permitted-Cross-Domain-Policies"));
        Assert.Contains("default-src 'self'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("camera=()", Header(response, "Permissions-Policy"), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(Header(response, "X-Request-ID")));
    }

    [Theory]
    [InlineData("/health/live", "self")]
    [InlineData("/health/ready", "database")]
    [InlineData("/health", "database")]
    public async Task HealthEndpoints_AreMachineReadableAndDoNotExposeExceptions(
        string path,
        string expectedCheck)
    {
        var response = await CreateClient().GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store, max-age=0", Header(response, "Cache-Control"));
        Assert.Contains("\"status\":\"Healthy\"", content, StringComparison.Ordinal);
        Assert.Contains($"\"name\":\"{expectedCheck}\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("exception", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticatedPages_AreExplicitlyNotCached()
    {
        var email = $"hardening-{Guid.NewGuid():N}@example.test";
        await CreateVerifiedUserAsync(email);
        var client = CreateClient();
        await SignInAsync(client, email);

        var response = await client.GetAsync("/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(TimeSpan.Zero, response.Headers.CacheControl?.MaxAge);
        Assert.Equal("no-cache", Header(response, "Pragma"));
    }

    private async Task CreateVerifiedUserAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await users.CreateAsync(
            new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Hardening Test User",
                TimeZoneId = "Australia/Perth",
            },
            Password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
    }

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var loginPage = await client.GetAsync("/account/login");
        var token = ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Password"] = Password,
                ["RememberMe"] = "false",
                ["__RequestVerificationToken"] = token,
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? string.Join(",", values)
            : string.Empty;

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "The page did not contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}
