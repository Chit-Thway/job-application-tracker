using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class OwnerIsolationTests
{
    [Fact]
    public async Task OtherOwnersApplication_CannotBeReadUpdatedDeletedOrUsedForTask()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"owner-isolation-{Guid.NewGuid()}")
            .Options;

        await using var database = new ApplicationDbContext(options);
        var ownerA = User("owner-a");
        var ownerB = User("owner-b");
        var applicationB = new JobApplication
        {
            OwnerId = ownerB.Id,
            RoleTitle = "Synthetic Engineer",
            AppliedOn = new DateOnly(2026, 8, 1),
        };
        database.Users.AddRange(ownerA, ownerB);
        database.JobApplications.Add(applicationB);
        await database.SaveChangesAsync();

        var service = new OwnedApplicationService(database, new FixedCurrentUser(ownerA.Id));

        Assert.Null(await service.FindAsync(applicationB.Id));
        Assert.False(await service.RenameAsync(applicationB.Id, "Changed"));
        Assert.False(await service.DeleteAsync(applicationB.Id));
        Assert.Null(await service.AddTaskAsync(applicationB.Id, "Cross-owner task"));

        var unchanged = await database.JobApplications.SingleAsync();
        Assert.Equal("Synthetic Engineer", unchanged.RoleTitle);
        Assert.Empty(database.Tasks);
    }

    [Fact]
    public void DependentRelationships_IncludeOwnerInForeignKey()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"owner-model-{Guid.NewGuid()}")
            .Options;
        using var database = new ApplicationDbContext(options);

        var taskEntity = database.Model.FindEntityType(typeof(TaskItem));
        Assert.NotNull(taskEntity);
        var applicationForeignKey = taskEntity.GetForeignKeys().Single(key =>
            key.PrincipalEntityType.ClrType == typeof(JobApplication));

        Assert.Equal(
            [nameof(TaskItem.JobApplicationId), nameof(TaskItem.OwnerId)],
            applicationForeignKey.Properties.Select(property => property.Name));
    }

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = $"{id}@example.test",
        Email = $"{id}@example.test",
        DisplayName = "Synthetic Test User",
        TimeZoneId = "Australia/Perth",
    };

    private sealed class FixedCurrentUser(string userId) : ICurrentUserContext
    {
        public string? UserId { get; } = userId;
    }
}
