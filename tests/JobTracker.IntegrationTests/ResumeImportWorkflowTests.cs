using System.IO.Compression;
using System.Net;
using AngleSharp.Html.Parser;
using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.IntegrationTests;

public sealed partial class ApplicationWorkflowTests
{
    [Fact]
    public async Task ResumeUpload_RequiresAuthenticationAndKeepsSuggestionsInAnUnsavedDraft()
    {
        const string route = "/extension/profile";
        var signedOut = factory.CreateClient(new() { AllowAutoRedirect = false });
        using var anonymous = UploadResume();
        var denied = await signedOut.PostAsync($"{route}/resume", anonymous);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/account/login", denied.Headers.Location?.PathAndQuery);

        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedProfiles = await database.AutofillProfiles.CountAsync();

        var owner = await CreateAuthenticatedClientAsync();
        var token = ExtractAntiforgeryToken(await owner.GetStringAsync(route));
        using var withoutToken = UploadResume();
        Assert.False((await owner.PostAsync($"{route}/resume", withoutToken)).IsSuccessStatusCode);
        using var upload = UploadResume(token);
        var review = await owner.PostAsync($"{route}/resume", upload);
        review.EnsureSuccessStatusCode();
        Assert.True(review.Headers.CacheControl?.NoStore);
        var html = await new HtmlParser().ParseDocumentAsync(await review.Content.ReadAsStringAsync());
        Assert.Contains("Review résumé suggestions", html.Body?.TextContent);
        Assert.Equal("Maya Chen", html.QuerySelector("#FullName")?.GetAttribute("value"));
        Assert.Equal("https://linkedin.com/in/maya-example", html.QuerySelector("#LinkedIn")?.GetAttribute("value"));
        Assert.Equal("Wattle Systems", html.QuerySelector("input[name='Experience[0].Employer']")?.GetAttribute("value"));
        Assert.DoesNotContain("Maya Chen", await owner.GetStringAsync(route));

        Assert.Equal(savedProfiles, await database.AutofillProfiles.CountAsync());
    }

    private static MultipartFormDataContent UploadResume(string? token = null)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
            Write("word/document.xml", """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                    xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>
                    <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Maya Chen</w:t></w:r></w:p></w:tc>
                      <w:tc><w:p><w:hyperlink r:id="rId1"><w:r><w:t>LinkedIn</w:t></w:r></w:hyperlink></w:p></w:tc></w:tr></w:tbl>
                    <w:p><w:r><w:t>maya@example.test</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Work experience</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Support Technician at Wattle Systems</w:t></w:r></w:p>
                    <w:p><w:r><w:t>Feb 2024 – Present</w:t></w:r></w:p>
                  </w:body>
                </w:document>
                """);
            Write("word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"
                    Target="https://linkedin.com/in/maya-example" TargetMode="External" />
                </Relationships>
                """);

            void Write(string path, string text)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(text);
            }
        }
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(stream.ToArray()), "resume", "synthetic-resume.docx");
        if (token is not null)
        {
            form.Add(new StringContent(token), "__RequestVerificationToken");
        }
        return form;
    }
}
