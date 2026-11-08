using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    [Fact]
    public async Task AutofillProfile_ConnectsWithProofAndRevokesOnLogout()
    {
        const string route = "/extension/profile";
        var owner = await CreateAuthenticatedClientAsync();
        var other = await CreateAuthenticatedClientAsync();
        var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        var page = await owner.GetStringAsync(route);
        var csrf = ExtractAntiforgeryToken(page);
        var save = await owner.PostAsync(route, Form(
            ("__RequestVerificationToken", csrf),
            ("GivenName", "Maya"), ("FamilyName", "Chen"), ("Postcode", "0800"),
            ("NoticePeriod", "Two weeks"), ("Portfolio", "https://example.test/portfolio"),
            ("Experience[0].Employer", "Wattle Systems"), ("Experience[0].Title", "Support Technician"),
            ("Experience[0].StartMonth", "2024-02"), ("Experience[0].EndMonth", "2025-01"),
            ("Experience[0].Current", "true"),
            ("Education[0].Institution", "Example College"), ("Education[0].Qualification", "Diploma"),
            ("Certifications[0].Name", "Example Certificate")));
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var savedPage = await owner.GetStringAsync(route);
        Assert.Contains("name=\"Experience[0].Employer\"", savedPage);
        Assert.Contains("Wattle Systems", savedPage);
        Assert.DoesNotContain("Wattle Systems", await other.GetStringAsync(route));
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync($"{route}/data")).StatusCode);

        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var approve = await owner.PostAsync($"{route}/connect",
            Form(("__RequestVerificationToken", csrf), ("challenge", challenge)));
        approve.EnsureSuccessStatusCode();
        var document = await new HtmlParser().ParseDocumentAsync(await approve.Content.ReadAsStringAsync());
        var code = document.QuerySelector("#connection-code")?.GetAttribute("value");
        Assert.NotNull(code);
        var invalid = await anonymous.PostAsJsonAsync($"{route}/exchange", new { code, verifier = new string('A', 43) });
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        var exchange = await anonymous.PostAsJsonAsync($"{route}/exchange", new { code, verifier });
        exchange.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await exchange.Content.ReadAsStringAsync());
        var token = tokenJson.RootElement.GetProperty("token").GetString();
        var replay = await anonymous.PostAsJsonAsync($"{route}/exchange", new { code, verifier });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var data = await anonymous.GetAsync($"{route}/data");
        Assert.True(data.Headers.CacheControl?.NoStore);
        using var profile = JsonDocument.Parse(await data.Content.ReadAsStringAsync());
        Assert.Equal("0800", profile.RootElement.GetProperty("postcode").GetString());
        Assert.Equal("Two weeks", profile.RootElement.GetProperty("noticePeriod").GetString());
        Assert.Equal("Wattle Systems", profile.RootElement.GetProperty("experience")[0].GetProperty("employer").GetString());
        Assert.Equal(JsonValueKind.Null, profile.RootElement.GetProperty("experience")[0].GetProperty("endMonth").ValueKind);
        Assert.Equal("Diploma", profile.RootElement.GetProperty("education")[0].GetProperty("qualification").GetString());
        Assert.Equal("Example Certificate", profile.RootElement.GetProperty("certifications")[0].GetProperty("name").GetString());

        var logout = await owner.PostAsync("/account/logout", Form(("__RequestVerificationToken", csrf)));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{route}/data")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await database.AutofillProfiles.SingleAsync();
        Assert.DoesNotContain("Wattle Systems", stored.ProtectedProfile);
        Assert.Null(stored.AccessHash);
    }
}
