using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class ApplicationServiceTests
{
    [Fact]
    public async Task TierOneStopsAtTenApplications_WhileTierTwoIsUnlimited()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"application-quota-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("quota-owner");
        database.Users.Add(user);
        database.JobApplications.AddRange(Enumerable.Range(1, 10).Select(index =>
            new JobApplication
            {
                OwnerId = user.Id,
                RoleTitle = $"Existing role {index}",
                AppliedOn = new DateOnly(2026, 8, index),
            }));
        await database.SaveChangesAsync();
        var service = new ApplicationTrackerService(
            database,
            new FixedCurrentUser(user.Id),
            TimeProvider.System,
            new ApplicationQuotaService(database));

        var limited = await service.CreateAsync(Input("Blocked Tier 1 role"));

        Assert.Equal(ApplicationWriteResult.LimitReached, limited.Result);
        Assert.Null(limited.Id);
        Assert.Equal(10, await database.JobApplications.CountAsync());
        Assert.Empty(database.StatusHistory);

        user.AccountTier = AccountTier.Tier2;
        await database.SaveChangesAsync();
        var unlimited = await service.CreateAsync(Input("Allowed Tier 2 role"));

        Assert.Equal(ApplicationWriteResult.Success, unlimited.Result);
        Assert.Equal(11, await database.JobApplications.CountAsync());
        Assert.Single(database.StatusHistory);
    }

    [Fact]
    public async Task Create_WritesInitialHistory_AndSavedStateCanChange()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"application-create-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("application-owner");
        var company = new Company
        {
            OwnerId = user.Id,
            Name = "Synthetic Harbour Labs",
            Location = "Perth, WA",
        };
        database.Users.Add(user);
        database.Companies.Add(company);
        await database.SaveChangesAsync();
        var service = new ApplicationTrackerService(
            database,
            new FixedCurrentUser(user.Id),
            TimeProvider.System,
            new ApplicationQuotaService(database));

        var created = await service.CreateAsync(new ApplicationInput(
            company.Id,
            "Platform Engineer",
            new DateOnly(2026, 8, 4),
            "https://example.test/jobs/platform",
            "https://careers.example.test/candidate/home",
            "About the role\n\nBuild reliable synthetic platforms.",
            "Synthetic notes",
            false));

        Assert.Equal(ApplicationWriteResult.Success, created.Result);
        Assert.NotNull(created.Id);
        var application = await database.JobApplications.SingleAsync();
        Assert.False(application.IsSavedForever);
        Assert.Equal("https://careers.example.test/candidate/home", application.ApplicationPortalUrl);
        Assert.Equal("About the role\n\nBuild reliable synthetic platforms.", application.DescriptionText);
        var history = await database.StatusHistory.SingleAsync();
        Assert.Equal(application.Id, history.JobApplicationId);
        Assert.Equal(PipelineStage.Applied, history.NewStage);
        Assert.Equal(ApplicationOutcome.Active, history.NewOutcome);
        Assert.Null(history.PreviousStage);

        application.DeletionScheduledAt = DateTimeOffset.UtcNow.AddDays(5);
        await database.SaveChangesAsync();
        Assert.Equal(
            ApplicationWriteResult.Success,
            await service.SetSavedForeverAsync(application.Id, true));
        Assert.True(application.IsSavedForever);
        Assert.Null(application.DeletionScheduledAt);

        Assert.Equal(
            ApplicationWriteResult.Success,
            await service.SetSavedForeverAsync(application.Id, false));
        Assert.False(application.IsSavedForever);
    }

    [Fact]
    public async Task CompanyDuplicatesAreRejected_AndLinkedCompanyCannotBeDeleted()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"company-duplicate-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("company-owner");
        database.Users.Add(user);
        await database.SaveChangesAsync();
        var currentUser = new FixedCurrentUser(user.Id);
        var companies = new CompanyTrackerService(database, currentUser);
        var applications = new ApplicationTrackerService(
            database,
            currentUser,
            TimeProvider.System,
            new ApplicationQuotaService(database));

        var first = await companies.CreateAsync(new CompanyInput(
            "Synthetic Northstar",
            "Perth, WA",
            null,
            null));
        var duplicate = await companies.CreateAsync(new CompanyInput(
            " synthetic northstar ",
            "perth, wa",
            null,
            null));

        Assert.Equal(CompanyWriteResult.Success, first.Result);
        Assert.Equal(CompanyWriteResult.Duplicate, duplicate.Result);
        Assert.Equal(first.Id, duplicate.Id);
        Assert.Single(database.Companies);

        var application = await applications.CreateAsync(new ApplicationInput(
            first.Id,
            "Support Engineer",
            new DateOnly(2026, 8, 4),
            null,
            null,
            null,
            null,
            false));
        Assert.Equal(ApplicationWriteResult.Success, application.Result);
        Assert.Equal(CompanyWriteResult.InUse, await companies.DeleteAsync(first.Id!.Value));
        Assert.Single(database.Companies);
    }

    [Fact]
    public async Task Search_FiltersByCompanySavedStateDatesAndText()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"application-search-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("search-owner");
        var company = new Company { OwnerId = user.Id, Name = "Synthetic Atlas" };
        database.Users.Add(user);
        database.Companies.Add(company);
        database.JobApplications.AddRange(
            new JobApplication
            {
                OwnerId = user.Id,
                CompanyId = company.Id,
                RoleTitle = "Cloud Engineer",
                AppliedOn = new DateOnly(2026, 8, 2),
                IsSavedForever = true,
            },
            new JobApplication
            {
                OwnerId = user.Id,
                RoleTitle = "Service Analyst",
                AppliedOn = new DateOnly(2026, 7, 1),
            });
        await database.SaveChangesAsync();
        var service = new ApplicationTrackerService(
            database,
            new FixedCurrentUser(user.Id),
            TimeProvider.System,
            new ApplicationQuotaService(database));

        var results = await service.SearchAsync(new ApplicationSearch(
            "atlas",
            company.Id,
            PipelineStage.Applied,
            ApplicationOutcome.Active,
            true,
            null,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            "newest"));

        var result = Assert.Single(results);
        Assert.Equal("Cloud Engineer", result.RoleTitle);
        Assert.Equal("Synthetic Atlas", result.CompanyName);
        Assert.True(result.IsSavedForever);

        var roleResults = await service.SearchAsync(new ApplicationSearch(
            "cloud engineer",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "newest"));
        Assert.Equal("Cloud Engineer", Assert.Single(roleResults).RoleTitle);
    }

    [Fact]
    public async Task OldUnsavedApplication_GetsFreshGrace_AndSavingCancelsIt()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"application-retention-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("retention-owner");
        database.Users.Add(user);
        await database.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);
        var service = new ApplicationTrackerService(
            database,
            new FixedCurrentUser(user.Id),
            new FixedTimeProvider(now),
            new ApplicationQuotaService(database));

        var created = await service.CreateAsync(new ApplicationInput(
            null,
            "Old unsaved role",
            new DateOnly(2026, 5, 14),
            null,
            null,
            null,
            null,
            false));

        var application = await database.JobApplications.SingleAsync();
        Assert.Equal(ApplicationWriteResult.Success, created.Result);
        Assert.Equal(now.AddDays(14), application.DeletionScheduledAt);

        Assert.Equal(
            ApplicationWriteResult.Success,
            await service.DismissDeletionWarningAsync(application.Id));
        Assert.Equal(now, application.DeletionWarningDismissedAt);

        Assert.Equal(
            ApplicationWriteResult.Success,
            await service.SetSavedForeverAsync(application.Id, true));
        Assert.True(application.IsSavedForever);
        Assert.Null(application.DeletionScheduledAt);
        Assert.Null(application.DeletionWarningDismissedAt);
        Assert.Equal(
            ApplicationWriteResult.InvalidRetentionState,
            await service.DismissDeletionWarningAsync(application.Id));

        Assert.Equal(
            ApplicationWriteResult.Success,
            await service.SetSavedForeverAsync(application.Id, false));
        Assert.False(application.IsSavedForever);
        Assert.Equal(now.AddDays(14), application.DeletionScheduledAt);
        Assert.Null(application.DeletionWarningDismissedAt);

        var deletionScheduled = await service.SearchAsync(new ApplicationSearch(
            null,
            null,
            null,
            null,
            null,
            true,
            null,
            null,
            "newest"));
        Assert.Equal(application.Id, Assert.Single(deletionScheduled).Id);
    }

    private static ApplicationUser User(string id) => new()
    {
        Id = id,
        UserName = $"{id}@example.test",
        Email = $"{id}@example.test",
        DisplayName = "Synthetic Test User",
        TimeZoneId = "Australia/Perth",
    };

    private static ApplicationInput Input(string roleTitle) => new(
        null,
        roleTitle,
        new DateOnly(2026, 8, 28),
        null,
        null,
        null,
        null,
        false);

    private sealed class FixedCurrentUser(string userId) : ICurrentUserContext
    {
        public string? UserId { get; } = userId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
