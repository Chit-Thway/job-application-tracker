using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class ApplicationWorkflowServiceTests
{
    [Fact]
    public async Task StatusTransitions_AreAppendOnly_AndNoChangeIsRejected()
    {
        await using var database = Database("workflow-status");
        var user = User("workflow-status-owner");
        var application = Application(user.Id);
        database.Users.Add(user);
        database.JobApplications.Add(application);
        database.StatusHistory.Add(new StatusHistory
        {
            OwnerId = user.Id,
            JobApplicationId = application.Id,
            NewStage = PipelineStage.Applied,
            NewOutcome = ApplicationOutcome.Active,
            EffectiveAt = new DateTimeOffset(2026, 8, 1, 1, 0, 0, TimeSpan.Zero),
            Note = "Application created.",
        });
        await database.SaveChangesAsync();

        var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 9, 2, 0, 0, TimeSpan.Zero));
        var service = new ApplicationWorkflowService(
            database,
            new FixedCurrentUser(user.Id),
            time);

        Assert.Equal(
            WorkflowWriteResult.InvalidTransition,
            await service.TransitionAsync(
                application.Id,
                new StatusTransitionInput(
                    PipelineStage.Applied,
                    ApplicationOutcome.Ghosted,
                    "Too early to confirm ghosting.")));
        Assert.Equal(
            WorkflowWriteResult.Success,
            await service.TransitionAsync(
                application.Id,
                new StatusTransitionInput(
                    PipelineStage.Screening,
                    ApplicationOutcome.Active,
                    "Phone screen booked.")));
        time.UtcNow = time.UtcNow.AddHours(1);
        Assert.Equal(
            WorkflowWriteResult.Success,
            await service.TransitionAsync(
                application.Id,
                new StatusTransitionInput(
                    PipelineStage.Screening,
                    ApplicationOutcome.Rejected,
                    "Employer closed the role.")));
        Assert.Equal(
            WorkflowWriteResult.InvalidTransition,
            await service.TransitionAsync(
                application.Id,
                new StatusTransitionInput(
                    PipelineStage.Screening,
                    ApplicationOutcome.Rejected,
                    null)));

        var saved = await database.JobApplications.SingleAsync();
        Assert.Equal(PipelineStage.Screening, saved.Stage);
        Assert.Equal(ApplicationOutcome.Rejected, saved.Outcome);
        var history = await database.StatusHistory.OrderBy(item => item.EffectiveAt).ToListAsync();
        Assert.Equal(3, history.Count);
        Assert.Equal(PipelineStage.Applied, history[1].PreviousStage);
        Assert.Equal(PipelineStage.Screening, history[1].NewStage);
        Assert.Null(history[1].NewOutcome);
        Assert.Equal(ApplicationOutcome.Active, history[2].PreviousOutcome);
        Assert.Equal(ApplicationOutcome.Rejected, history[2].NewOutcome);
        Assert.Null(history[2].NewStage);
    }

    [Fact]
    public async Task OldUnansweredApplication_CanBeCorrectedFromWithdrawnBackToGhosted()
    {
        await using var database = Database("workflow-ghosted-correction");
        var user = User("workflow-ghosted-correction-owner");
        var application = Application(user.Id);
        application.AppliedOn = new DateOnly(2026, 5, 14);
        application.Outcome = ApplicationOutcome.Withdrawn;
        database.Users.Add(user);
        database.JobApplications.Add(application);
        await database.SaveChangesAsync();
        var service = new ApplicationWorkflowService(
            database,
            new FixedCurrentUser(user.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 16, 4, 0, 0, TimeSpan.Zero)));

        var details = await service.FindAsync(application.Id);

        Assert.NotNull(details);
        Assert.True(details.CanConfirmGhosted);
        Assert.Equal(
            WorkflowWriteResult.Success,
            await service.TransitionAsync(
                application.Id,
                new StatusTransitionInput(
                    PipelineStage.Interview,
                    ApplicationOutcome.Ghosted,
                    "Corrected the earlier outcome.")));
        Assert.Equal(ApplicationOutcome.Ghosted, application.Outcome);
        Assert.Equal(PipelineStage.Interview, application.Stage);
    }

    [Fact]
    public async Task EmployerResponse_IsDerivedFromCorrectableInteractions()
    {
        await using var database = Database("workflow-response");
        var user = User("workflow-response-owner");
        var application = Application(user.Id);
        database.Users.Add(user);
        database.JobApplications.Add(application);
        await database.SaveChangesAsync();
        var service = Service(database, user.Id);

        var first = await service.AddInteractionAsync(
            application.Id,
            new InteractionInput(
                null,
                InteractionType.Email,
                new DateTime(2026, 8, 5, 9, 0, 0),
                false,
                "Sent a follow-up."));
        var second = await service.AddInteractionAsync(
            application.Id,
            new InteractionInput(
                null,
                InteractionType.Call,
                new DateTime(2026, 8, 6, 10, 30, 0),
                true,
                "Recruiter called."));

        var details = await service.FindAsync(application.Id);
        Assert.NotNull(details);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 6, 2, 30, 0, TimeSpan.Zero),
            details.FirstEmployerResponseAt);

        Assert.Equal(
            WorkflowWriteResult.Success,
            await service.UpdateInteractionAsync(
                application.Id,
                first.Id!.Value,
                new InteractionInput(
                    null,
                    InteractionType.Email,
                    new DateTime(2026, 8, 5, 9, 0, 0),
                    true,
                    "Employer replied by email.")));
        details = await service.FindAsync(application.Id);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 5, 1, 0, 0, TimeSpan.Zero),
            details!.FirstEmployerResponseAt);

        Assert.Equal(
            WorkflowWriteResult.Success,
            await service.UpdateInteractionAsync(
                application.Id,
                first.Id.Value,
                new InteractionInput(
                    null,
                    InteractionType.Email,
                    new DateTime(2026, 8, 5, 9, 0, 0),
                    false,
                    "Correction: this was outbound.")));
        details = await service.FindAsync(application.Id);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 6, 2, 30, 0, TimeSpan.Zero),
            details!.FirstEmployerResponseAt);
        Assert.NotNull(second.Id);
    }

    [Fact]
    public async Task TasksAndAppointments_UseOwnerTimezoneAndValidateTimeRange()
    {
        await using var database = Database("workflow-time");
        var user = User("workflow-time-owner");
        var application = Application(user.Id);
        database.Users.Add(user);
        database.JobApplications.Add(application);
        await database.SaveChangesAsync();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 8, 9, 3, 0, 0, TimeSpan.Zero));
        var service = new ApplicationWorkflowService(
            database,
            new FixedCurrentUser(user.Id),
            time);

        var task = await service.AddTaskAsync(
            application.Id,
            new WorkflowTaskInput(
                "Send portfolio",
                new DateTime(2026, 8, 10, 9, 30, 0),
                "Attach the updated PDF."));
        Assert.Equal(WorkflowWriteResult.Success, task.Result);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 10, 1, 30, 0, TimeSpan.Zero),
            (await database.Tasks.SingleAsync()).DueAt);

        Assert.Equal(
            WorkflowWriteResult.Success,
            await service.SetTaskCompletedAsync(application.Id, task.Id!.Value, true));
        Assert.Equal(time.UtcNow, (await database.Tasks.SingleAsync()).CompletedAt);

        var appointment = await service.AddAppointmentAsync(
            application.Id,
            new WorkflowAppointmentInput(
                AppointmentType.Interview,
                new DateTime(2026, 8, 11, 14, 0, 0),
                new DateTime(2026, 8, 11, 15, 0, 0),
                "https://example.test/interview",
                "Technical interview."));
        Assert.Equal(WorkflowWriteResult.Success, appointment.Result);
        var stored = await database.Appointments.SingleAsync();
        Assert.Equal("Australia/Perth", stored.TimeZoneId);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 11, 6, 0, 0, TimeSpan.Zero),
            stored.StartsAt);

        var invalid = await service.AddAppointmentAsync(
            application.Id,
            new WorkflowAppointmentInput(
                AppointmentType.Call,
                new DateTime(2026, 8, 12, 10, 0, 0),
                new DateTime(2026, 8, 12, 9, 0, 0),
                null,
                null));
        Assert.Equal(WorkflowWriteResult.InvalidLocalTime, invalid.Result);
        Assert.Single(database.Appointments);
    }

    [Fact]
    public async Task CrossOwnerApplicationsAndContacts_AreRejected()
    {
        await using var database = Database("workflow-owner");
        var ownerA = User("workflow-owner-a");
        var ownerB = User("workflow-owner-b");
        var applicationA = Application(ownerA.Id);
        var applicationB = Application(ownerB.Id);
        var contactB = new Contact
        {
            OwnerId = ownerB.Id,
            JobApplicationId = applicationB.Id,
            Name = "Other owner's recruiter",
        };
        database.Users.AddRange(ownerA, ownerB);
        database.JobApplications.AddRange(applicationA, applicationB);
        database.Contacts.Add(contactB);
        await database.SaveChangesAsync();
        var service = Service(database, ownerA.Id);

        Assert.Null(await service.FindAsync(applicationB.Id));
        Assert.Equal(
            WorkflowWriteResult.NotFound,
            await service.TransitionAsync(
                applicationB.Id,
                new StatusTransitionInput(
                    PipelineStage.Interview,
                    ApplicationOutcome.Active,
                    null)));
        var interaction = await service.AddInteractionAsync(
            applicationA.Id,
            new InteractionInput(
                contactB.Id,
                InteractionType.Email,
                new DateTime(2026, 8, 9, 9, 0, 0),
                true,
                null));
        Assert.Equal(WorkflowWriteResult.InvalidRelationship, interaction.Result);
        Assert.Empty(database.Interactions);
    }

    private static ApplicationWorkflowService Service(
        ApplicationDbContext database,
        string userId) =>
        new(database, new FixedCurrentUser(userId), TimeProvider.System);

    private static ApplicationDbContext Database(string prefix)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{prefix}-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static JobApplication Application(string ownerId) => new()
    {
        OwnerId = ownerId,
        RoleTitle = "Synthetic Support Engineer",
        AppliedOn = new DateOnly(2026, 8, 4),
    };

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = $"{id}@example.test",
        Email = $"{id}@example.test",
        DisplayName = "Synthetic Workflow User",
        TimeZoneId = "Australia/Perth",
    };

    private sealed class FixedCurrentUser(string userId) : ICurrentUserContext
    {
        public string? UserId { get; } = userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
