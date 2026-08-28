using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class BulkApplicationServiceTests
{
    [Fact]
    public async Task BulkActions_AreOwnerScoped_AndAppendHistory()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"bulk-actions-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var owner = User("bulk-owner");
        var other = User("bulk-other");
        var first = Application(owner.Id, "First owned role", PipelineStage.Applied);
        var second = Application(owner.Id, "Second owned role", PipelineStage.Screening);
        var privateOther = Application(other.Id, "Other private role", PipelineStage.Applied);
        database.Users.AddRange(owner, other);
        database.JobApplications.AddRange(first, second, privateOther);
        await database.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 12, 14, 8, 0, 0, TimeSpan.Zero);
        var service = new ApplicationTrackerService(
            database,
            new FixedCurrentUser(owner.Id),
            new FixedTimeProvider(now),
            new ApplicationQuotaService(database));

        var stageResult = await service.BulkChangeStageAsync(
            [first.Id, second.Id, privateOther.Id],
            PipelineStage.Screening);

        Assert.Equal(2, stageResult.MatchedCount);
        Assert.Equal(1, stageResult.ChangedCount);
        Assert.Equal(PipelineStage.Screening, first.Stage);
        Assert.Equal(PipelineStage.Screening, second.Stage);
        Assert.Equal(PipelineStage.Applied, privateOther.Stage);
        var stageHistory = Assert.Single(database.StatusHistory);
        Assert.Equal(first.Id, stageHistory.JobApplicationId);
        Assert.Equal(PipelineStage.Applied, stageHistory.PreviousStage);
        Assert.Equal(PipelineStage.Screening, stageHistory.NewStage);
        Assert.Equal(now, stageHistory.EffectiveAt);

        var noteResult = await service.BulkAddNoteAsync(
            [first.Id, second.Id, privateOther.Id],
            "  Shared interview preparation note.  ");

        Assert.Equal(2, noteResult.MatchedCount);
        Assert.Equal(2, noteResult.ChangedCount);
        var notes = await database.StatusHistory
            .Where(item => item.NewStage == null)
            .ToListAsync();
        Assert.Equal(2, notes.Count);
        Assert.All(notes, item => Assert.Equal("Shared interview preparation note.", item.Note));
        Assert.DoesNotContain(notes, item => item.OwnerId == other.Id);

        first.DeletionScheduledAt = now.AddDays(2);
        await database.SaveChangesAsync();
        var saveResult = await service.BulkSetSavedForeverAsync(
            [first.Id, privateOther.Id],
            true);
        Assert.Equal(1, saveResult.MatchedCount);
        Assert.Equal(1, saveResult.ChangedCount);
        Assert.True(first.IsSavedForever);
        Assert.Null(first.DeletionScheduledAt);
        Assert.False(privateOther.IsSavedForever);

        var unsaveResult = await service.BulkSetSavedForeverAsync([first.Id], false);
        Assert.Equal(1, unsaveResult.MatchedCount);
        Assert.Equal(1, unsaveResult.ChangedCount);
        Assert.False(first.IsSavedForever);
        Assert.Equal(now.AddDays(RetentionPolicy.GracePeriodDays), first.DeletionScheduledAt);

        var selected = await service.FindSelectedAsync([first.Id, privateOther.Id]);
        Assert.Equal(first.Id, Assert.Single(selected).Id);

        var deleteResult = await service.BulkDeleteAsync([second.Id, privateOther.Id]);

        Assert.Equal(1, deleteResult.MatchedCount);
        Assert.Equal(1, deleteResult.ChangedCount);
        Assert.DoesNotContain(database.JobApplications, item => item.Id == second.Id);
        Assert.Contains(database.JobApplications, item => item.Id == privateOther.Id);
    }

    private static JobApplication Application(
        string ownerId,
        string roleTitle,
        PipelineStage stage) => new()
        {
            OwnerId = ownerId,
            RoleTitle = roleTitle,
            AppliedOn = new DateOnly(2026, 8, 1),
            Stage = stage,
        };

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = $"{id}@example.test",
        Email = $"{id}@example.test",
        DisplayName = "Synthetic Bulk User",
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
