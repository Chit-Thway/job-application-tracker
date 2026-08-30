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
        Assert.Equal("1.0.3", root.GetProperty("version").GetString());
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
}
