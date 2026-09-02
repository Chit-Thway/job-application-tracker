using Microsoft.Playwright;
using JobTracker.Web.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace JobTracker.BrowserTests;

[Collection(BrowserJourneyCollection.Name)]
public sealed class CriticalJourneysTests(BrowserJourneyFixture fixture)
{
    [Theory]
    [InlineData(1280, 900)]
    [InlineData(390, 844)]
    public async Task PublicLanding_IsConciseResponsiveAndShowsProductPreviews(int width, int height)
    {
        await using var context = await fixture.CreateContextAsync(width, height);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Track every application.", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true }).Last)
            .ToBeVisibleAsync();
        await Expect(page.GetByAltText("Colourful application pipeline chart in the read-only demo dashboard"))
            .ToBeVisibleAsync();
        var extensionVideo = page.Locator("video[aria-label='Job Application Tracker browser extension capture demonstration']");
        await Expect(extensionVideo).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(1_500);
        var videoState = await extensionVideo.EvaluateAsync<string>(
            "video => JSON.stringify({ source: video.currentSrc, duration: video.duration, readyState: video.readyState, networkState: video.networkState, error: video.error?.code ?? null })");
        Assert.True(
            await extensionVideo.EvaluateAsync<bool>("video => Number.isFinite(video.duration) && video.duration > 0"),
            videoState);
        Assert.True(await extensionVideo.EvaluateAsync<bool>("video => video.paused"));
        var videoToggle = page.Locator("[data-extension-video]");
        await Expect(videoToggle).ToBeVisibleAsync();
        await Expect(videoToggle).ToHaveAttributeAsync("aria-label", "Play browser extension demonstration");
        await videoToggle.ClickAsync();
        await Expect(videoToggle).ToHaveClassAsync(new Regex("has-started"));
        Assert.False(await extensionVideo.EvaluateAsync<bool>("video => video.paused"));
        await Expect(extensionVideo).ToHaveAttributeAsync("controls", string.Empty);
        await Expect(page.GetByText("Your job search, brought into focus.", new() { Exact = true }))
            .ToHaveCountAsync(0);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await AssertAccessiblePageStructureAsync(page);
    }

    [Fact]
    public async Task LoginManualCreateSaveStatusAndDashboardJourney_WorksWithKeyboardReadyPages()
    {
        await using var context = await fixture.CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/account/login");
        await AssertAccessiblePageStructureAsync(page);
        await page.Keyboard.PressAsync("Tab");
        await Expect(page.Locator(":focus")).ToHaveTextAsync("Skip to main content");

        await page.GetByLabel("Email").FillAsync(BrowserJourneyFixture.Email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserJourneyFixture.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true }))
            .ToBeVisibleAsync();
        await page.GotoAsync("/");
        await Expect(page).ToHaveURLAsync(new Regex("/dashboard$"));

        await page.GetByRole(AriaRole.Link, new() { Name = "Add application", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/applications/new$"));
        await AssertAccessiblePageStructureAsync(page);
        await page.GetByLabel("Role title").FillAsync("Synthetic Browser-Test Engineer");
        await page.GetByLabel("Application date").FillAsync("2026-08-16");
        await page.GetByLabel("Job description").FillAsync(
            "About the role\n\nBuild deterministic browser coverage.\n\nWhat you will do\n\n- Test critical journeys");
        await page.GetByLabel("Saved").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Add application" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Synthetic Browser-Test Engineer" }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "This application is saved." }))
            .ToBeVisibleAsync();

        await page.GetByText("Record a status change", new() { Exact = true }).ClickAsync();
        await page.GetByLabel("Stage").SelectOptionAsync("Screening");
        await page.Locator("#Status_Note").FillAsync("Recruiter requested a short introductory call.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add to history" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Screening · Active" }))
            .ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Synthetic Browser-Test Engineer", Exact = true }).First)
            .ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Applications", Exact = true }).ClickAsync();
        var search = page.GetByLabel("Search", new() { Exact = true });
        await search.FillAsync("Browser-Test");
        await Expect(page).ToHaveURLAsync(new Regex("Query=Browser-Test"));
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Synthetic Browser-Test Engineer", Exact = true }))
            .ToBeVisibleAsync();
        await search.FillAsync("No matching application");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No applications found." }))
            .ToBeVisibleAsync();
        await search.FillAsync(string.Empty);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Synthetic Browser-Test Engineer", Exact = true }))
            .ToBeVisibleAsync();
        await AssertAccessiblePageStructureAsync(page);
    }

    [Fact]
    public async Task PublicDemo_IsReadOnlyAccessibleAndResponsive()
    {
        await using var context = await fixture.CreateContextAsync(width: 390, height: 844);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/demo");
        await Expect(page.GetByText("Synthetic, read-only demonstration."))
            .ToBeVisibleAsync();
        await Expect(page.GetByText("Essential cookies only", new() { Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Use essential cookies" }).ClickAsync();
        await Expect(page.GetByText("Essential cookies only", new() { Exact = true })).ToBeHiddenAsync();
        await AssertAccessiblePageStructureAsync(page);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));

        var navigation = page.Locator("[data-site-nav]");
        var toggle = navigation.Locator(".site-nav-toggle");
        await Expect(toggle).Not.ToBeCheckedAsync();
        await navigation.Locator(".site-nav-summary").ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Applications", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Applications" }))
            .ToBeVisibleAsync();
        Assert.Equal(0, await page.Locator("form[method='post']").CountAsync());

        await page.GetByRole(AriaRole.Link, new() { Name = "Graduate Platform Engineer" }).ClickAsync();
        await Expect(page.GetByText("Read-only synthetic record", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Equal(0, await page.Locator("button[type='submit']").CountAsync());
        await AssertAccessiblePageStructureAsync(page);
    }

    [Fact]
    public async Task OpenRegistration_EmailCodeJourneyCreatesVerifiedTierOneAccount()
    {
        await using var context = await fixture.CreateContextAsync();
        var page = await context.NewPageAsync();
        var email = $"browser-signup-{Guid.NewGuid():N}@example.test";

        await page.GotoAsync("/account/register");
        await page.GetByLabel("Your name").FillAsync("Browser Signup User");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserJourneyFixture.Password);
        await page.GetByLabel("Confirm password").FillAsync(BrowserJourneyFixture.Password);
        await page.GetByLabel("I agree to the Terms of Service and acknowledge the Privacy Policy").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Enter your six-digit code." }))
            .ToBeVisibleAsync();
        await Expect(page.GetByText(email, new() { Exact = true })).ToBeVisibleAsync();
        var resend = page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Resend code in [0-9]+s") });
        await Expect(resend).ToBeDisabledAsync();

        var message = fixture.Factory.Services
            .GetRequiredService<DevelopmentMailStore>()
            .Messages
            .First(item => string.Equals(item.Recipient, email, StringComparison.OrdinalIgnoreCase));
        await page.GetByLabel("Verification code").FillAsync(message.OneTimeCode!);
        await page.GetByRole(AriaRole.Button, new() { Name = "Verify email" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Email verified" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Resend an email verification code" })).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Link, new() { Name = "Go to sign in" }).ClickAsync();
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserJourneyFixture.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Demo", Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ReviewOriginalSource_CopyButtonCopiesTheCompleteText()
    {
        await using var context = await fixture.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(
            """
            Object.defineProperty(navigator, "clipboard", {
                configurable: true,
                value: {
                    writeText: async text => { window.__copiedSource = text; }
                }
            });
            """);
        const string source =
            "Job title: Clipboard Engineer\nCompany: Synthetic Copy Labs\n\nPreserve every line exactly.";

        await SignInAsync(page);
        await page.GotoAsync("/applications/import/text");
        await page.GetByLabel("Job description or posting text").FillAsync(source);
        await page.GetByRole(AriaRole.Button, new() { Name = "Extract a review draft" }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var copy = page.GetByRole(
            AriaRole.Button,
            new() { Name = "Copy the complete original source" });
        await Expect(copy).ToBeVisibleAsync();
        await Expect(copy).ToHaveAttributeAsync("data-copy-ready", "true");
        await copy.ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status))
            .ToContainTextAsync("Original source copied to the clipboard.");
        var displayedSource = await page.Locator("#original-source-text").TextContentAsync();
        Assert.Equal(displayedSource, await page.EvaluateAsync<string>("window.__copiedSource"));
        Assert.Contains("Preserve every line exactly.", displayedSource, StringComparison.Ordinal);
    }


    [Fact]
    public async Task MobileNavigation_StartsClosedAndCanBeOpenedAndDismissed()
    {
        await using var context = await fixture.CreateContextAsync(width: 390, height: 844);
        var page = await context.NewPageAsync();

        foreach (var path in new[] { "/account/login", "/demo" })
        {
            await page.GotoAsync(path);
            var navigation = page.Locator("[data-site-nav]");
            var menu = navigation.Locator(".site-nav-summary");
            var toggle = navigation.Locator(".site-nav-toggle");

            await Expect(toggle).Not.ToBeCheckedAsync();
            await Expect(menu).ToBeVisibleAsync();

            await menu.ClickAsync();
            await Expect(toggle).ToBeCheckedAsync();

            await menu.ClickAsync();
            await Expect(toggle).Not.ToBeCheckedAsync();
            await Expect(page.Locator("#theme-toggle")).ToBeVisibleAsync();
        }
    }
    private static async Task AssertAccessiblePageStructureAsync(IPage page)
    {
        var violations = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                if (document.documentElement.lang !== 'en') problems.push('html language');
                if (!document.title.trim()) problems.push('document title');
                if (document.querySelectorAll('main').length !== 1) problems.push('single main landmark');
                if (document.querySelectorAll('h1').length !== 1) problems.push('single h1');
                if (!document.querySelector('a.skip-link[href="#main-content"]')) problems.push('skip link');

                const ids = [...document.querySelectorAll('[id]')].map(element => element.id);
                if (new Set(ids).size !== ids.length) problems.push('duplicate ids');

                for (const control of document.querySelectorAll('input:not([type="hidden"]), select, textarea')) {
                    const labelled = control.getAttribute('aria-label')
                        || control.getAttribute('aria-labelledby')
                        || (control.id && document.querySelector(`label[for="${CSS.escape(control.id)}"]`))
                        || control.closest('label');
                    if (!labelled) problems.push(`unlabelled control: ${control.name || control.tagName}`);
                }

                for (const image of document.querySelectorAll('img')) {
                    if (!image.hasAttribute('alt')) problems.push('image without alt');
                }

                return problems;
            }
            """);

        Assert.Empty(violations);
    }

    private static async Task SignInAsync(IPage page)
    {
        await page.GotoAsync("/account/login");
        await page.GetByLabel("Email").FillAsync(BrowserJourneyFixture.Email);
        await page.GetByLabel("Password", new() { Exact = true })
            .FillAsync(BrowserJourneyFixture.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }
}
