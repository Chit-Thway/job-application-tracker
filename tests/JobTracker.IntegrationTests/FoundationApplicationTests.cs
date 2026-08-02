using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JobTracker.IntegrationTests;

public sealed class FoundationApplicationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public FoundationApplicationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("/", "Job Application Tracker")]
    [InlineData("/dashboard", "The signal, without the noise.")]
    [InlineData("/applications", "Every opportunity, easy to find.")]
    [InlineData("/applications/new", "Start with what you have.")]
    [InlineData("/actions", "Know your next move.")]
    [InlineData("/settings", "Simple controls, clear consequences.")]
    [InlineData("/demo", "A realistic demo. Never real data.")]
    public async Task FoundationRoutes_ReturnSuccessfulBrandedPages(
        string route,
        string expectedText)
    {
        var client = CreateClient();

        var response = await client.GetAsync(route);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, content, StringComparison.Ordinal);
        Assert.Contains("Milestone 1 · Foundation", content, StringComparison.Ordinal);
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
