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
        Assert.Equal(3, result.RetentionMonths);
        Assert.Equal(14, result.DeletionGraceDays);
        Assert.Equal(1, result.SavedCount);
        Assert.Equal(1, result.RecentCount);
        Assert.Equal(1, result.EligibleCount);
        Assert.Equal(1, result.DeletionScheduledCount);
        Assert.NotNull(result.LastRun);
        Assert.False(result.LastRun.Succeeded);
        Assert.Equal("22023", result.LastRun.ErrorCode);
    }

    [Fact]
    public async Task UpdatingPreferences_RecalculatesOwnedSchedulesFromNow()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"retention-preferences-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var owner = User("retention-preferences-owner");
        var other = User("retention-preferences-other");
        var now = new DateTimeOffset(2026, 8, 14, 4, 0, 0, TimeSpan.Zero);
        var ownerOld = Application(
            owner.Id,
            "Owner old role",
            new DateOnly(2026, 7, 1),
            false,
            now.AddDays(12));
        var ownerRecent = Application(
            owner.Id,
            "Owner recent role",
            new DateOnly(2026, 8, 1),
            false,
            null);
        var ownerSaved = Application(
            owner.Id,
            "Owner saved role",
            new DateOnly(2026, 1, 1),
            true,
            null);
        var otherOld = Application(
            other.Id,
            "Other old role",
            new DateOnly(2026, 1, 1),
            false,
            now.AddDays(11));
        database.Users.AddRange(owner, other);
        database.JobApplications.AddRange(ownerOld, ownerRecent, ownerSaved, otherOld);
        await database.SaveChangesAsync();
        var service = new RetentionOperationsService(
            database,
            new FixedCurrentUser(owner.Id),
            new FixedTimeProvider(now));

        var updated = await service.UpdateSettingsAsync(1, 3);

        Assert.True(updated);
        Assert.Equal(1, owner.RetentionMonths);
        Assert.Equal(3, owner.DeletionGraceDays);
        Assert.Equal(now.AddDays(3), ownerOld.DeletionScheduledAt);
        Assert.Null(ownerRecent.DeletionScheduledAt);
        Assert.Null(ownerSaved.DeletionScheduledAt);
        Assert.Equal(now.AddDays(11), otherOld.DeletionScheduledAt);
        Assert.Equal(3, other.RetentionMonths);
        Assert.Equal(14, other.DeletionGraceDays);
    }

    [Fact]
    public async Task UpdatingPreferences_RejectsValuesOutsideTheExposedChoices()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"retention-preferences-invalid-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var owner = User("retention-preferences-invalid-owner");
        database.Users.Add(owner);
        await database.SaveChangesAsync();
        var service = new RetentionOperationsService(
            database,
            new FixedCurrentUser(owner.Id),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var updated = await service.UpdateSettingsAsync(4, 2);

        Assert.False(updated);
        Assert.Equal(3, owner.RetentionMonths);
        Assert.Equal(14, owner.DeletionGraceDays);
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
