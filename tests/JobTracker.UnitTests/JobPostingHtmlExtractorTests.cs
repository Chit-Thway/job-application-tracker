using JobTracker.Web.Extraction;

namespace JobTracker.UnitTests;

public sealed class JobPostingHtmlExtractorTests
{
    private readonly JobPostingHtmlExtractor extractor = new(new PastedJobTextExtractor());

    [Fact]
    public void JobPostingJsonLd_ProvidesOfficialEmployerAndPostingFields()
    {
        const string html = """
            <!doctype html>
            <html>
            <head>
              <meta property="og:site_name" content="Synthetic Graduate Board">
              <script type="application/ld+json">
              {
                "@context": "https://schema.org",
                "@type": "JobPosting",
                "title": "Graduate Program (Feb 2027)",
                "description": "<p>Build a consulting career with a fictional team.</p>",
                "hiringOrganization": { "@type": "Organization", "name": "Escient" },
                "jobLocation": {
                  "@type": "Place",
                  "address": {
                    "@type": "PostalAddress",
                    "addressLocality": "Sydney",
                    "addressRegion": "NSW",
                    "addressCountry": "AU"
                  }
                },
                "employmentType": "FULL_TIME",
                "identifier": { "@type": "PropertyValue", "value": "ESC-2027" },
                "baseSalary": {
                  "@type": "MonetaryAmount",
                  "currency": "AUD",
                  "value": { "@type": "QuantitativeValue", "minValue": 70000, "maxValue": 75000, "unitText": "YEAR" }
                },
                "validThrough": "2026-08-16T23:59:59+10:00",
                "applicationContact": { "name": "Graduate Recruitment", "email": "graduates@example.test" }
              }
              </script>
            </head>
            <body><h1>Graduate Program</h1><p>Location</p><p>Sydney NSW</p></body>
            </html>
            """;
        var requested = new Uri("https://jobs.example.test/graduate?id=42");

        var result = extractor.Extract(requested, requested, html);

        Assert.Equal("Graduate Program (Feb 2027)", result.Fields.RoleTitle);
        Assert.Equal("Escient", result.Fields.CompanyName);
        Assert.Equal("Sydney, NSW, AU", result.Fields.CompanyLocation);
        Assert.Equal("AUD 70,000 - 75,000 / Year", result.Fields.SalaryText);
        Assert.Equal("Full-time", result.Fields.EmploymentType);
        Assert.Equal("ESC-2027", result.Fields.JobReference);
        Assert.Equal(new DateOnly(2026, 8, 16), result.Fields.ClosingDate);
        Assert.Equal("Graduate Recruitment", result.Fields.ContactName);
        Assert.Equal("graduates@example.test", result.Fields.ContactEmail);
        Assert.Equal(requested.AbsoluteUri, result.Fields.SourceUrl);
        Assert.Equal("Synthetic Graduate Board", result.Fields.SourceSite);
        Assert.Contains("official JobPosting hiring organisation", result.Evidence["CompanyName"], StringComparison.Ordinal);
        Assert.DoesNotContain("application/ld+json", result.OriginalText, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedStructuredData_FallsBackToVisibleDeterministicText()
    {
        const string html = """
            <html><head>
              <meta property="og:site_name" content="Fallback Careers">
              <script type="application/ld+json">{ not valid json }</script>
            </head><body>
              <p>Job Title: Platform Engineer</p>
              <p>Company: Synthetic Fallback Labs</p>
              <p>Location: Perth, WA</p>
            </body></html>
            """;
        var requested = new Uri("https://careers.example.test/jobs/platform");

        var result = extractor.Extract(requested, requested, html);

        Assert.Equal("Platform Engineer", result.Fields.RoleTitle);
        Assert.Equal("Synthetic Fallback Labs", result.Fields.CompanyName);
        Assert.Equal("Perth, WA", result.Fields.CompanyLocation);
        Assert.Equal("Fallback Careers", result.Fields.SourceSite);
    }

    [Fact]
    public void JsonLdGraphAndRemoteType_AreRecognized()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            { "@graph": [
              { "@type": "Organization", "name": "Ignore Me" },
              { "@type": ["Thing", "JobPosting"], "title": "Remote Analyst",
                "hiringOrganization": { "name": "Synthetic Remote Co" },
                "jobLocationType": "TELECOMMUTE" }
            ] }
            </script></head><body><p>Remote opportunity.</p></body></html>
            """;
        var requested = new Uri("https://remote.example.test/job/1");

        var result = extractor.Extract(requested, requested, html);

        Assert.Equal("Remote Analyst", result.Fields.RoleTitle);
        Assert.Equal("Synthetic Remote Co", result.Fields.CompanyName);
        Assert.Equal("Remote", result.Fields.WorkplaceMode);
    }

    [Fact]
    public void SeekSemanticJobElements_OverrideGenericPageChrome()
    {
        var html = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Html",
            "seek-job-details.html"));
        var requested = new Uri(
            "https://au.seek.com/job/93675873?ref=search-standalone&type=promoted&origin=showNewTab");

        var result = extractor.Extract(requested, requested, html);

        Assert.Equal("IT Helpdesk Support Technician Level 1/2", result.Fields.RoleTitle);
        Assert.Equal("Technicalities Group Consulting", result.Fields.CompanyName);
        Assert.Equal("Heatherton, Melbourne VIC", result.Fields.CompanyLocation);
        Assert.Equal("Full time", result.Fields.EmploymentType);
        Assert.Equal("$70,000 – $80,000 per year & Super", result.Fields.SalaryText);
        Assert.Equal(requested.AbsoluteUri, result.Fields.SourceUrl);
        Assert.StartsWith("IT Helpdesk Support Technician Level 1/2", result.OriginalText);
        Assert.Contains("successful IT Services company", result.OriginalText, StringComparison.Ordinal);
        Assert.DoesNotContain("Sidekicker", result.OriginalText, StringComparison.Ordinal);
        Assert.Equal(
            "High confidence — read from the page’s labelled job-page element for employer.",
            result.Evidence["CompanyName"]);
        Assert.DoesNotContain(
            result.Evidence.Values,
            value => value.Contains('\u00E2', StringComparison.Ordinal)
                || value.Contains('\u00C3', StringComparison.Ordinal)
                || value.Contains('\uFFFD', StringComparison.Ordinal));
        Assert.DoesNotContain(
            result.Warnings,
            warning => warning.Contains("job-title", StringComparison.OrdinalIgnoreCase)
                || warning.Contains("company label", StringComparison.OrdinalIgnoreCase)
                || warning.Contains("location label", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OfficialJobPostingData_RemainsStrongerThanSemanticPageElements()
    {
        const string html = """
            <html><head><script type="application/ld+json">
            {
              "@type": "JobPosting",
              "title": "Official Role",
              "hiringOrganization": { "name": "Official Employer" },
              "jobLocation": { "address": { "addressLocality": "Perth", "addressRegion": "WA" } },
              "employmentType": "FULL_TIME",
              "baseSalary": { "currency": "AUD", "value": { "minValue": 80000, "maxValue": 90000, "unitText": "YEAR" } }
            }
            </script></head><body>
              <h1 data-automation="job-detail-title">Weaker Page Role</h1>
              <span data-automation="advertiser-name">Weaker Page Employer</span>
              <span data-automation="job-detail-location">Sydney NSW</span>
              <span data-automation="job-detail-work-type">Part time</span>
              <span data-automation="job-detail-salary">$40 per hour</span>
              <div data-automation="jobAdDetails">A readable description.</div>
            </body></html>
            """;
        var requested = new Uri("https://jobs.example.test/official-role");

        var result = extractor.Extract(requested, requested, html);

        Assert.Equal("Official Role", result.Fields.RoleTitle);
        Assert.Equal("Official Employer", result.Fields.CompanyName);
        Assert.Equal("Perth, WA", result.Fields.CompanyLocation);
        Assert.Equal("Full-time", result.Fields.EmploymentType);
        Assert.Equal("AUD 80,000 - 90,000 / Year", result.Fields.SalaryText);
        Assert.Contains("official JobPosting", result.Evidence["CompanyName"], StringComparison.Ordinal);
    }
}
