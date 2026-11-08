using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using JobTracker.Web.Autofill;
using Microsoft.AspNetCore.Http;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace JobTracker.UnitTests;

public sealed class ResumeImportTests
{
    [Fact]
    public async Task Docx_ExtractsStructuredDetailsAndKeepsSavedEditsOnReimport()
    {
        var bytes = WordDocument("""
            Maya Chen
            maya@example.test
            Phone: +61 491 570 006
            Address: 10 Sample Street, Darwin NT 0800, Australia
            LinkedIn: linkedin.com/in/maya-example
            GitHub: github.com/maya-example
            Portfolio: https://example.test/portfolio
            Professional Experience
            IT Support Technician | Wattle Systems | Darwin, NT
            Feb 2024 – Present
            • Resolve support tickets
            Service Desk Assistant
            Paperbark IT
            Jul 2022 to Jan 2024
            • Triaged requests
            Education
            Diploma of Information Technology | Example College
            Feb 2020 – Nov 2021
            Major: Networking
            Certifications
            Working at Heights | Example Training
            Issued: Mar 2023
            References
            A Reference
            referee@example.test
            0412 345 678
            """);
        using var service = new ResumeImportService();
        var draft = new ApplicantProfile { Email = "confirmed@example.test" };
        using var file = new MemoryStream(bytes);
        var review = await service.ImportAsync(Upload(file, "resume.docx"), draft, CancellationToken.None);

        Assert.Equal("Maya Chen", draft.FullName);
        Assert.Equal("Maya", draft.GivenName);
        Assert.Equal("Chen", draft.FamilyName);
        Assert.Equal("confirmed@example.test", draft.Email);
        Assert.DoesNotContain(review.FilledFields, field => field.Name == nameof(ApplicantProfile.Email));
        var difference = Assert.Single(review.Differences);
        Assert.Equal("maya@example.test", difference.Suggested);
        Assert.Equal("+61 491 570 006", draft.Phone);
        Assert.Equal("10 Sample Street", draft.AddressLine1);
        Assert.Equal("Darwin", draft.City);
        Assert.Equal("NT", draft.Region);
        Assert.Equal("0800", draft.Postcode);
        Assert.Equal("Australia", draft.Country);
        Assert.Equal("https://linkedin.com/in/maya-example", draft.LinkedIn);
        Assert.Equal("https://github.com/maya-example", draft.GitHub);
        Assert.Equal("https://example.test/portfolio", draft.Portfolio);
        Assert.Collection(draft.Experience, current =>
        {
            Assert.Equal("Wattle Systems", current.Employer);
            Assert.Equal("IT Support Technician", current.Title);
            Assert.Equal("2024-02", current.StartMonth);
            Assert.True(current.Current);
            Assert.Null(current.EndMonth);
            Assert.Contains("Resolve support tickets", current.Responsibilities);
        }, past =>
        {
            Assert.Equal("Paperbark IT", past.Employer);
            Assert.Equal("2022-07", past.StartMonth);
            Assert.Equal("2024-01", past.EndMonth);
        });
        var education = Assert.Single(draft.Education);
        Assert.Equal("Example College", education.Institution);
        Assert.Equal("Networking", education.FieldOfStudy);
        Assert.Equal("2021-11", education.EndMonth);
        var certificate = Assert.Single(draft.Certifications);
        Assert.Equal("Working at Heights", certificate.Name);
        Assert.Equal("2023-03", certificate.IssuedMonth);

        draft.Experience[0].Responsibilities = "My confirmed description";
        using var reimport = new MemoryStream(bytes);
        var repeated = await service.ImportAsync(Upload(reimport, "same.docx"), draft, CancellationToken.None);
        Assert.Equal(0, repeated.RolesAdded + repeated.EducationAdded + repeated.CertificatesAdded);
        Assert.Equal(4, repeated.EntriesSkipped);
        Assert.Equal("My confirmed description", draft.Experience[0].Responsibilities);
    }

    [Fact]
    public async Task Pdf_KeepsCompoundNameAndYearOnlyDatesAtTheirAvailablePrecision()
    {
        var bytes = PdfDocument(
            "Maya de Silva",
            "maya@example.test",
            "Experience",
            "Technician at Wattle Systems",
            "2022 - 2024");
        using var service = new ResumeImportService();
        var draft = new ApplicantProfile();
        using var file = new MemoryStream(bytes);
        await service.ImportAsync(Upload(file, "resume.pdf"), draft, CancellationToken.None);

        Assert.Equal("Maya de Silva", draft.FullName);
        Assert.Null(draft.GivenName);
        Assert.Null(draft.FamilyName);
        Assert.Equal("maya@example.test", draft.Email);
        var role = Assert.Single(draft.Experience);
        Assert.Null(role.StartMonth);
        Assert.Null(role.EndMonth);
    }

    [Fact]
    public async Task RejectsWrongSignaturesTextlessPdfsAndOversizedExpandedWordParts()
    {
        using var service = new ResumeImportService();
        await Rejects("file.pdf", "not a pdf"u8.ToArray(), "Choose a valid PDF");
        await Rejects("blank.pdf", PdfDocument(), "no readable text");
        await Rejects("compressed.docx", WordDocument(new string('A', 1_100_000)), "expands beyond the size limit");

        async Task Rejects(string name, byte[] bytes, string message)
        {
            using var file = new MemoryStream(bytes);
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                service.ImportAsync(Upload(file, name), new ApplicantProfile(), CancellationToken.None));
            Assert.Contains(message, error.Message, StringComparison.Ordinal);
        }
    }

    private static FormFile Upload(Stream stream, string name) => new(stream, 0, stream.Length, "resume", name);

    private static byte[] WordDocument(string text)
    {
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var body = new XElement(word + "body", text.Split('\n').Select(line =>
            new XElement(word + "p", new XElement(word + "r", new XElement(word + "t", line.Trim())))));
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var types = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open(), Encoding.UTF8))
            {
                types.Write("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\" />");
            }
            using var document = archive.CreateEntry("word/document.xml").Open();
            new XDocument(new XElement(word + "document", body)).Save(document);
        }
        return stream.ToArray();
    }

    private static byte[] PdfDocument(params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        for (var index = 0; index < lines.Length; index++)
        {
            page.AddText(lines[index], 12, new PdfPoint(50, 780 - index * 20), font);
        }
        return builder.Build();
    }
}
