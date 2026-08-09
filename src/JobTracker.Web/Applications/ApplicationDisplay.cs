using JobTracker.Web.Data;

namespace JobTracker.Web.Applications;

public static class ApplicationDisplay
{
    public static string Stage(PipelineStage stage) => stage switch
    {
        PipelineStage.RecruiterContact => "Recruiter contact",
        PipelineStage.ReferenceCheck => "Reference check",
        _ => stage.ToString(),
    };

    public static string Outcome(ApplicationOutcome outcome) => outcome switch
    {
        ApplicationOutcome.OfferDeclined => "Offer declined",
        _ => outcome.ToString(),
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
