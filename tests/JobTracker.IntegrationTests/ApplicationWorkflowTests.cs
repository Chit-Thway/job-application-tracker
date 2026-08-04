using System.Net;
using System.Text.RegularExpressions;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
    : IClassFixture<JobTrackerWebApplicationFactory>
{
    private const string TestPassword = "A-Strong-Test-Password-482!";
    private readonly JobTrackerWebApplicationFactory factory;

    public ApplicationWorkflowTests(JobTrackerWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AuthenticatedUser_CanCompleteManualApplicationJourney()
    {
        var client = await CreateAuthenticatedClientAsync();

        var companyPage = await client.GetAsync("/companies/new");
        var companyToken = ExtractAntiforgeryToken(await companyPage.Content.ReadAsStringAsync());
        var companyResponse = await client.PostAsync(
            "/companies/new",
            Form(
                ("Name", "Synthetic Meridian Works"),
                ("Location", "Perth, WA"),
                ("Website", "https://example.test/meridian"),
                ("Notes", "Synthetic company notes"),
                ("__RequestVerificationToken", companyToken)));
        Assert.Equal(HttpStatusCode.Redirect, companyResponse.StatusCode);
        var companyLocation = companyResponse.Headers.Location?.OriginalString;
        Assert.NotNull(companyLocation);
        var companyId = IdFromLocation(companyLocation);

        var createPage = await client.GetAsync("/applications/new");
        var createContent = await createPage.Content.ReadAsStringAsync();
        Assert.Contains("Add an application.", createContent, StringComparison.Ordinal);
        Assert.Contains("Synthetic Meridian Works", createContent, StringComparison.Ordinal);
        var createToken = ExtractAntiforgeryToken(createContent);
        var createResponse = await client.PostAsync(
            "/applications/new",
            Form(
                ("RoleTitle", "Synthetic Platform Engineer"),
                ("CompanyId", companyId.ToString()),
                ("AppliedOn", "2026-08-04"),
                ("SourceUrl", "https://example.test/jobs/platform"),
                ("Notes", "Follow up next week"),
                ("IsSavedForever", "false"),
                ("__RequestVerificationToken", createToken)));
        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var applicationLocation = createResponse.Headers.Location?.OriginalString;
        Assert.NotNull(applicationLocation);
        var applicationId = IdFromLocation(applicationLocation);

        var details = await client.GetAsync(applicationLocation);
        var detailsContent = await details.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Contains("Synthetic Platform Engineer", detailsContent, StringComparison.Ordinal);
        Assert.Contains("Synthetic Meridian Works", detailsContent, StringComparison.Ordinal);
        Assert.Contains("This application is not saved.", detailsContent, StringComparison.Ordinal);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var history = await database.StatusHistory.SingleAsync(item =>
                item.JobApplicationId == applicationId);
            Assert.Equal(PipelineStage.Applied, history.NewStage);
            Assert.Equal(ApplicationOutcome.Active, history.NewOutcome);
        }

        var saveToken = ExtractAntiforgeryToken(detailsContent);
        var saveResponse = await client.PostAsync(
            $"/applications/{applicationId}/saved",
            Form(
                ("isSavedForever", "true"),
                ("__RequestVerificationToken", saveToken)));
        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);

        var filtered = await client.GetAsync(
            "/applications?Query=Meridian&IsSavedForever=true&AppliedFrom=2026-08-01&AppliedTo=2026-08-31");
        var filteredContent = await filtered.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Contains("Synthetic Platform Engineer", filteredContent, StringComparison.Ordinal);
        Assert.Contains("Saved", filteredContent, StringComparison.Ordinal);

        var editPage = await client.GetAsync($"/applications/{applicationId}/edit");
        var editToken = ExtractAntiforgeryToken(await editPage.Content.ReadAsStringAsync());
        var editResponse = await client.PostAsync(
            $"/applications/{applicationId}/edit",
            Form(
                ("RoleTitle", "Synthetic Senior Platform Engineer"),
                ("CompanyId", companyId.ToString()),
                ("AppliedOn", "2026-08-03"),
                ("SourceUrl", "https://example.test/jobs/platform-senior"),
                ("Notes", "Updated safe notes"),
                ("IsSavedForever", "true"),
                ("__RequestVerificationToken", editToken)));
        Assert.Equal(HttpStatusCode.Redirect, editResponse.StatusCode);

        var updated = await client.GetAsync(applicationLocation);
        var updatedContent = await updated.Content.ReadAsStringAsync();
        Assert.Contains("Synthetic Senior Platform Engineer", updatedContent, StringComparison.Ordinal);
        Assert.Contains("Updated safe notes", updatedContent, StringComparison.Ordinal);

        var companyDeletePage = await client.GetAsync($"/companies/{companyId}/delete");
        var companyDeleteToken = ExtractAntiforgeryToken(
            await companyDeletePage.Content.ReadAsStringAsync());
        var companyDeleteResponse = await client.PostAsync(
            $"/companies/{companyId}/delete",
            Form(("__RequestVerificationToken", companyDeleteToken)));
        Assert.Equal(HttpStatusCode.Conflict, companyDeleteResponse.StatusCode);

        var deletePage = await client.GetAsync($"/applications/{applicationId}/delete");
        var deleteToken = ExtractAntiforgeryToken(await deletePage.Content.ReadAsStringAsync());
        var deleteResponse = await client.PostAsync(
            $"/applications/{applicationId}/delete",
            Form(("__RequestVerificationToken", deleteToken)));
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);
        Assert.Equal("/applications", deleteResponse.Headers.Location?.OriginalString);

        var deleted = await client.GetAsync(applicationLocation);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Fact]
    public async Task DuplicateCompanyAndInvalidApplication_ShowClearValidationWithoutSaving()
    {
        var client = await CreateAuthenticatedClientAsync();
        await PostCompanyAsync(client, "Synthetic Duplicate", "Perth, WA");

        var duplicatePage = await client.GetAsync("/companies/new");
        var duplicateToken = ExtractAntiforgeryToken(
            await duplicatePage.Content.ReadAsStringAsync());
        var duplicateResponse = await client.PostAsync(
            "/companies/new",
            Form(
                ("Name", " synthetic duplicate "),
                ("Location", "perth, wa"),
                ("__RequestVerificationToken", duplicateToken)));
        var duplicateContent = await duplicateResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);
        Assert.Contains("already have a company", duplicateContent, StringComparison.Ordinal);

        var applicationPage = await client.GetAsync("/applications/new");
        var applicationToken = ExtractAntiforgeryToken(
            await applicationPage.Content.ReadAsStringAsync());
        var invalidResponse = await client.PostAsync(
            "/applications/new",
            Form(
                ("RoleTitle", ""),
                ("AppliedOn", "2026-08-04"),
                ("SourceUrl", "not a URL"),
                ("Notes", "Preserve this safe note"),
                ("__RequestVerificationToken", applicationToken)));
        var invalidContent = await invalidResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);
        Assert.Contains("The Role title field is required", invalidContent, StringComparison.Ordinal);
        Assert.Contains("Preserve this safe note", invalidContent, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(database.Companies.Where(company => company.Name == "Synthetic Duplicate"));
        Assert.Empty(database.JobApplications);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var email = $"workflow-{Guid.NewGuid():N}@example.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Synthetic Workflow User",
                TimeZoneId = "Australia/Perth",
            };
            var result = await manager.CreateAsync(user, TestPassword);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Code)));
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var loginPage = await client.GetAsync("/account/login");
        var loginToken = ExtractAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        var loginResponse = await client.PostAsync(
            "/account/login",
            Form(
                ("Email", email),
                ("Password", TestPassword),
                ("RememberMe", "false"),
                ("__RequestVerificationToken", loginToken)));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        return client;
    }

    private static async Task PostCompanyAsync(
        HttpClient client,
        string name,
        string location)
    {
        var page = await client.GetAsync("/companies/new");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        var response = await client.PostAsync(
            "/companies/new",
            Form(
                ("Name", name),
                ("Location", location),
                ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static Guid IdFromLocation(string location)
    {
        var segment = location.TrimEnd('/').Split('/').Last();
        Assert.True(Guid.TryParse(segment, out var id), $"No GUID was found in '{location}'.");
        return id;
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "The page did not contain an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryTokenRegex();
}
