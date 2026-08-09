using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using System.ComponentModel.DataAnnotations;

namespace JobTracker.Web.Models;

public sealed class ApplicationWorkflowPageViewModel
{
    public required ApplicationWorkflowDetails Workflow { get; set; }

    public StatusTransitionFormViewModel Status { get; set; } = new();

    public ContactFormViewModel Contact { get; set; } = new();

    public InteractionFormViewModel Interaction { get; set; } = new();

    public WorkflowTaskFormViewModel Task { get; set; } = new();

    public AppointmentFormViewModel Appointment { get; set; } = new();
}

public sealed class StatusTransitionFormViewModel
{
    [EnumDataType(typeof(PipelineStage))]
    [Display(Name = "Pipeline stage")]
    public PipelineStage Stage { get; set; }

    [EnumDataType(typeof(ApplicationOutcome))]
    public ApplicationOutcome Outcome { get; set; }

    [StringLength(2_000)]
    [Display(Name = "What changed?")]
    public string? Note { get; set; }
}

public sealed class ContactFormViewModel
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Job title")]
    public string? JobTitle { get; set; }

    [StringLength(320)]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(10_000)]
    public string? Notes { get; set; }
}

public sealed class InteractionFormViewModel
{
    [Display(Name = "Contact")]
    public Guid? ContactId { get; set; }

    [EnumDataType(typeof(InteractionType))]
    public InteractionType Type { get; set; } = InteractionType.Email;

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "When it happened")]
    public DateTime? OccurredAtLocal { get; set; }

    [Display(Name = "Counts as an employer response")]
    public bool IsEmployerResponse { get; set; }

    [StringLength(10_000)]
    public string? Notes { get; set; }
}

public sealed class WorkflowTaskFormViewModel
{
    [Required]
    [StringLength(240)]
    public string Title { get; set; } = string.Empty;

    [DataType(DataType.DateTime)]
    [Display(Name = "Due date and time")]
    public DateTime? DueAtLocal { get; set; }

    [StringLength(10_000)]
    public string? Notes { get; set; }
}

public sealed class AppointmentFormViewModel : IValidatableObject
{
    [EnumDataType(typeof(AppointmentType))]
    public AppointmentType Type { get; set; } = AppointmentType.Interview;

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "Starts")]
    public DateTime? StartsAtLocal { get; set; }

    [Required]
    [DataType(DataType.DateTime)]
    [Display(Name = "Ends")]
    public DateTime? EndsAtLocal { get; set; }

    [StringLength(2048)]
    [Display(Name = "Location or meeting link")]
    public string? LocationOrLink { get; set; }

    [StringLength(10_000)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsAtLocal is not null
            && EndsAtLocal is not null
            && EndsAtLocal <= StartsAtLocal)
        {
            yield return new ValidationResult(
                "The appointment must end after it starts.",
                [nameof(EndsAtLocal)]);
        }
    }
}
