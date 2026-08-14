using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class RetentionOperationsServiceTests
{
    [Fact]
    public async Task Settings_CountOnlyOwnedStates_AndExposePrivacySafeRunHealth()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"retention-settings-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var owner = User("retention-settings-owner");
        var other = User("retention-settings-other");
        database.Users.AddRange(owner, other);
        database.JobApplications.AddRange(
            Application(owner.Id, "Saved private role", new DateOnly(2026, 1, 1), true, null),
            Application(owner.Id, "Recent private role", new DateOnly(2026, 8, 1), false, null),
            Application(owner.Id, "Awaiting schedule private role", new DateOnly(2026, 5, 14), false, null),
            Application(owner.Id, "Deletion scheduled private role", new DateOnly(2026, 4, 1), false,
                new DateTimeOffset(2026, 8, 25, 4, 0, 0, TimeSpan.Zero)),
            Application(other.Id, "Other owner's private role", new DateOnly(2026, 4, 1), false,
                new DateTimeOffset(2026, 8, 24, 4, 0, 0, TimeSpan.Zero)));
        database.RetentionRuns.Add(new RetentionRun
        {
            StartedAt = new DateTimeOffset(2026, 8, 14, 3, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 8, 14, 3, 0, 1, TimeSpan.Zero),
            Succeeded = false,
            ErrorCode = "22023",
        });
        await database.SaveChangesAsync();
        var service = new RetentionOperationsService(
            database,
            new FixedCurrentUser(owner.Id),
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 14, 4, 0, 0, TimeSpan.Zero)));

        var result = await service.GetSettingsAsync();

        Assert.Equal("Australia/Perth", result.TimeZoneId);
        Assert.Equal(1, result.SavedCount);
        Assert.Equal(1, result.RecentCount);
        Assert.Equal(1, result.EligibleCount);
        Assert.Equal(1, result.DeletionScheduledCount);
        Assert.NotNull(result.LastRun);
        Assert.False(result.LastRun.Succeeded);
        Assert.Equal("22023", result.LastRun.ErrorCode);
    }

    private static JobApplication Application(
        string ownerId,
        string title,
        DateOnly appliedOn,
        bool saved,
        DateTimeOffset? scheduledAt) => new()
        {
            OwnerId = ownerId,
            RoleTitle = title,
            AppliedOn = appliedOn,
            IsSavedForever = saved,
            DeletionScheduledAt = scheduledAt,
        };

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = $"{id}@example.test",
        Email = $"{id}@example.test",
        DisplayName = "Synthetic Retention User",
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
