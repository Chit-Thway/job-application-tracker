using System.ComponentModel.DataAnnotations;
using JobTracker.Web.Data;
using JobTracker.Web.Extraction;

namespace JobTracker.Web.Models;

public sealed class PastedTextInputViewModel
{
    [Required]
    [StringLength(100_000, MinimumLength = 20)]
    [Display(Name = "Job description or posting text")]
    public string SourceText { get; set; } = string.Empty;
}

public sealed class JobPostingUrlInputViewModel
{
    [Required]
    [StringLength(2048)]
    [Url]
    [Display(Name = "Public job-posting URL")]
    public string SourceUrl { get; set; } = string.Empty;
}

public sealed class BrowserExtensionCaptureInputViewModel
{
    [Required]
    [StringLength(120_000)]
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class ExtractionReviewViewModel
{
    public Guid DraftId { get; set; }

    public ExtractionSourceType SourceType { get; set; }

    public string ImportAction => SourceType switch
    {
        ExtractionSourceType.JobPostingUrl => "ImportUrl",
        ExtractionSourceType.BrowserExtension => "ImportExtension",
        _ => "PasteText",
    };

    public string ImportAgainLabel => SourceType switch
    {
        ExtractionSourceType.JobPostingUrl => "Import a different link",
        ExtractionSourceType.BrowserExtension => "Capture a different page",
        _ => "Paste different text",
    };

    public string OriginalSourceLabel => SourceType switch
    {
        ExtractionSourceType.JobPostingUrl => "Fetched job-page text",
        ExtractionSourceType.BrowserExtension => "Captured job-page text",
        _ => "Pasted job text",
    };

    public string SourceText { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public IReadOnlyDictionary<string, string> Evidence { get; set; } =
        new Dictionary<string, string>();

    public IReadOnlyList<string> Warnings { get; set; } = [];

    [Required]
    [StringLength(200)]
    [Display(Name = "Role title")]
    public string RoleTitle { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Company name")]
    public string? CompanyName { get; set; }

    [StringLength(300)]
    [Display(Name = "Company location")]
    public string? CompanyLocation { get; set; }

    public Guid? ExistingCompanyId { get; set; }

    public IReadOnlyList<CompanyReuseSuggestion> CompanySuggestions { get; set; } = [];

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Application date")]
    public DateOnly? AppliedOn { get; set; }

    [StringLength(100)]
    [Display(Name = "Work arrangement")]
    public string? WorkplaceMode { get; set; }

    [StringLength(2048)]
    [Url]
    [Display(Name = "Job posting URL")]
    public string? SourceUrl { get; set; }

    [StringLength(2048)]
    [HttpUrl]
    [Display(Name = "Application portal URL")]
    public string? ApplicationPortalUrl { get; set; }

    [StringLength(200)]
    [Display(Name = "Source site")]
    public string? SourceSite { get; set; }

    [StringLength(200)]
    [Display(Name = "Job reference")]
    public string? JobReference { get; set; }

    [StringLength(500)]
    [Display(Name = "Salary")]
    public string? SalaryText { get; set; }

    [StringLength(200)]
    [Display(Name = "Employment type")]
    public string? EmploymentType { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Closing date")]
    public DateOnly? ClosingDate { get; set; }

    [StringLength(200)]
    [Display(Name = "Contact name")]
    public string? ContactName { get; set; }

    [StringLength(320)]
    [EmailAddress]
    [Display(Name = "Contact email")]
    public string? ContactEmail { get; set; }

    [StringLength(100_000)]
    [Display(Name = "Job description")]
    public string? DescriptionText { get; set; }

    [StringLength(10_000)]
    public string? Notes { get; set; }

    [Display(Name = "Saved")]
    public bool IsSavedForever { get; set; }

    public string? EvidenceFor(string field) =>
        Evidence.TryGetValue(field, out var note) ? note : null;
}
