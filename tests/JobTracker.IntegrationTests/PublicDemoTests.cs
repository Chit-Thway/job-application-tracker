using System.Net;
using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed class PublicDemoTests : IClassFixture<JobTrackerWebApplicationFactory>
{
    private const string PrivateCanary = "PRIVATE-CANARY-NEVER-IN-DEMO";
    private readonly JobTrackerWebApplicationFactory factory;

    public PublicDemoTests(JobTrackerWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("/demo", "A realistic tracker")]
    [InlineData("/demo/applications", "synthetic applications")]
    [InlineData("/demo/actions", "Synthetic Action Centre")]
    [InlineData("/demo/applications/nova-harbour-graduate-platform-engineer", "Graduate Platform Engineer")]
    public async Task PublicDemo_IsBrowsableAndContainsOnlySyntheticReadOnlyContent(
        string route,
        string expectedText)
    {
        await SeedPrivateCanaryAsync();
        var client = CreateClient();

        var response = await client.GetAsync(route);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Synthetic, read-only demonstration", content, StringComparison.Ordinal);
        Assert.Contains("Private sign in", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Sign out", content, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateCanary, content, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public async Task DemoQuery_FiltersOnlyTheSyntheticCatalog()
    {
        await SeedPrivateCanaryAsync();
        var client = CreateClient();

        var response = await client.GetAsync("/demo/applications?query=Nova");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nova Harbour Labs", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Atlas Ember Systems", content, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateCanary, content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownDemoIdentifier_ReturnsSyntheticNotFoundWithoutPrivateFallback()
    {
        await SeedPrivateCanaryAsync();
        var client = CreateClient();

        var response = await client.GetAsync($"/demo/applications/{Guid.NewGuid()}");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("fixed public catalog", content, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateCanary, content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task DemoMutationMethods_AreAlwaysRejected(string method)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/demo/applications/nova-harbour-graduate-platform-engineer")
        {
            Content = new StringContent("{}"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private async Task SeedPrivateCanaryAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await database.JobApplications.AnyAsync(item => item.RoleTitle == PrivateCanary))
        {
            return;
        }

        database.JobApplications.Add(new JobApplication
        {
            OwnerId = "private-demo-isolation-owner",
            RoleTitle = PrivateCanary,
            AppliedOn = new DateOnly(2026, 8, 14),
            Notes = PrivateCanary,
        });
        await database.SaveChangesAsync();
    }

    private HttpClient CreateClient() => factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    });
}
