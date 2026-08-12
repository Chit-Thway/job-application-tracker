using System.Text.Json;
using JobTracker.Web.Extraction;

namespace JobTracker.UnitTests;

public sealed class BrowserExtensionImportServiceTests
{
    private readonly BrowserExtensionImportService service =
        new(new PastedJobTextExtractor());

    [Fact]
    public void CapturedRenderedFields_OverrideWeakerDescriptionGuesses()
    {
        var result = service.Import(JsonSerializer.Serialize(new
        {
            version = 1,
            pageUrl = "https://au.seek.com/job/93675873?type=promoted",
            roleTitle = "IT Helpdesk Support Technician Level 1/2",
            companyName = "Technicalities Group Consulting",
            companyLocation = "Heatherton, Melbourne VIC",
            sourceSite = "SEEK",
            salaryText = "$70,000 - $80,000 per year & Super",
            employmentType = "Full time",
            closingDate = "2026-08-16",
            descriptionText = """
                About the role

                We are looking for a Level 1/2 Helpdesk Engineer.
                """,
            sourceText = """
                Sidekicker
                Salary: 4.2
                116 reviews

                We are looking for a Level 1/2 Helpdesk Engineer.
                """,
        }));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Extraction);
        var fields = result.Extraction.Fields;
        Assert.Equal("IT Helpdesk Support Technician Level 1/2", fields.RoleTitle);
        Assert.Equal("Technicalities Group Consulting", fields.CompanyName);
        Assert.Equal("Heatherton, Melbourne VIC", fields.CompanyLocation);
        Assert.Equal("$70,000 - $80,000 per year & Super", fields.SalaryText);
        Assert.Equal("Full time", fields.EmploymentType);
        Assert.Equal(new DateOnly(2026, 8, 16), fields.ClosingDate);
        Assert.Equal(
            "About the role\n\nWe are looking for a Level 1/2 Helpdesk Engineer.",
            fields.DescriptionText);
        Assert.Equal("https://au.seek.com/job/93675873?type=promoted", fields.SourceUrl);
        Assert.Contains("active page's rendered job metadata", result.Extraction.Evidence["RoleTitle"]);
        Assert.DoesNotContain(result.Extraction.Warnings, warning =>
            warning.StartsWith("No supported company", StringComparison.Ordinal));
    }

    [Fact]
    public void IndeedCapture_PreservesSelectedJobAndHourlyPay()
    {
        var result = service.Import(JsonSerializer.Serialize(new
        {
            version = 1,
            pageUrl = "https://au.indeed.com/jobs?l=Perth+WA&vjk=bbd3cfff48d83a54",
            roleTitle = "IT Support Technician",
            companyName = "V4 Services Pty Ltd",
            companyLocation = "Australia",
            sourceSite = "au.indeed.com",
            jobReference = "bbd3cfff48d83a54",
            salaryText = "$35 - $40 an hour",
            employmentType = "Casual",
            sourceText = """
                Job title: IT Support Technician
                Company: V4 Services Pty Ltd
                Location: Australia
                Salary: $35 - $40 an hour
                Employment type: Casual

                Welcome, Chit
                jobs in Perth WA
                Provide desktop troubleshooting and customer support.
                """,
        }));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Extraction);
        Assert.Equal("IT Support Technician", result.Extraction.Fields.RoleTitle);
        Assert.Equal("V4 Services Pty Ltd", result.Extraction.Fields.CompanyName);
        Assert.Equal("Australia", result.Extraction.Fields.CompanyLocation);
        Assert.Equal("$35 - $40 an hour", result.Extraction.Fields.SalaryText);
        Assert.Equal("Casual", result.Extraction.Fields.EmploymentType);
        Assert.Equal("bbd3cfff48d83a54", result.Extraction.Fields.JobReference);
        Assert.DoesNotContain("Welcome, Chit", result.Extraction.Fields.RoleTitle, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"version\":1,\"pageUrl\":\"file:///private/job.html\",\"sourceText\":\"A sufficiently long captured job page\"}")]
    public void InvalidOrUnsupportedCapture_FailsWithoutExtraction(string payload)
    {
        var result = service.Import(payload);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Extraction);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
