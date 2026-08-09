namespace JobTracker.Web.Data;

public enum PipelineStage
{
    Applied,
    RecruiterContact,
    Screening,
    Assessment,
    Interview,
    ReferenceCheck,
    Offer,
}

public enum ApplicationOutcome
{
    Active,
    Rejected,
    Withdrawn,
    Ghosted,
    Accepted,
    OfferDeclined,
}

public enum InteractionType
{
    Call,
    Email,
    Message,
    Meeting,
    Note,
}

public enum AppointmentType
{
    Interview,
    Call,
    Assessment,
    Other,
}

public enum ExtractionSourceType
{
    PastedText,
    JobPostingUrl,
    BrowserExtension,
}
