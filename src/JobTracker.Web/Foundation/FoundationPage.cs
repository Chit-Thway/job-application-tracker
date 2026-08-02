namespace JobTracker.Web.Foundation;

public sealed record FoundationPage(
    string Action,
    string Section,
    string Route,
    string NavigationLabel,
    string Eyebrow,
    string Title,
    string Description,
    string NextMilestone,
    IReadOnlyList<string> PreviewItems);
