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
}
