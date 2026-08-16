using JobTracker.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace JobTracker.BrowserTests;

public sealed class BrowserJourneyFixture : IAsyncLifetime
{
    public const string Email = "browser-journey@example.test";
    public const string Password = "Browser-Test-Password-482!";

    public BrowserTestApplicationFactory Factory { get; } = new();

    public Uri BaseAddress { get; private set; } = null!;

    public IPlaywright Playwright { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        BaseAddress = Factory.StartAndGetAddress();
        await SeedVerifiedUserAsync();
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "chromium",
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        Playwright?.Dispose();
        await Factory.DisposeAsync();
    }

    public Task<IBrowserContext> CreateContextAsync(int width = 1280, int height = 900) =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseAddress.AbsoluteUri,
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });

    private async Task SeedVerifiedUserAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(Email) is not null)
        {
            return;
        }

        var result = await users.CreateAsync(
            new ApplicationUser
            {
                UserName = Email,
                Email = Email,
                EmailConfirmed = true,
                DisplayName = "Browser Journey User",
                TimeZoneId = "Australia/Perth",
            },
            Password);

        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrowserJourneyCollection : ICollectionFixture<BrowserJourneyFixture>
{
    public const string Name = "Critical browser journeys";
}
