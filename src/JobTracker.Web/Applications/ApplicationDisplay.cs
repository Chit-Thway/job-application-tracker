using JobTracker.Web.Data;

namespace JobTracker.Web.Applications;

public static class ApplicationDisplay
{
    public static IReadOnlyList<PipelineStage> ActiveStages { get; } =
    [
        PipelineStage.Applied,
        PipelineStage.Screening,
        PipelineStage.Assessment,
        PipelineStage.Interview,
        PipelineStage.Offer,
    ];

    public static IReadOnlyList<ApplicationOutcome> ActiveOutcomes { get; } =
    [
        ApplicationOutcome.Active,
        ApplicationOutcome.Rejected,
        ApplicationOutcome.Ghosted,
        ApplicationOutcome.Accepted,
        ApplicationOutcome.Withdrawn,
    ];

    public static PipelineStage NormalizeStage(PipelineStage stage) => stage switch
    {
        PipelineStage.RecruiterContact => PipelineStage.Screening,
        PipelineStage.ReferenceCheck => PipelineStage.Interview,
        _ => stage,
    };

    public static ApplicationOutcome NormalizeOutcome(ApplicationOutcome outcome) => outcome switch
    {
        ApplicationOutcome.OfferDeclined => ApplicationOutcome.Withdrawn,
        _ => outcome,
    };

    public static bool IsActive(PipelineStage stage) => ActiveStages.Contains(stage);

    public static bool IsActive(ApplicationOutcome outcome) => ActiveOutcomes.Contains(outcome);

    public static string Stage(PipelineStage stage) => stage switch
    {
        PipelineStage.RecruiterContact => "Screening",
        PipelineStage.ReferenceCheck => "Interview",
        _ => stage.ToString(),
    };

    public static string Outcome(ApplicationOutcome outcome) => outcome switch
    {
        ApplicationOutcome.OfferDeclined => "Withdrawn",
        _ => outcome.ToString(),
    };

    public static string StageCssClass(PipelineStage stage) => NormalizeStage(stage) switch
    {
        PipelineStage.Applied => "status-stage-applied",
        PipelineStage.Screening => "status-stage-screening",
        PipelineStage.Assessment => "status-stage-assessment",
        PipelineStage.Interview => "status-stage-interview",
        PipelineStage.Offer => "status-stage-offer",
        _ => string.Empty,
    };

    public static string OutcomeCssClass(ApplicationOutcome outcome) => NormalizeOutcome(outcome) switch
    {
        ApplicationOutcome.Active => "status-outcome-active",
        ApplicationOutcome.Accepted => "status-outcome-accepted",
        ApplicationOutcome.Rejected => "status-outcome-rejected",
        ApplicationOutcome.Withdrawn => "status-outcome-withdrawn",
        ApplicationOutcome.Ghosted => "status-outcome-ghosted",
        _ => string.Empty,
    };

    public static string Interaction(InteractionType type) => type switch
    {
        InteractionType.Call => "Call",
        InteractionType.Email => "Email",
        InteractionType.Message => "Message",
        InteractionType.Meeting => "Meeting",
        InteractionType.Note => "Note",
        _ => type.ToString(),
    };

    public static string Appointment(AppointmentType type) => type switch
    {
        AppointmentType.Interview => "Interview",
        AppointmentType.Call => "Call",
        AppointmentType.Assessment => "Assessment",
        AppointmentType.Other => "Other appointment",
        _ => type.ToString(),
    };
}
