using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using System.ComponentModel.DataAnnotations;

namespace JobTracker.Web.Models;

public sealed class ApplicationFilterViewModel
{
    [Display(Name = "Search")]
    public string? Query { get; set; }

    [Display(Name = "Company")]
    public Guid? CompanyId { get; set; }

    public PipelineStage? Stage { get; set; }

    public ApplicationOutcome? Outcome { get; set; }

    [Display(Name = "Saved state")]
    public bool? IsSavedForever { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Applied from")]
    public DateOnly? AppliedFrom { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Applied to")]
    public DateOnly? AppliedTo { get; set; }

    public string Sort { get; set; } = "newest";
}

public sealed record ApplicationIndexViewModel(
    ApplicationFilterViewModel Filters,
    IReadOnlyList<ApplicationListItem> Applications,
    IReadOnlyList<CompanyListItem> Companies);

public sealed class ApplicationFormViewModel
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Role title")]
    public string RoleTitle { get; set; } = string.Empty;

    [Display(Name = "Company")]
    public Guid? CompanyId { get; set; }

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Application date")]
    public DateOnly? AppliedOn { get; set; }

    [StringLength(2048)]
    [Url]
    [Display(Name = "Job posting URL")]
    public string? SourceUrl { get; set; }

    [StringLength(10_000)]
    public string? Notes { get; set; }

    [Display(Name = "Saved")]
    public bool IsSavedForever { get; set; }

    public IReadOnlyList<CompanyListItem> Companies { get; set; } = [];
}

public sealed record ApplicationDeleteViewModel(ApplicationDetails Application);

public sealed class CompanyFilterViewModel
{
    public string? Query { get; set; }
}

public sealed record CompanyIndexViewModel(
    CompanyFilterViewModel Filters,
    IReadOnlyList<CompanyListItem> Companies);

public sealed class CompanyFormViewModel
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Location { get; set; }

    [StringLength(2048)]
    [Url]
    public string? Website { get; set; }

    [StringLength(10_000)]
    public string? Notes { get; set; }
}

public sealed record CompanyDeleteViewModel(
    CompanyDetails Company,
    string? ConflictMessage = null);
