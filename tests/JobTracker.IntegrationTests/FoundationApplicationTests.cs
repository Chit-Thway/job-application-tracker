using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTracker.IntegrationTests;

public sealed class FoundationApplicationTests : IClassFixture<JobTrackerWebApplicationFactory>
{
    private readonly JobTrackerWebApplicationFactory factory;

    public FoundationApplicationTests(JobTrackerWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("/", "Job Application Tracker", "Milestone 8", "Retention review and scheduled cleanup")]
    [InlineData("/demo", "A realistic tracker", "Milestone 9", "Public synthetic demo")]
    public async Task FoundationRoutes_ReturnSuccessfulBrandedPages(
        string route,
        string expectedText,
        string milestone,
        string footerText)
    {
        var client = CreateClient();

        var response = await client.GetAsync(route);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, content, StringComparison.Ordinal);
        Assert.Contains(milestone, content, StringComparison.Ordinal);
        Assert.Contains(footerText, content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/applications")]
    [InlineData("/applications/new")]
    [InlineData("/applications/import/text")]
    [InlineData("/applications/import/url")]
    [InlineData("/applications/import/extension")]
    [InlineData("/actions")]
    [InlineData("/settings")]
    public async Task PrivateRoutes_RedirectSignedOutVisitorsToLogin(string route)
    {
        var client = CreateClient();

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(
            "/account/login?ReturnUrl=",
            response.Headers.Location?.PathAndQuery,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOnlyItsStatus()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Healthy", content);
    }

    [Fact]
    public async Task Stylesheet_IsServedAsCss()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/css/site.css");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("--blue: #3e63dd", content, StringComparison.Ordinal);
        Assert.Contains("html[data-theme=\"dark\"]", content, StringComparison.Ordinal);
        Assert.Contains(".detail-main > #history", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThemeScript_IsServedAndPublicLayoutOffersAccessibleToggle()
    {
        var client = CreateClient();

        var pageResponse = await client.GetAsync("/");
        var pageContent = await pageResponse.Content.ReadAsStringAsync();
        var scriptResponse = await client.GetAsync("/js/theme.js");
        var scriptContent = await scriptResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Contains("id=\"theme-toggle\"", pageContent, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Switch to dark mode\"", pageContent, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
        Assert.Contains("javascript", scriptResponse.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("job-tracker-theme", scriptContent, StringComparison.Ordinal);
        Assert.Contains("localStorage", scriptContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DashboardPipelineScript_ProvidesPointerAndKeyboardDetails()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/js/dashboard-pipeline.js");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pointerenter", content, StringComparison.Ordinal);
        Assert.Contains("focus", content, StringComparison.Ordinal);
        Assert.Contains("data-pipeline-tooltip", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplicationIndexScript_AllowsCardSurfaceSelection()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/js/application-index.js");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("card.addEventListener(\"click\"", content, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", content, StringComparison.Ordinal);
        Assert.Contains("checkbox.checked = !checkbox.checked", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Responses_IncludeBaselineSecurityHeaders()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal("nosniff", GetHeader(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", GetHeader(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", GetHeader(response, "Referrer-Policy"));
        Assert.Contains("frame-ancestors 'none'", GetHeader(response, "Content-Security-Policy"));
    }

    [Fact]
    public async Task SignedOutDesktopNavigation_IsExpandedAndOffersSignIn()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<details class=\"site-nav\" open>", content, StringComparison.Ordinal);
        Assert.Contains("href=\"/account/login\">Sign in</a>", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsFriendlyNotFoundPage()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/this-page-does-not-exist");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("That page wandered off.", content, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", content, StringComparison.Ordinal);
    }

    private HttpClient CreateClient()
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
    }

    private static string GetHeader(HttpResponseMessage response, string headerName)
    {
        Assert.True(response.Headers.TryGetValues(headerName, out var values));
        return Assert.Single(values);
    }
}
