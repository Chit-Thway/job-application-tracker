using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace JobTracker.UnitTests;

public sealed class BrowserExtensionManifestTests
{
    [Fact]
    public void Manifest_RequiresOnlyExplicitClickPermissions()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;
        var permissions = root.GetProperty("permissions")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

        Assert.Equal(3, root.GetProperty("manifest_version").GetInt32());
        Assert.Equal("1.0.4", root.GetProperty("version").GetString());
        Assert.Equal("102", root.GetProperty("minimum_chrome_version").GetString());
        Assert.Equal(
            "https://myjobtracker.com.au/extension",
            root.GetProperty("homepage_url").GetString());
        Assert.Equal(new[] { "activeTab", "scripting", "storage" }, permissions);
        Assert.False(root.TryGetProperty("host_permissions", out _));
        Assert.False(root.TryGetProperty("content_scripts", out _));
        Assert.False(root.TryGetProperty("background", out _));
    }

    [Fact]
    public void CaptureScript_UsesRenderedJobFieldsWithoutRemoteRequests()
    {
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "popup.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("chrome.scripting.executeScript", script, StringComparison.Ordinal);
        Assert.Contains("application/ld+json", script, StringComparison.Ordinal);
        Assert.Contains("job-detail-title", script, StringComparison.Ordinal);
        Assert.Contains("advertiser-name", script, StringComparison.Ordinal);
        Assert.Contains("jobAdDetails", script, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", script, StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.Contains("https://myjobtracker.com.au", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureTransport_UsesSessionBackedCleanHandoffInsteadOfPayloadUrl()
    {
        var popupPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "popup.js");
        var handoffPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "handoff.js");
        var popup = File.ReadAllText(popupPath);
        var handoff = File.ReadAllText(handoffPath);

        Assert.Contains("chrome.storage.session.set", popup, StringComparison.Ordinal);
        Assert.Contains("handoff.html?id=", popup, StringComparison.Ordinal);
        Assert.DoesNotContain("#capture=", popup, StringComparison.Ordinal);
        Assert.DoesNotContain("toBase64Url", popup, StringComparison.Ordinal);
        Assert.Contains("chrome.storage.session.remove", handoff, StringComparison.Ordinal);
        Assert.Contains("/applications/import/extension/handoff", handoff, StringComparison.Ordinal);
        Assert.Contains("form.submit()", handoff, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(", handoff, StringComparison.Ordinal);
    }


    [Fact]
    public async Task IndeedFixture_SelectorsTargetSelectedJobPanel()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Extension",
            "indeed-selected-job.html");
        var document = await new HtmlParser().ParseDocumentAsync(File.ReadAllText(fixturePath));
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "popup.js");
        var script = File.ReadAllText(scriptPath);

        var title = document.QuerySelector("[data-testid='jobsearch-JobInfoHeader-title']")?.TextContent;
        title = Regex.Replace(title ?? string.Empty, @"\s*-\s*job post\s*$", string.Empty).Trim();

        Assert.Equal("IT Support Technician", title);
        Assert.Equal(
            "V4 Services Pty Ltd",
            document.QuerySelector("[data-testid='inlineHeader-companyName']")?.TextContent.Trim());
        Assert.Equal(
            "Australia",
            document.QuerySelector("[data-testid='inlineHeader-companyLocation']")?.TextContent.Trim());
        Assert.Equal(
            "$35 - $40 an hour",
            document.QuerySelector("#jobDetailsSection [aria-label='Pay'] [data-testid='list-item']")?.TextContent.Trim());
        Assert.Equal(
            "Casual",
            document.QuerySelector("#jobDetailsSection [aria-label='Job type'] [data-testid='list-item']")?.TextContent.Trim());
        Assert.Contains(
            "desktop troubleshooting",
            document.QuerySelector("#jobDescriptionText")?.TextContent,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("Welcome, Chit", title);
        Assert.NotEqual("jobs in Perth WA", title);

        Assert.Contains("jobsearch-JobInfoHeader-title", script, StringComparison.Ordinal);
        Assert.Contains("inlineHeader-companyName", script, StringComparison.Ordinal);
        Assert.Contains("inlineHeader-companyLocation", script, StringComparison.Ordinal);
        Assert.Contains("#salaryInfoAndJobType", script, StringComparison.Ordinal);
        Assert.Contains("#jobDescriptionText", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinkedInFixture_SelectorsTargetFocusedJobDetailsInsteadOfResultCard()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Extension",
            "linkedin-focused-job.html");
        var document = await new HtmlParser().ParseDocumentAsync(File.ReadAllText(fixturePath));
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "popup.js");
        var script = File.ReadAllText(scriptPath);

        var focusedPanel = document.QuerySelector(".jobs-search__job-details--container");
        Assert.NotNull(focusedPanel);

        Assert.Equal(
            "IT Service Desk Analyst",
            focusedPanel.QuerySelector(".job-details-jobs-unified-top-card__job-title h1")?.TextContent.Trim());
        Assert.Equal(
            "Synthetic Legal",
            focusedPanel.QuerySelector(".job-details-jobs-unified-top-card__company-name a")?.TextContent.Trim());
        Assert.StartsWith(
            "Perth, Western Australia, Australia",
            focusedPanel.QuerySelector(".job-details-jobs-unified-top-card__primary-description-container")?.TextContent.Trim(),
            StringComparison.Ordinal);
        Assert.Equal(
            "Full-time",
            focusedPanel.QuerySelector(".job-details-jobs-unified-top-card__job-insight")?.TextContent.Trim());
        Assert.Contains(
            "Troubleshoot user issues",
            focusedPanel.QuerySelector("#job-details")?.TextContent,
            StringComparison.Ordinal);
        Assert.NotEqual(
            document.QuerySelector(".job-card-list__title")?.TextContent.Trim(),
            focusedPanel.QuerySelector(".job-details-jobs-unified-top-card__job-title h1")?.TextContent.Trim());

        Assert.Contains("jobs-search__job-details--container", script, StringComparison.Ordinal);
        Assert.Contains("job-details-jobs-unified-top-card__job-title", script, StringComparison.Ordinal);
        Assert.Contains("job-details-jobs-unified-top-card__company-name", script, StringComparison.Ordinal);
        Assert.Contains("job-details-jobs-unified-top-card__primary-description-container", script, StringComparison.Ordinal);
        Assert.Contains("jobs-description-content__text", script, StringComparison.Ordinal);
        Assert.Contains("currentJobId", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProspleFixture_SelectorsTargetSelectedOpportunityInsteadOfSearchHeading()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Extension",
            "prosple-selected-job.html");
        var document = await new HtmlParser().ParseDocumentAsync(File.ReadAllText(fixturePath));
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "popup.js");
        var script = File.ReadAllText(scriptPath);

        var opportunityHeading = document.QuerySelectorAll("h2")
            .Single(element => element.TextContent.Trim() == "Opportunity details");
        var selectedOpportunity = opportunityHeading.ParentElement;
        while (selectedOpportunity is not null
               && (selectedOpportunity.QuerySelector("header h2") is null
                   || selectedOpportunity.QuerySelector("[data-testid='raw-html']") is null))
        {
            selectedOpportunity = selectedOpportunity.ParentElement;
        }

        Assert.NotNull(selectedOpportunity);
        Assert.Equal(
            "Graduate Program, March 2027: General Application (Mar 2027)",
            selectedOpportunity.QuerySelector("header h2 a")?.TextContent.Trim());
        Assert.Equal(
            "Capgemini Australia and New Zealand",
            selectedOpportunity.QuerySelectorAll("header a[href*='/graduate-employers/']")
                .Single(element => element.Closest("h1, h2, h3") is null)
                .TextContent.Trim());
        Assert.Equal(
            "Auckland, Wellington, Canberra, Sydney, Brisbane, Adelaide, Melbourne, Perth",
            selectedOpportunity.QuerySelector("header span + p")?.TextContent.Trim());
        Assert.Contains(
            "Build technology solutions",
            selectedOpportunity.QuerySelector("[data-testid='raw-html']")?.TextContent,
            StringComparison.Ordinal);
        Assert.NotEqual(
            document.QuerySelector("main > h1")?.TextContent.Trim(),
            selectedOpportunity.QuerySelector("header h2 a")?.TextContent.Trim());

        Assert.Contains("endsWith(\".prosple.com\")", script, StringComparison.Ordinal);
        Assert.Contains("Opportunity details", script, StringComparison.Ordinal);
        Assert.Contains("/graduate-employers/", script, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"raw-html\"", script, StringComparison.Ordinal);
        Assert.Contains("data-event-track=\"cta-apply\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GreenhouseFixture_SelectorsCaptureCompanyLocationAndDescription()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Extension",
            "greenhouse-job.html");
        var document = await new HtmlParser().ParseDocumentAsync(File.ReadAllText(fixturePath));
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "BrowserExtension", "popup.js");
        var script = File.ReadAllText(scriptPath);

        Assert.Equal(
            "Junior Software Engineer",
            document.QuerySelector(".job__title h1")?.TextContent.Trim());
        Assert.Equal(
            "US - Remote",
            document.QuerySelector(".job__location")?.TextContent.Trim());
        Assert.Equal(
            "Waymark Logo",
            document.QuerySelector(".job-post-container .logo img")?.GetAttribute("alt"));
        Assert.Contains(
            "technology-enabled healthcare",
            document.QuerySelector(".job__description")?.TextContent,
            StringComparison.Ordinal);

        Assert.Contains("job-boards.greenhouse.io", script, StringComparison.Ordinal);
        Assert.Contains(".job__title h1", script, StringComparison.Ordinal);
        Assert.Contains(".job__location", script, StringComparison.Ordinal);
        Assert.Contains(".job-post-container .logo img", script, StringComparison.Ordinal);
        Assert.Contains(".job__description", script, StringComparison.Ordinal);
    }
}
