using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Snapshot_UsesWindowMetricsButKeepsAllOwnedActions()
    {
        await using var database = Database();
        var owner = User("dashboard-owner");
        var other = User("dashboard-other");
        database.Users.AddRange(owner, other);

        var june = Application(owner.Id, "June role", new DateOnly(2026, 6, 1));
        var july = Application(owner.Id, "July role", new DateOnly(2026, 7, 20));
        var august = Application(owner.Id, "August role", new DateOnly(2026, 8, 12));
        var savedMay = Application(owner.Id, "Saved May role", new DateOnly(2026, 5, 31));
        savedMay.IsSavedForever = true;
        var deletionScheduled = Application(owner.Id, "Deletion scheduled role", new DateOnly(2026, 4, 1));
        deletionScheduled.DeletionScheduledAt = new DateTimeOffset(2026, 8, 20, 4, 0, 0, TimeSpan.Zero);
        var otherApplication = Application(other.Id, "Other owner role", new DateOnly(2026, 8, 1));
        var otherDeletionScheduled = Application(other.Id, "Other private scheduled role", new DateOnly(2026, 4, 1));
        otherDeletionScheduled.DeletionScheduledAt = new DateTimeOffset(2026, 8, 19, 4, 0, 0, TimeSpan.Zero);
        database.JobApplications.AddRange(june, july, august, savedMay, deletionScheduled, otherApplication, otherDeletionScheduled);
        database.Interactions.AddRange(
            new Interaction
            {
                OwnerId = owner.Id,
                JobApplicationId = july.Id,
                Type = InteractionType.Email,
                OccurredAt = new DateTimeOffset(2026, 7, 25, 2, 0, 0, TimeSpan.Zero),
                IsEmployerResponse = true,
            },
            new Interaction
            {
                OwnerId = other.Id,
                JobApplicationId = otherApplication.Id,
                Type = InteractionType.Email,
                OccurredAt = new DateTimeOffset(2026, 8, 2, 2, 0, 0, TimeSpan.Zero),
                IsEmployerResponse = true,
            });
        database.Tasks.Add(new TaskItem
        {
            OwnerId = owner.Id,
            JobApplicationId = savedMay.Id,
            Title = "Follow up on saved record",
            DueAt = new DateTimeOffset(2026, 8, 11, 8, 0, 0, TimeSpan.Zero),
        });
        database.Appointments.AddRange(
            new Appointment
            {
                OwnerId = owner.Id,
                JobApplicationId = july.Id,
                Type = AppointmentType.Interview,
                StartsAt = new DateTimeOffset(2026, 8, 15, 1, 0, 0, TimeSpan.Zero),
                EndsAt = new DateTimeOffset(2026, 8, 15, 2, 0, 0, TimeSpan.Zero),
                TimeZoneId = "Australia/Perth",
            },
            new Appointment
            {
                OwnerId = other.Id,
                JobApplicationId = otherApplication.Id,
                Type = AppointmentType.Interview,
                StartsAt = new DateTimeOffset(2026, 8, 15, 1, 0, 0, TimeSpan.Zero),
                EndsAt = new DateTimeOffset(2026, 8, 15, 2, 0, 0, TimeSpan.Zero),
                TimeZoneId = "Australia/Perth",
            });
        await database.SaveChangesAsync();

        var service = new DashboardService(
            database,
            new FixedCurrentUser(owner.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 12, 4, 0, 0, TimeSpan.Zero)));
        var snapshot = await service.GetAsync();

        Assert.Equal(new DateOnly(2026, 6, 1), snapshot.Window.StartsOn);
        Assert.Equal(new DateOnly(2026, 8, 31), snapshot.Window.EndsOn);
        Assert.Equal(3, snapshot.ApplicationCount);
        Assert.Equal(1, snapshot.ResponseCount);
        Assert.Equal(1, snapshot.InterviewCount);
        Assert.DoesNotContain(snapshot.RecentApplications, item => item.RoleTitle == savedMay.RoleTitle);
        Assert.DoesNotContain(snapshot.RecentApplications, item => item.RoleTitle == otherApplication.RoleTitle);
        Assert.Contains(snapshot.OpenTasks, item => item.RoleTitle == savedMay.RoleTitle && item.IsOverdue);
        Assert.Equal(deletionScheduled.Id, Assert.Single(snapshot.DeletionScheduled).ApplicationId);
        Assert.DoesNotContain(snapshot.DeletionScheduled, item => item.RoleTitle == otherDeletionScheduled.RoleTitle);
        Assert.Equal([1, 1, 1], snapshot.Months.Select(item => item.ApplicationCount));
    }

    [Fact]
    public async Task Snapshot_GhostingAttentionRequiresNoResponseAndActiveOutcome()
    {
        await using var database = Database();
        var owner = User("ghosting-owner");
        database.Users.Add(owner);
        var warning = Application(owner.Id, "Warning role", new DateOnly(2026, 7, 25));
        var confirm = Application(owner.Id, "Confirm role", new DateOnly(2026, 7, 1));
        var responded = Application(owner.Id, "Responded role", new DateOnly(2026, 6, 20));
        database.JobApplications.AddRange(warning, confirm, responded);
        database.Interactions.Add(new Interaction
        {
            OwnerId = owner.Id,
            JobApplicationId = responded.Id,
            Type = InteractionType.Email,
            OccurredAt = new DateTimeOffset(2026, 6, 25, 1, 0, 0, TimeSpan.Zero),
            IsEmployerResponse = true,
        });
        await database.SaveChangesAsync();

        var service = new DashboardService(
            database,
            new FixedCurrentUser(owner.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 12, 4, 0, 0, TimeSpan.Zero)));
        var snapshot = await service.GetAsync();

        Assert.Contains(snapshot.GhostingAttention, item =>
            item.ApplicationId == warning.Id && item.Kind == GhostingAttentionKind.FollowUp);
        Assert.Contains(snapshot.GhostingAttention, item =>
            item.ApplicationId == confirm.Id && item.Kind == GhostingAttentionKind.ConfirmGhosted);
        Assert.DoesNotContain(snapshot.GhostingAttention, item => item.ApplicationId == responded.Id);
        Assert.True(await service.CanConfirmGhostedAsync(confirm.Id));
        Assert.False(await service.CanConfirmGhostedAsync(responded.Id));
    }

    private static ApplicationDbContext Database()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"dashboard-{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static JobApplication Application(string ownerId, string title, DateOnly appliedOn) => new()
    {
        OwnerId = ownerId,
        RoleTitle = title,
        AppliedOn = appliedOn,
    };

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = $"{id}@example.test",
        Email = $"{id}@example.test",
        DisplayName = "Synthetic Dashboard User",
        TimeZoneId = "Australia/Perth",
    };

    private sealed class FixedCurrentUser(string userId) : ICurrentUserContext
    {
        public string? UserId { get; } = userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
