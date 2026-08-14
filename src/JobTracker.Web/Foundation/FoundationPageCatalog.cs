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
                "Follow-ups, appointments, and scheduled-deletion warnings",
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
