using JobTracker.Web.Applications;
using JobTracker.Web.Data;

namespace JobTracker.Web.Demo;

public sealed class DemoCatalog(TimeProvider timeProvider)
{
    public const string TimeZoneId = "Australia/Perth";

    public DemoDashboardViewModel GetDashboard()
    {
        var today = Today();
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        return new DemoDashboardViewModel(
            today,
            currentMonth.AddMonths(-2),
            currentMonth.AddMonths(1).AddDays(-1),
            BuildApplications(today));
    }

    public IReadOnlyList<DemoApplication> GetApplications() => BuildApplications(Today());

    public DemoApplication? Find(string slug) => GetApplications().SingleOrDefault(item =>
        string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase));

    private DateOnly Today() => DateOnly.FromDateTime(
        ApplicationTime.ToLocal(timeProvider.GetUtcNow(), TimeZoneId));

    private static IReadOnlyList<DemoApplication> BuildApplications(DateOnly today) =>
    [
        new(
            Slug: "nova-harbour-graduate-platform-engineer",
            CompanyName: "Nova Harbour Labs",
            RoleTitle: "Graduate Platform Engineer",
            Location: "Perth WA",
            WorkArrangement: "Hybrid",
            AppliedOn: today.AddDays(-4),
            Stage: PipelineStage.Screening,
            Outcome: ApplicationOutcome.Active,
            RetentionState: DemoRetentionState.Saved,
            DeletionScheduledAt: null,
            EmploymentType: "Full time",
            Salary: "$78,000 - $86,000 plus super",
            SourceSite: "Synthetic Careers Board",
            JobReference: "DEMO-NHL-104",
            Summary: "A graduate role supporting internal developer tooling and reliable cloud services.",
            Description:
            [
                Section(
                    "About the role",
                    "Join a small platform team that helps product engineers ship dependable services. You will pair with experienced engineers while building practical skills in automation, observability, and cloud operations."),
                Section(
                    "What you will do",
                    bullets:
                    [
                        "Improve deployment checks and engineering dashboards.",
                        "Investigate service issues with a supportive on-call partner.",
                        "Document repeatable solutions for the wider engineering team.",
                    ]),
                Section(
                    "What we are looking for",
                    "Curiosity, clear communication, and foundational programming knowledge matter more than experience with a particular cloud provider."),
            ],
            Contacts:
            [
                new("Avery Sample", "Early careers partner", "avery.sample@example.test", "Synthetic contact for demonstration only."),
            ],
            Activity:
            [
                new("Applied", At(today.AddDays(-4), 10, 15), "Application submitted through the synthetic careers board."),
                new("Email", At(today.AddDays(-2), 14, 20), "Avery Sample confirmed the application was received.", IsEmployerResponse: true),
            ],
            Tasks:
            [
                new("Send portfolio link", At(today.AddDays(1), 16, 0), false, "Share the public project portfolio before the introductory call."),
            ],
            Appointments:
            [
                new("Introductory call", At(today.AddDays(3), 11, 0), At(today.AddDays(3), 11, 30), "Video call", "Meet the early careers partner."),
            ]),
        new(
            Slug: "bright-wattle-junior-software-developer",
            CompanyName: "Bright Wattle Studio",
            RoleTitle: "Junior Software Developer",
            Location: "Canberra ACT",
            WorkArrangement: "Hybrid",
            AppliedOn: today.AddDays(-9),
            Stage: PipelineStage.Applied,
            Outcome: ApplicationOutcome.Active,
            RetentionState: DemoRetentionState.Recent,
            DeletionScheduledAt: null,
            EmploymentType: "Full time",
            Salary: "$76,000 - $84,000 plus super",
            SourceSite: "Synthetic Graduate Network",
            JobReference: "DEMO-BWS-168",
            Summary: "A junior product-engineering role building accessible tools for fictional community services.",
            Description:
            [
                Section("The opportunity", "Learn alongside a small delivery team while contributing to well-tested web applications and clear technical documentation."),
            ],
            Contacts: [],
            Activity:
            [
                new("Applied", At(today.AddDays(-9), 13, 10), "Application submitted through the synthetic graduate portal."),
            ],
            Tasks: [],
            Appointments: []),
        new(
            Slug: "atlas-ember-junior-cybersecurity-analyst",
            CompanyName: "Atlas Ember Systems",
            RoleTitle: "Junior Cybersecurity Analyst",
            Location: "Sydney NSW",
            WorkArrangement: "On-site",
            AppliedOn: today.AddDays(-12),
            Stage: PipelineStage.Screening,
            Outcome: ApplicationOutcome.Active,
            RetentionState: DemoRetentionState.Recent,
            DeletionScheduledAt: null,
            EmploymentType: "Full time",
            Salary: "$82,000 package",
            SourceSite: "Synthetic Graduate Network",
            JobReference: "DEMO-AES-227",
            Summary: "An entry-level security operations role covering alert triage, investigation, and client reporting.",
            Description:
            [
                Section("The opportunity", "Work alongside incident responders and security engineers to investigate alerts and improve practical defensive controls."),
                Section("Your first six months", bullets:
                [
                    "Learn the team's investigation and evidence-handling workflow.",
                    "Triage supported alerts and document clear findings.",
                    "Contribute to tabletop exercises and security awareness material.",
                ]),
            ],
            Contacts:
            [
                new("Jordan Example", "Talent coordinator", "jordan.example@example.test", "No real person or inbox is represented."),
            ],
            Activity:
            [
                new("Applied", At(today.AddDays(-12), 9, 5), "Application submitted."),
                new("Phone call", At(today.AddDays(-7), 15, 45), "Completed a short eligibility screen.", IsEmployerResponse: true),
                new("Stage changed", At(today.AddDays(-7), 16, 10), "Moved from Applied to Screening."),
            ],
            Tasks:
            [
                new("Prepare two incident-response examples", At(today.AddDays(2), 18, 0), false, "Use university lab and personal project examples."),
            ],
            Appointments:
            [
                new("Interview", At(today.AddDays(4), 13, 30), At(today.AddDays(4), 14, 15), "Sydney office", "Structured behavioural and technical interview."),
            ]),
        new(
            Slug: "harbour-pine-business-systems-graduate",
            CompanyName: "Harbour & Pine Consulting",
            RoleTitle: "Business Systems Graduate",
            Location: "Hobart TAS",
            WorkArrangement: "Flexible hybrid",
            AppliedOn: today.AddDays(-20),
            Stage: PipelineStage.Offer,
            Outcome: ApplicationOutcome.Active,
            RetentionState: DemoRetentionState.Saved,
            DeletionScheduledAt: null,
            EmploymentType: "Graduate program",
            Salary: "$79,000 plus super",
            SourceSite: "Synthetic Careers Board",
            JobReference: "DEMO-HPC-284",
            Summary: "A fictional consulting graduate role demonstrating the offer stage in the active pipeline.",
            Description:
            [
                Section("The program", "Work with supported project teams to map business processes, improve internal systems, and communicate practical recommendations."),
            ],
            Contacts:
            [
                new("Morgan Example", "Graduate recruiter", "morgan.example@example.test", "Synthetic demonstration contact."),
            ],
            Activity:
            [
                new("Applied", At(today.AddDays(-20), 9, 25), "Application submitted."),
                new("Offer", At(today.AddDays(-2), 15, 30), "A written synthetic offer was received.", IsEmployerResponse: true),
            ],
            Tasks:
            [
                new("Review offer details", At(today.AddDays(2), 17, 0), false, "Compare the role, learning support, and proposed start date."),
            ],
            Appointments: []),
        new(
            Slug: "paper-kite-support-engineer",
            CompanyName: "Paper Kite Digital",
            RoleTitle: "Support Engineer",
            Location: "Melbourne VIC",
            WorkArrangement: "Remote within Australia",
            AppliedOn: today.AddDays(-29),
            Stage: PipelineStage.Assessment,
            Outcome: ApplicationOutcome.Active,
            RetentionState: DemoRetentionState.Recent,
            DeletionScheduledAt: null,
            EmploymentType: "Full time",
            Salary: "$72,000 - $79,000 plus super",
            SourceSite: "Synthetic Jobs Australia",
            JobReference: "DEMO-PKD-318",
            Summary: "A customer-focused technical role troubleshooting web products and improving support documentation.",
            Description:
            [
                Section("Why this role", "Combine technical problem solving with thoughtful customer communication in a distributed product team."),
                Section("Core responsibilities", bullets:
                [
                    "Reproduce customer issues and explain findings clearly.",
                    "Write internal troubleshooting guides and customer-facing articles.",
                    "Partner with engineering when a product fix is required.",
                ]),
            ],
            Contacts: [],
            Activity:
            [
                new("Applied", At(today.AddDays(-29), 12, 35), "Application submitted."),
                new("Email", At(today.AddDays(-21), 10, 0), "Received a take-home troubleshooting exercise.", IsEmployerResponse: true),
                new("Assessment", At(today.AddDays(-19), 17, 40), "Submitted the troubleshooting exercise."),
            ],
            Tasks:
            [
                new("Follow up on assessment", At(today.AddDays(-1), 9, 0), false, "Send a brief and polite follow-up."),
            ],
            Appointments: []),
        new(
            Slug: "meridian-orchard-technology-graduate",
            CompanyName: "Meridian Orchard Health",
            RoleTitle: "Technology Graduate",
            Location: "Brisbane QLD",
            WorkArrangement: "Hybrid",
            AppliedOn: today.AddDays(-47),
            Stage: PipelineStage.Interview,
            Outcome: ApplicationOutcome.Active,
            RetentionState: DemoRetentionState.Saved,
            DeletionScheduledAt: null,
            EmploymentType: "Graduate program",
            Salary: "$75,000 plus super",
            SourceSite: "Synthetic University Careers",
            JobReference: "DEMO-MOH-441",
            Summary: "A rotational graduate program spanning software delivery, data, and service operations.",
            Description:
            [
                Section("The program", "Complete three supported rotations while contributing to digital health products used by fictional clinics."),
                Section("Program support", bullets:
                [
                    "A dedicated graduate mentor and rotation manager.",
                    "Monthly learning sessions and project showcases.",
                    "A permanent team placement after the final rotation.",
                ]),
            ],
            Contacts:
            [
                new("Casey Sample", "Graduate program coordinator", "casey.sample@example.test", "Synthetic demonstration contact."),
            ],
            Activity:
            [
                new("Applied", At(today.AddDays(-47), 8, 50), "Application submitted."),
                new("Assessment", At(today.AddDays(-31), 11, 30), "Completed the online assessment.", IsEmployerResponse: true),
                new("Interview", At(today.AddDays(-8), 10, 0), "Completed the panel interview."),
            ],
            Tasks: [],
            Appointments: []),
        new(
            Slug: "cobalt-finch-data-operations-associate",
            CompanyName: "Cobalt Finch Energy",
            RoleTitle: "Data Operations Associate",
            Location: "Adelaide SA",
            WorkArrangement: "Hybrid",
            AppliedOn: today.AddDays(-66),
            Stage: PipelineStage.Offer,
            Outcome: ApplicationOutcome.Accepted,
            RetentionState: DemoRetentionState.Saved,
            DeletionScheduledAt: null,
            EmploymentType: "Full time",
            Salary: "$80,000 plus super",
            SourceSite: "Synthetic Careers Board",
            JobReference: "DEMO-CFE-509",
            Summary: "A fictional successful application included to demonstrate offer and accepted states.",
            Description:
            [
                Section("The role", "Support reliable operational reporting and help product teams understand the quality of their data."),
                Section("Day to day", bullets:
                [
                    "Monitor scheduled data workflows and investigate failures.",
                    "Improve data-quality checks with the analytics team.",
                    "Keep runbooks current and easy to follow.",
                ]),
            ],
            Contacts: [],
            Activity:
            [
                new("Applied", At(today.AddDays(-66), 13, 20), "Application submitted."),
                new("Offer", At(today.AddDays(-6), 16, 0), "Received a written synthetic offer.", IsEmployerResponse: true),
                new("Outcome changed", At(today.AddDays(-5), 9, 30), "Offer accepted."),
            ],
            Tasks: [],
            Appointments: []),
        new(
            Slug: "lumen-parcel-service-desk-analyst",
            CompanyName: "Lumen Parcel Cooperative",
            RoleTitle: "Service Desk Analyst",
            Location: "Perth WA",
            WorkArrangement: "On-site",
            AppliedOn: today.AddMonths(-3).AddDays(-3),
            Stage: PipelineStage.Applied,
            Outcome: ApplicationOutcome.Active,
            RetentionState: DemoRetentionState.DeletionScheduled,
            DeletionScheduledAt: At(today.AddDays(9), 17, 0),
            EmploymentType: "Full time",
            Salary: "$34 - $38 an hour",
            SourceSite: "Synthetic Jobs Australia",
            JobReference: "DEMO-LPC-612",
            Summary: "An older unsaved record demonstrating the visible 14-day retention grace period.",
            Description:
            [
                Section("About the team", "Provide first-line support for a fictional logistics cooperative with clear escalation paths and practical documentation."),
                Section("What you will bring", bullets:
                [
                    "Patient communication and structured troubleshooting.",
                    "Confidence supporting common desktop applications.",
                    "A willingness to learn the team's service-management tools.",
                ]),
            ],
            Contacts: [],
            Activity:
            [
                new("Applied", At(today.AddMonths(-3).AddDays(-3), 15, 10), "Application submitted."),
                new("Retention", At(today, 8, 0), "A synthetic deletion grace period was scheduled."),
            ],
            Tasks: [],
            Appointments: []),
    ];

    private static DemoDescriptionSection Section(
        string heading,
        string? paragraph = null,
        IReadOnlyList<string>? bullets = null) =>
        new(
            heading,
            paragraph is null ? [] : [paragraph],
            bullets ?? []);

    private static DateTimeOffset At(DateOnly date, int hour, int minute) =>
        new(date.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(8));
}
