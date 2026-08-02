namespace JobTracker.Web.Foundation;

public static class FoundationPageCatalog
{
    public static IReadOnlyList<FoundationPage> All { get; } =
    [
        new(
            Action: "Dashboard",
            Section: "dashboard",
            Route: "/dashboard",
            NavigationLabel: "Dashboard",
            Eyebrow: "Your three-month view",
            Title: "The signal, without the noise.",
            Description: "This will become your single view of applications, responses, interviews, follow-ups, and upcoming appointments for the current month and the two before it.",
            NextMilestone: "Dashboard data and metrics arrive in Milestone 7.",
            PreviewItems:
            [
                "Current and previous two calendar months",
                "Recent activity and pipeline overview",
                "Follow-ups, appointments, and Chopping Block warnings",
            ]),
        new(
            Action: "Applications",
            Section: "applications",
            Route: "/applications",
            NavigationLabel: "Applications",
            Eyebrow: "Your application library",
            Title: "Every opportunity, easy to find.",
            Description: "This is where applications will be searchable by role, company, date, status, and whether you chose to keep them forever.",
            NextMilestone: "Manual application management arrives in Milestone 3.",
            PreviewItems:
            [
                "Search, sort, and focused filters",
                "Saved — keep forever state",
                "Clear Recent and Chopping Block labels",
            ]),
        new(
            Action: "AddApplication",
            Section: "add",
            Route: "/applications/new",
            NavigationLabel: "Add application",
            Eyebrow: "Capture an opportunity",
            Title: "Start with what you have.",
            Description: "You will be able to enter an application manually, paste a job posting, or provide a public URL—then review every detected field before saving.",
            NextMilestone: "Manual entry arrives in Milestone 3; extraction follows in Milestones 4 and 5.",
            PreviewItems:
            [
                "Manual entry for reliable control",
                "Rule-based pasted-text extraction",
                "Safe URL import with a review step",
            ]),
        new(
            Action: "ActionCentre",
            Section: "actions",
            Route: "/actions",
            NavigationLabel: "Action Centre",
            Eyebrow: "What needs attention",
            Title: "Know your next move.",
            Description: "Follow-ups, overdue tasks, interviews, ghosting warnings, and deletion dates will meet here in one calm queue.",
            NextMilestone: "Tracking arrives in Milestone 6; the Action Centre is completed in Milestone 7.",
            PreviewItems:
            [
                "Due and overdue follow-ups",
                "Upcoming appointments",
                "14-day ghosting and retention warnings",
            ]),
        new(
            Action: "Settings",
            Section: "settings",
            Route: "/settings",
            NavigationLabel: "Settings",
            Eyebrow: "Make it yours",
            Title: "Simple controls, clear consequences.",
            Description: "Profile, timezone, account security, and a plain-language explanation of retention will live here.",
            NextMilestone: "Account and timezone settings begin in Milestone 2.",
            PreviewItems:
            [
                "Profile and timezone",
                "Password and verified email",
                "Retention and privacy explanation",
            ]),
        new(
            Action: "Demo",
            Section: "demo",
            Route: "/demo",
            NavigationLabel: "Public demo",
            Eyebrow: "Safe to share",
            Title: "A realistic demo. Never real data.",
            Description: "The public portfolio experience will use separate, read-only synthetic applications and will never allow edits or account registration.",
            NextMilestone: "The isolated public demo arrives in Milestone 9.",
            PreviewItems:
            [
                "Clearly marked synthetic records",
                "Read-only routes and controls",
                "Complete separation from private data",
            ]),
    ];

    public static FoundationPage GetByAction(string actionName)
    {
        return All.Single(page =>
            string.Equals(page.Action, actionName, StringComparison.Ordinal));
    }
}
