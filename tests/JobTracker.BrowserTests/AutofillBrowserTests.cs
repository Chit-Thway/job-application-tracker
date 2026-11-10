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
    public async Task LoadedExtension_FillConnectsAndFillsDistinctEmployerLayouts()
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
                ContentType = "text/html; charset=utf-8",
                Body = new Uri(route.Request.Url).AbsolutePath switch
                {
                    "/apply/history" => HistoryForm,
                    "/apply/screening" => ScreeningForm,
                    _ => ContactForm,
                },
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
            await Expect(popup.Locator("#connection-summary")).ToHaveTextAsync("Autofill profile");
            await Expect(popup.GetByRole(AriaRole.Button, new() { Name = "Connect to tracker", IncludeHidden = true })).ToHaveCountAsync(0);

            var employer = await context.NewPageAsync();
            await employer.GotoAsync($"{EmployerOrigin}/apply/contact");
            await TargetEmployerTabAsync(popup);
            await popup.Locator("#fill").ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("Filled 7 fields.");
            await Expect(popup.Locator("#connection-summary")).ToHaveTextAsync("Autofill profile connected");
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
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("Filled 7 fields.");
            await Expect(employer.Locator("#first-company")).ToHaveValueAsync("Wattle Systems");
            await Expect(employer.Locator("#first-start")).ToHaveValueAsync("02/2024");
            await Expect(employer.Locator("#second-company")).ToBeEmptyAsync();
            await Expect(employer.GetByLabel("School")).ToHaveValueAsync("Example College");
            await Expect(employer.GetByLabel("Certification name")).ToHaveValueAsync("Example Certificate");
            await Expect(employer.GetByLabel("Licence class")).ToHaveValueAsync("C");
            await employer.GetByText("Open employment 2", new() { Exact = true }).ClickAsync();
            await popup.Locator("#fill").ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("Filled 3 fields.");
            await Expect(employer.Locator("#second-company")).ToHaveValueAsync("Paperbark IT");
            await Expect(employer.Locator("#second-end")).ToHaveValueAsync("2024-01");
            await popup.Locator("#fill").ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("No blank supported fields");

            await employer.GotoAsync($"{EmployerOrigin}/apply/screening");
            await popup.Locator("#fill").ClickAsync();
            await Expect(popup.Locator("#fill-status")).ToContainTextAsync("Filled 2 fields.");
            await Expect(employer.Locator("#work-rights")).ToHaveValueAsync("y");
            await Expect(employer.Locator("#sponsorship-no")).ToBeCheckedAsync();
            await Expect(employer.Locator("#unrestricted")).ToHaveValueAsync("");
            await Expect(employer.Locator("#already-answered")).ToHaveValueAsync("no");
            await Expect(popup.Locator("#fill-review")).ToContainTextAsync("unrestricted work rights");
            await Expect(popup.Locator("#fill-review")).ToContainTextAsync("working in a hybrid setup");
            await Expect(employer.Locator("#project")).ToBeEmptyAsync();
            await Expect(employer.Locator("#salary")).ToBeEmptyAsync();
            await Expect(employer.Locator("#relocation")).ToHaveValueAsync("");
            await Expect(employer.Locator("#referee-email")).ToBeEmptyAsync();
            await Expect(employer.Locator("#consent")).Not.ToBeCheckedAsync();

            await popup.Locator("#application-documents summary").ClickAsync();
            await popup.Locator("#document-file").SetInputFilesAsync(new FilePayload
            {
                Name = "example.pdf", MimeType = "application/pdf", Buffer = "%PDF-1.7\nExample"u8.ToArray(),
            });
            await Expect(popup.Locator("#selected-document")).ToContainTextAsync("example.pdf");
            await Expect(employer.Locator("#resume")).ToBeEmptyAsync();
            await popup.Locator("#attach-document").ClickAsync();
            await Expect(popup.Locator("#document-status")).ToContainTextAsync("Added example.pdf");
            Assert.Equal("example.pdf", await employer.Locator("#resume").EvaluateAsync<string>("field => field.files[0]?.name"));
            await Expect(employer.Locator("#cover-letter")).ToBeEmptyAsync();

            await popup.GetByRole(AriaRole.Textbox, new() { Name = "Describe a project you led?", Exact = true })
                .FillAsync("I led an updated project example.");
            await popup.GetByRole(AriaRole.Button, new() { Name = "Use answer for Describe a project you led?" }).ClickAsync();
            await Expect(employer.Locator("#project")).ToHaveValueAsync("I led an updated project example.");
            await popup.GetByRole(AriaRole.Button, new() { Name = "Use answer for What are your minimum salary expectations for this role?" }).ClickAsync();
            await Expect(employer.Locator("#salary")).ToHaveValueAsync("AUD 90,000 base per year");
            await popup.GetByRole(AriaRole.Button, new() { Name = "Use answer for Are you open to relocating for this position?" }).ClickAsync();
            await Expect(employer.Locator("#relocation")).ToHaveValueAsync("no");
            await popup.Locator("#referee-picker summary").ClickAsync();
            await Expect(popup.Locator("#referee-select")).ToContainTextAsync("Alex Sample");
            await Expect(popup.GetByRole(AriaRole.Button, new() { Name = "Copy email" })).ToBeVisibleAsync();

            await tracker.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
            await employer.GotoAsync($"{EmployerOrigin}/apply/contact");
            var login = await context.RunAndWaitForPageAsync(() => popup.Locator("#fill").ClickAsync(),
                new() { Timeout = 10_000 });
            await login.WaitForURLAsync("**/account/login?ReturnUrl=*");
            await popup.CloseAsync();
            await login.GetByLabel("Email").FillAsync(BrowserJourneyFixture.Email);
            await login.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserJourneyFixture.Password);
            var loginClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            login.Close += (_, _) => loginClosed.TrySetResult();
            // Chrome closes the sign-in window during the callback navigation.
            await login.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true })
                .EvaluateAsync("button => { setTimeout(() => button.click(), 0); }");
            await loginClosed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var reopened = await context.NewPageAsync();
            await reopened.GotoAsync($"chrome-extension://{extensionId}/popup.html");
            await Expect(reopened.Locator("#connection-summary")).ToHaveTextAsync("Autofill profile connected");
            await Expect(employer.GetByLabel("Full name")).ToBeEmptyAsync();
            await TargetEmployerTabAsync(reopened);
            await reopened.Locator("#fill").ClickAsync();
            await Expect(reopened.Locator("#fill-status")).ToContainTextAsync("Filled 7 fields.");
            await Expect(employer.GetByLabel("Full name")).ToHaveValueAsync("Maya Chen");
            await reopened.Locator("#application-documents summary").ClickAsync();
            await Expect(reopened.Locator("#selected-document")).ToContainTextAsync("example.pdf");
            await reopened.Locator("#forget-document").ClickAsync();
            await Expect(reopened.Locator("#selected-document")).ToContainTextAsync("No file saved");
        }
        finally
        {
            extension.Delete(recursive: true);
        }
    }

    private static Task TargetEmployerTabAsync(IPage popup) => popup.EvaluateAsync("""
        async () => {
          const tab = (await chrome.tabs.query({ url: "https://careers.example.test/*" }))[0];
          chrome.tabs.query = async () => [tab];
        }
        """);

    private async Task CreateProfileAsync(IPage page)
    {
        await page.GotoAsync(new Uri(fixture.BaseAddress, "/account/login").AbsoluteUri);
        await page.GetByLabel("Email").FillAsync(BrowserJourneyFixture.Email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserJourneyFixture.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/dashboard");
        await page.GotoAsync(new Uri(fixture.BaseAddress, "/extension/profile").AbsoluteUri);
        await page.GetByRole(AriaRole.Button, new() { Name = "Use essential cookies" }).ClickAsync();
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
            "Wattle Systems - Support Technician Feb 2024-Present",
            "Resolve support tickets.",
            "Paperbark IT - Service Desk Assistant Jul 2022-Jan 2024",
            "Education",
            "Example College - Diploma",
            "Certifications",
            "Example Certificate",
        ];
        for (var index = 0; index < lines.Length; index++)
        {
            resumePage.AddText(lines[index], 12, new PdfPoint(50, 780 - index * 22), font);
        }
        var upload = page.GetByLabel("Résumé file");
        await upload.SetInputFilesAsync(new FilePayload
        {
            Name = "invalid.txt",
            MimeType = "text/plain",
            Buffer = "Synthetic invalid file"u8.ToArray(),
        });
        await Expect(upload).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#resume-upload-error")).ToBeVisibleAsync();
        await upload.SetInputFilesAsync(new FilePayload
        {
            Name = "synthetic-resume.pdf",
            MimeType = "application/pdf",
            Buffer = resume.Build(),
        });
        await Expect(page.Locator("#resume-upload-error")).ToBeHiddenAsync();
        await Expect(page.GetByText("File selected. Ready to extract.")).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
              const form = document.querySelector("[data-resume-upload]");
              const submit = form.querySelector("button[type='submit']");
              form.addEventListener("submit", event => event.preventDefault(), { once: true });
              form.requestSubmit(submit);
              const pending = submit.disabled && submit.getAttribute("aria-busy") === "true"
                && submit.textContent === "Extracting details…";
              window.dispatchEvent(new PageTransitionEvent("pageshow", { persisted: true }));
              return pending && !submit.disabled && !submit.hasAttribute("aria-busy");
            }
            """));
        await page.GetByRole(AriaRole.Button, new() { Name = "Extract résumé details" }).ClickAsync();
        var review = page.GetByRole(AriaRole.Heading, new() { Name = "Review résumé suggestions" });
        await Expect(review).ToBeFocusedAsync();
        await Expect(upload).ToBeHiddenAsync();
        await Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Profile sections" })
            .GetByRole(AriaRole.Link, new() { Name = "Review suggestions" })).ToHaveAttributeAsync("aria-current", "location");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        Assert.Equal("none", await page.Locator("#resume-review")
            .EvaluateAsync<string>("panel => getComputedStyle(panel).animationName"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });

        await page.SetViewportSizeAsync(390, 844);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        await page.SetViewportSizeAsync(1280, 900);
        await page.GetByRole(AriaRole.Navigation, new() { Name = "Profile sections" })
            .GetByRole(AriaRole.Link, new() { Name = "Import résumé", Exact = true }).ClickAsync();
        await Expect(upload).ToBeVisibleAsync();

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
        var experience = page.Locator("#work-experience");
        var addRole = experience.GetByRole(AriaRole.Button, new() { Name = "Add role" });
        await addRole.ClickAsync();
        await Expect(Field("Experience[2].Title")).ToBeFocusedAsync();
        await Expect(experience.GetByRole(AriaRole.Status).Filter(new() { HasText = "Entry added to the draft." })).ToBeVisibleAsync();
        await experience.Locator("[data-entry]").Last.GetByRole(AriaRole.Button, new() { Name = "Remove role" }).ClickAsync();
        await Expect(addRole).ToBeFocusedAsync();
        await Expect(Field("Experience[2].Title")).ToHaveCountAsync(0);
        await Field("Phone").FillAsync("+61 491 570 007");
        await Expect(Badge("Phone")).ToHaveCountAsync(0);
        await Field("Availability").FillAsync("Immediately");
        await Field("Certifications[0].LicenceClass").FillAsync("C");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add country" }).ClickAsync();
        await Field("WorkPermissions[0].CountryCode").SelectOptionAsync("AU");
        await Field("WorkPermissions[0].Authorized").SelectOptionAsync("true");
        await Field("WorkPermissions[0].SponsorshipNow").SelectOptionAsync("false");
        await Field("WorkPermissions[0].SponsorshipLater").SelectOptionAsync("false");
        await Field("SalaryExpectation").FillAsync("AUD 90,000 base per year");
        await Field("OpenToRelocation").SelectOptionAsync("false");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add answer" }).ClickAsync();
        await Field("SavedAnswers[0].Question").FillAsync("Describe a project you led?");
        await Field("SavedAnswers[0].Answer").FillAsync("I led a synthetic example project.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Add referee" }).ClickAsync();
        await Field("Referees[0].Name").FillAsync("Alex Sample");
        await Field("Referees[0].Email").FillAsync("alex@example.test");
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

    private const string ScreeningForm = """
        <h1>Application questions</h1>
        <form>
          <label>Do you currently have the right to work in Australia?
            <select id="work-rights"><option value=""></option><option value="y">Yes</option><option value="n">No</option></select></label>
          <fieldset><legend>Do you now, or will you in the future, require sponsorship for employment visa status to work in Australia?</legend>
            <label><input type="radio" name="sponsorship" value="y">Yes</label>
            <label><input type="radio" name="sponsorship" value="n" id="sponsorship-no">No</label>
          </fieldset>
          <label>Do you have unrestricted work rights in Australia?
            <select id="unrestricted"><option value=""></option><option value="yes">Yes</option><option value="no">No</option></select></label>
          <label>Are you legally authorised to work in Australia?
            <select id="already-answered"><option value=""></option><option value="yes">Yes</option><option value="no" selected>No</option></select></label>
          <label>Are you open to relocating for this position?
            <select id="relocation"><option value=""></option><option value="yes">Yes</option><option value="no">No</option></select></label>
          <label>Describe a project you led?<textarea id="project"></textarea></label>
          <label>What are your minimum salary expectations for this role?<input id="salary"></label>
          <label id="hybrid-question">Are you open to working in a hybrid setup?</label>
          <button type="button" role="combobox" aria-labelledby="hybrid-question" aria-expanded="false">Select One</button>
          <label>Résumé<input id="resume" type="file" accept=".pdf" style="display:none"></label>
          <label>Cover letter<input id="cover-letter" type="file"></label>
          <fieldset><legend>Referees</legend><label>Email<input id="referee-email" type="email"></label></fieldset>
          <label>I consent to follow-up emails<input id="consent" type="checkbox"></label>
        </form>
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
          <fieldset><legend>Certification 1</legend><label>Certification name<input></label><label>Licence class<input></label></fieldset>
        </div>
        """;
}
