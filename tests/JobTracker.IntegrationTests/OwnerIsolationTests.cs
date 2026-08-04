using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using JobTracker.Web.Applications;
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

    [Fact]
    public async Task MilestoneThreeServices_RejectCrossOwnerRecordsAndCompanyLinks()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"milestone-three-owner-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var ownerA = User("owner-a-m3");
        var ownerB = User("owner-b-m3");
        var companyB = new Company
        {
            OwnerId = ownerB.Id,
            Name = "Synthetic Other Owner Company",
        };
        var applicationB = new JobApplication
        {
            OwnerId = ownerB.Id,
            CompanyId = companyB.Id,
            RoleTitle = "Other Owner Role",
            AppliedOn = new DateOnly(2026, 8, 1),
        };
        database.Users.AddRange(ownerA, ownerB);
        database.Companies.Add(companyB);
        database.JobApplications.Add(applicationB);
        await database.SaveChangesAsync();

        var currentUser = new FixedCurrentUser(ownerA.Id);
        var applicationService = new ApplicationTrackerService(
            database,
            currentUser,
            TimeProvider.System);
        var companyService = new CompanyTrackerService(database, currentUser);
        var input = new ApplicationInput(
            companyB.Id,
            "Forged company attempt",
            new DateOnly(2026, 8, 4),
            null,
            null,
            false);

        Assert.Null(await applicationService.FindAsync(applicationB.Id));
        Assert.Equal(
            ApplicationWriteResult.NotFound,
            await applicationService.UpdateAsync(applicationB.Id, input));
        Assert.Equal(
            ApplicationWriteResult.NotFound,
            await applicationService.SetSavedForeverAsync(applicationB.Id, true));
        Assert.Equal(
            ApplicationWriteResult.NotFound,
            await applicationService.DeleteAsync(applicationB.Id));

        var createResult = await applicationService.CreateAsync(input);
        Assert.Equal(ApplicationWriteResult.InvalidCompany, createResult.Result);
        Assert.Null(createResult.Id);
        Assert.Null(await companyService.FindAsync(companyB.Id));
        Assert.Equal(CompanyWriteResult.NotFound, await companyService.DeleteAsync(companyB.Id));
        Assert.Single(database.JobApplications);
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
