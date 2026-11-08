using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Microsoft.Playwright;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using static Microsoft.Playwright.Assertions;

namespace JobTracker.BrowserTests;

[Collection(BrowserJourneyCollection.Name)]
public sealed class AutofillBrowserTests(BrowserJourneyFixture fixture)
{
    private const string EmployerOrigin = "https://careers.example.test";

    [Fact]
    public async Task LoadedExtension_PairsAndFillsDistinctEmployerLayouts()
    {
        var extension = Directory.CreateTempSubdirectory("jobtracker-autofill-");
        try
        {
            var extensionId = PrepareExtension(extension.FullName);
            await using var context = await fixture.Playwright.Chromium.LaunchPersistentContextAsync(
                "", new BrowserTypeLaunchPersistentContextOptions
                {
                    Channel = "chromium",
                    Headless = true,
                    Args = [$"--disable-extensions-except={extension.FullName}", $"--load-extension={extension.FullName}"],
                });
            await context.RouteAsync($"{EmployerOrigin}/apply/**", route => route.FulfillAsync(new()
            {
                ContentType = "text/html",
                Body = route.Request.Url.EndsWith("/history", StringComparison.Ordinal)
                    ? HistoryForm : ContactForm,
            }));
            var tracker = await context.NewPageAsync();
            await CreateProfileAsync(tracker);
            await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new()
            {
                Origin = fixture.BaseAddress.GetLeftPart(UriPartial.Authority),
            });
            await tracker.GetByRole(AriaRole.Button, new() { Name = "Copy role" }).First.ClickAsync();
            await Expect(tracker.GetByText("Role copied.", new() { Exact = true })).ToBeVisibleAsync();
            Assert.Contains("Support Technician at Wattle Systems",
                await tracker.EvaluateAsync<string>("navigator.clipboard.readText()"));

            var popup = await context.NewPageAsync();
            await popup.GotoAsync($"chrome-extension://{extensionId}/popup.html");
            await popup.Locator("#connection summary").ClickAsync();
            var pairingPage = await context.RunAndWaitForPageAsync(async () =>
                await popup.GetByRole(AriaRole.Button, new() { Name = "Get connection code" }).ClickAsync());
            await pairingPage.GetByRole(AriaRole.Button, new() { Name = "Create connection code" }).ClickAsync();
            var pairingCode = await pairingPage.GetByLabel("Connection code").InputValueAsync();
            await popup.ReloadAsync();
            await popup.GetByLabel("One-time code from the tracker").FillAsync(pairingCode);
            await popup.GetByRole(AriaRole.Button, new() { Name = "Connect", Exact = true }).ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("Profile connected.");
            await pairingPage.CloseAsync();

            var employer = await context.NewPageAsync();
            await employer.GotoAsync($"{EmployerOrigin}/apply/contact");
            await popup.EvaluateAsync("""
                async () => {
                  const tab = (await chrome.tabs.query({ url: "https://careers.example.test/*" }))[0];
                  chrome.tabs.query = async () => [tab];
                }
                """);
            await popup.Locator("#fill").ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("Filled 7 fields.");
            await Expect(employer.GetByLabel("Full name")).ToHaveValueAsync("Maya Chen");
            await Expect(employer.GetByLabel("Postcode")).ToHaveValueAsync("0800");
            await Expect(employer.GetByLabel("State or territory")).ToHaveValueAsync("NT");
            await Expect(employer.GetByLabel("Phone")).ToHaveValueAsync("User entered phone");
            await Expect(employer.GetByLabel("Current company")).ToHaveValueAsync("Wattle Systems");
            await Expect(employer.GetByLabel("LinkedIn URL")).ToHaveValueAsync("https://www.linkedin.com/in/maya-example");
            await Expect(employer.GetByLabel("Notice period")).ToHaveValueAsync("Two weeks");
            await Expect(employer.GetByLabel("Availability")).ToBeEmptyAsync();
            await Expect(employer.GetByLabel("Location (City)")).ToBeEmptyAsync();
            await Expect(employer.GetByLabel("Referee first name")).ToBeEmptyAsync();
            await Expect(employer.GetByLabel("I agree to the privacy notice")).Not.ToBeCheckedAsync();
            await employer.GetByLabel("Full name").FillAsync("May Chen");
            await popup.GetByRole(AriaRole.Button, new() { Name = "Undo last fill" }).ClickAsync();
            await Expect(employer.GetByLabel("Full name")).ToHaveValueAsync("May Chen");
            await Expect(employer.GetByLabel("Postcode")).ToBeEmptyAsync();

            await employer.GotoAsync($"{EmployerOrigin}/apply/history");
            await popup.Locator("#fill").ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("Filled 6 fields.");
            await Expect(employer.Locator("#first-company")).ToHaveValueAsync("Wattle Systems");
            await Expect(employer.Locator("#first-start")).ToHaveValueAsync("02/2024");
            await Expect(employer.Locator("#second-company")).ToBeEmptyAsync();
            await Expect(employer.GetByLabel("School")).ToHaveValueAsync("Example College");
            await Expect(employer.GetByLabel("Certification name")).ToHaveValueAsync("Example Certificate");
            await employer.GetByText("Open employment 2", new() { Exact = true }).ClickAsync();
            await popup.Locator("#fill").ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("Filled 3 fields.");
            await Expect(employer.Locator("#second-company")).ToHaveValueAsync("Paperbark IT");
            await Expect(employer.Locator("#second-end")).ToHaveValueAsync("2024-01");
            await popup.Locator("#fill").ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("No blank supported fields");
        }
        finally
        {
            extension.Delete(recursive: true);
        }
    }

    private async Task CreateProfileAsync(IPage page)
    {
        await page.GotoAsync(new Uri(fixture.BaseAddress, "/account/login").AbsoluteUri);
        await page.GetByLabel("Email").FillAsync(BrowserJourneyFixture.Email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserJourneyFixture.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/dashboard");
        await page.GotoAsync(new Uri(fixture.BaseAddress, "/extension/profile").AbsoluteUri);
        var resume = new PdfDocumentBuilder();
        var font = resume.AddStandard14Font(Standard14Font.Helvetica);
        var resumePage = resume.AddPage(PageSize.A4);
        string[] lines =
        [
            "Maya Chen",
            "maya@example.test",
            "Phone: +61 491 570 006",
            "Address: 10 Sample Street, Darwin NT 0800, Australia",
            "LinkedIn: https://www.linkedin.com/in/maya-example",
            "Notice period: Two weeks",
            "Professional Experience",
            "Support Technician | Wattle Systems | Darwin, NT",
            "Feb 2024 - Present",
            "Resolve support tickets.",
            "Service Desk Assistant",
            "Paperbark IT",
            "Jul 2022 - Jan 2024",
            "Education",
            "Diploma | Example College",
            "Certifications",
            "Example Certificate",
        ];
        for (var index = 0; index < lines.Length; index++)
        {
            resumePage.AddText(lines[index], 12, new PdfPoint(50, 780 - index * 22), font);
        }
        await page.GetByLabel("Résumé file").SetInputFilesAsync(new FilePayload
        {
            Name = "synthetic-resume.pdf",
            MimeType = "application/pdf",
            Buffer = resume.Build(),
        });
        await page.GetByRole(AriaRole.Button, new() { Name = "Extract résumé details" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Review résumé suggestions" })).ToBeVisibleAsync();

        await CheckImportedAsync("GivenName", "Maya");
        await CheckImportedAsync("FamilyName", "Chen");
        await CheckImportedAsync("Email", "maya@example.test");
        await CheckImportedAsync("Phone", "+61 491 570 006");
        await CheckImportedAsync("Postcode", "0800");
        await CheckImportedAsync("Experience[0].Employer", "Wattle Systems");
        await CheckImportedAsync("Experience[0].Title", "Support Technician");
        await CheckImportedAsync("Experience[0].Responsibilities", "Resolve support tickets.");
        await CheckImportedAsync("Experience[1].Employer", "Paperbark IT");
        await CheckImportedAsync("Experience[1].EndMonth", "2024-01");
        await CheckImportedAsync("Education[0].Institution", "Example College");
        await CheckImportedAsync("Certifications[0].Name", "Example Certificate");
        await Expect(Badge("Email")).ToHaveCSSAsync("background-color", "rgb(255, 242, 188)");
        await Expect(Field("Experience[0].Current")).ToBeCheckedAsync();
        await Expect(Badge("Experience[0].Current")).ToBeVisibleAsync();
        await Expect(Badge("Experience[0].EndMonth")).ToHaveCountAsync(0);
        await Expect(Badge("Availability")).ToHaveCountAsync(0);
        await Field("Phone").FillAsync("+61 491 570 007");
        await Expect(Badge("Phone")).ToHaveCountAsync(0);
        await Field("Availability").FillAsync("Immediately");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save profile" }).ClickAsync();
        await Expect(page.GetByText("Autofill profile saved.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator(".resume-field-badge")).ToHaveCountAsync(0);

        ILocator Field(string name) => page.Locator($"[name='{name}']");
        ILocator Badge(string name) => Field(name).Locator("xpath=..").Locator(".resume-field-badge");

        async Task CheckImportedAsync(string name, string value)
        {
            await Expect(Field(name)).ToHaveValueAsync(value);
            await Expect(Badge(name)).ToBeVisibleAsync();
        }
    }

    private string PrepareExtension(string destination)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "BrowserExtension");
        foreach (var file in Directory.EnumerateFiles(source).Concat(Directory.EnumerateFiles(Path.Combine(source, "icons"))))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        var path = Path.Combine(destination, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        manifest["host_permissions"] = new JsonArray($"{fixture.BaseAddress.Scheme}://{fixture.BaseAddress.Host}/*", $"{EmployerOrigin}/*");
        using var key = RSA.Create(2048);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        manifest["key"] = Convert.ToBase64String(publicKey);
        File.WriteAllText(path, manifest.ToJsonString());
        File.WriteAllText(Path.Combine(destination, "tracker-config.js"),
            $"const trackerOrigin = '{fixture.BaseAddress.GetLeftPart(UriPartial.Authority)}';");
        return new string(Convert.ToHexStringLower(SHA256.HashData(publicKey).AsSpan(0, 16))
            .Select(character => (char)('a' + Convert.ToInt32(character.ToString(), 16))).ToArray());
    }

    private const string ContactForm = """
        <h1>Apply for Support Analyst</h1>
        <form>
          <label>Full name<input name="candidateName"></label>
          <label>Email<input type="email"></label>
          <label>Phone<input value="User entered phone"></label>
          <label>Location (City)<input role="combobox" aria-autocomplete="list"></label>
          <label>Postcode<input></label>
          <label>State or territory<select><option value="">Choose</option><option value="NT">NT</option></select></label>
          <label>Current company<input></label>
          <label>LinkedIn URL<input type="url"></label>
          <label>Notice period<input></label>
          <label>Availability<input id="reset-on-input"></label>
          <fieldset><legend>References</legend><label>Referee first name<input autocomplete="given-name"></label></fieldset>
          <label>I agree to the privacy notice<input type="checkbox"></label>
          <button type="submit">Submit</button>
        </form>
        <script>
          document.querySelector("#reset-on-input").addEventListener("input", event => { event.target.value = ""; });
        </script>
        """;

    private const string HistoryForm = """
        <h1>My Experience</h1>
        <div role="form" aria-label="Application">
          <section data-automation-id="workExperience-0">
            <label>Company<input id="first-company"></label>
            <label>Position<input></label>
            <label>Start date (MM/YYYY)<input id="first-start"></label>
            <label>I currently work here<input type="checkbox"></label>
          </section>
          <details data-automation-id="workExperience-1">
            <summary>Open employment 2</summary>
            <label>Company<input id="second-company"></label>
            <label>Position<input></label>
            <label>End date<input type="month" id="second-end"></label>
          </details>
          <fieldset><legend>Education 1</legend><label>School<input></label></fieldset>
          <fieldset><legend>Certification 1</legend><label>Certification name<input></label></fieldset>
        </div>
        """;
}
