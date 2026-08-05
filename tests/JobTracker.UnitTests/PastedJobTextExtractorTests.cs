using System.Text.Json;
using JobTracker.Web.Extraction;

namespace JobTracker.UnitTests;

public sealed class PastedJobTextExtractorTests
{
    private readonly PastedJobTextExtractor extractor = new();

    [Fact]
    public void CommonLabelledPosting_ExtractsEveryExplicitField()
    {
        var source = Fixture("common-labelled.txt");
        var result = extractor.Extract(source);

        Assert.Equal(source, result.OriginalText);
        Assert.Equal("Senior Platform Engineer", result.Fields.RoleTitle);
        Assert.Equal("Synthetic Meridian Works", result.Fields.CompanyName);
        Assert.Equal("Perth, WA", result.Fields.CompanyLocation);
        Assert.Equal("Hybrid", result.Fields.WorkplaceMode);
        Assert.Equal("Full-time", result.Fields.EmploymentType);
        Assert.Equal("AUD 130,000–150,000 plus super", result.Fields.SalaryText);
        Assert.Equal("SMW-4821", result.Fields.JobReference);
        Assert.Equal(new DateOnly(2026, 8, 28), result.Fields.ClosingDate);
        Assert.Equal("Morgan Example", result.Fields.ContactName);
        Assert.Equal("morgan@example.test", result.Fields.ContactEmail);
        Assert.Equal("Synthetic Careers", result.Fields.SourceSite);
        Assert.Equal("https://example.test/jobs/smw-4821", result.Fields.SourceUrl);
        Assert.Contains("RoleTitle", result.Evidence.Keys);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void AustralianLabelVariants_AreRecognizedWithoutGuessing()
    {
        var result = extractor.Extract(Fixture("australian-variant.txt"));

        Assert.Equal("Service Delivery Analyst", result.Fields.RoleTitle);
        Assert.Equal("Synthetic Harbour Cooperative", result.Fields.CompanyName);
        Assert.Equal("Fremantle, WA", result.Fields.CompanyLocation);
        Assert.Equal("On-site", result.Fields.WorkplaceMode);
        Assert.Equal("Fixed-term contract", result.Fields.EmploymentType);
        Assert.Equal("$92,000 package", result.Fields.SalaryText);
        Assert.Equal("SHC-177", result.Fields.JobReference);
        Assert.Equal(new DateOnly(2026, 9, 2), result.Fields.ClosingDate);
        Assert.Equal("careers@example.test", result.Fields.ContactEmail);
    }

    [Fact]
    public void UnlabelledPosting_LeavesUncertainCoreFieldsBlank()
    {
        var result = extractor.Extract(Fixture("sparse-uncertain.txt"));

        Assert.Null(result.Fields.RoleTitle);
        Assert.Null(result.Fields.CompanyName);
        Assert.Null(result.Fields.CompanyLocation);
        Assert.Null(result.Fields.WorkplaceMode);
        Assert.Contains(result.Warnings, warning => warning.Contains("job-title", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("company", StringComparison.Ordinal));
        Assert.DoesNotContain("thoughtful team", JsonSerializer.Serialize(result.Fields), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeekStyleStackedHeader_ExtractsCorroboratedFieldsWithoutInventingOthers()
    {
        var result = extractor.Extract(Fixture("seek-style-unlabelled.txt"));

        Assert.Equal("2027 Computer Science Graduate Program - Cybersecurity (NSW)", result.Fields.RoleTitle);
        Assert.Equal("Synthetic Horizon Advisory", result.Fields.CompanyName);
        Assert.Equal("Sydney NSW", result.Fields.CompanyLocation);
        Assert.Equal("$70,000 - $80,000 a year", result.Fields.SalaryText);
        Assert.Equal("SEEK", result.Fields.SourceSite);
        Assert.Null(result.Fields.EmploymentType);
        Assert.Null(result.Fields.WorkplaceMode);
        Assert.Null(result.Fields.ClosingDate);
        Assert.Contains("Medium confidence", result.Evidence["RoleTitle"], StringComparison.Ordinal);
        Assert.Contains("directly below", result.Evidence["CompanyName"], StringComparison.Ordinal);
        Assert.Contains(result.Warnings, warning => warning.Contains("no year", StringComparison.Ordinal));
    }

    [Fact]
    public void LinkedInStyleLayout_UsesApplyHeaderLocationAndSeparateMetadataHeading()
    {
        var result = extractor.Extract(Fixture("linkedin-style.txt"));

        Assert.Equal("Applications Analyst", result.Fields.RoleTitle);
        Assert.Equal("Synthetic Care Group", result.Fields.CompanyName);
        Assert.Equal("Baulkham Hills, New South Wales, Australia", result.Fields.CompanyLocation);
        Assert.Equal("Hybrid", result.Fields.WorkplaceMode);
        Assert.Equal("Full-time", result.Fields.EmploymentType);
        Assert.Contains("apply for this role", result.Evidence["RoleTitle"], StringComparison.Ordinal);
        Assert.Contains("immediately followed", result.Evidence["EmploymentType"], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "Synthetic Care Group hiring Applications Analyst in Sydney NSW",
        "Applications Analyst",
        "Synthetic Care Group",
        "Sydney NSW")]
    [InlineData(
        "Applications Analyst at Synthetic Care Group — Melbourne, Victoria, Australia | LinkedIn Jobs",
        "Applications Analyst",
        "Synthetic Care Group",
        "Melbourne, Victoria, Australia")]
    public void JobBoardPageTitles_ExtractRoleCompanyAndAustralianLocation(
        string pageTitle,
        string expectedRole,
        string expectedCompany,
        string expectedLocation)
    {
        var result = extractor.Extract($"{pageTitle}\nApply\nAbout the role\nA fictional posting used only for testing.");

        Assert.Equal(expectedRole, result.Fields.RoleTitle);
        Assert.Equal(expectedCompany, result.Fields.CompanyName);
        Assert.Equal(expectedLocation, result.Fields.CompanyLocation);
    }

    [Fact]
    public void StackedHeader_StripsCompanyRatingAndUnderstandsContactEmailValue()
    {
        const string source = """
            Support Engineer
            Synthetic Atlas 3.8 out of 5 stars
            Melbourne VIC
            $95,000
            Contact: careers@example.test
            """;

        var result = extractor.Extract(source);

        Assert.Equal("Support Engineer", result.Fields.RoleTitle);
        Assert.Equal("Synthetic Atlas", result.Fields.CompanyName);
        Assert.Equal("$95,000", result.Fields.SalaryText);
        Assert.Null(result.Fields.ContactName);
        Assert.Equal("careers@example.test", result.Fields.ContactEmail);
    }

    [Fact]
    public void DuplicateSalaryHeading_IgnoresRatingAndUsesRealPayRange()
    {
        var result = extractor.Extract(Fixture("duplicate-salary-rating.txt"));

        Assert.Equal("Graduate Program (Feb 2027)", result.Fields.RoleTitle);
        Assert.Equal("Synthetic Escent Advisory", result.Fields.CompanyName);
        Assert.Equal("Adelaide, Brisbane, Melbourne, Sydney", result.Fields.CompanyLocation);
        Assert.Equal("AUD 70,000 - 75,000 / Year", result.Fields.SalaryText);
        Assert.NotEqual("4.2", result.Fields.SalaryText);
        Assert.Equal(new DateOnly(2026, 8, 16), result.Fields.ClosingDate);
        Assert.Contains("immediately followed", result.Evidence["SalaryText"], StringComparison.Ordinal);
    }

    [Fact]
    public void LocationHeadingAfterRole_DoesNotBecomeCompanyName()
    {
        var result = extractor.Extract(Fixture("missing-company-location-heading.txt"));

        Assert.Equal("Graduate Program (Feb 2027)", result.Fields.RoleTitle);
        Assert.Null(result.Fields.CompanyName);
        Assert.Equal("Adelaide, Brisbane, Melbourne, Sydney", result.Fields.CompanyLocation);
        Assert.Equal("AUD 70,000 - 75,000 / Year", result.Fields.SalaryText);
        Assert.Equal(new DateOnly(2026, 8, 16), result.Fields.ClosingDate);
        Assert.Contains(result.Warnings, warning => warning.Contains("company", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("AUD 70,000 - 75,000 / Year")]
    [InlineData("70k - 80k")]
    [InlineData("$35 - $45 per hour")]
    [InlineData("$950 per day")]
    [InlineData("$95,000")]
    public void PlausibleSalaryFormats_AreAccepted(string salary)
    {
        var result = extractor.Extract($"Job Title: Test Engineer\nCompany: Synthetic Test Co\nSalary: {salary}");

        Assert.Equal(salary, result.Fields.SalaryText);
    }

    [Theory]
    [InlineData("4.2")]
    [InlineData("116 reviews")]
    [InlineData("Competitive")]
    public void RatingReviewCountAndVagueText_AreNotAcceptedAsSalary(string value)
    {
        var result = extractor.Extract($"Job Title: Test Engineer\nCompany: Synthetic Test Co\nSalary: {value}");

        Assert.Null(result.Fields.SalaryText);
    }

    [Fact]
    public void RepeatedExtraction_IsDeterministic_AndNormalizationPreservesParagraphs()
    {
        const string source = "\r\nJob Title:\tPlatform Engineer  \r\n\r\n\r\nCompany:  Synthetic Atlas  \r\n";

        var first = extractor.Extract(source);
        var second = extractor.Extract(source);

        Assert.Equal("Job Title: Platform Engineer\n\nCompany: Synthetic Atlas", first.NormalizedText);
        Assert.Equal(first.Fields, second.Fields);
        Assert.Equal(first.Evidence, second.Evidence);
        Assert.Equal(first.Warnings, second.Warnings);
    }

    [Fact]
    public void InvalidExplicitDateAndUrl_AreReportedAndLeftBlank()
    {
        const string source = "Job Title: Test Engineer\nCompany: Synthetic Test Co\nClosing Date: someday soon\nJob URL: file:///private.txt";

        var result = extractor.Extract(source);

        Assert.Null(result.Fields.ClosingDate);
        Assert.Null(result.Fields.SourceUrl);
        Assert.Contains(result.Warnings, warning => warning.Contains("not understood", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("URL was invalid", StringComparison.Ordinal));
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "PastedJobs", name));
}
