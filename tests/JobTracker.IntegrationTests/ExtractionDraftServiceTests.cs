using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Extraction;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class ExtractionDraftServiceTests
{
    [Fact]
    public async Task ExpiredDraft_CannotCreateAnApplication()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"expired-extraction-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("expired-draft-owner");
        database.Users.Add(user);
        await database.SaveChangesAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
        var service = Service(database, user.Id, time);
        var draftId = await service.CreatePastedTextDraftAsync(
            "Job Title: Test Engineer\nCompany: Synthetic Expiry Labs",
            new DateOnly(2026, 8, 4));

        time.Advance(TimeSpan.FromHours(25));
        var completion = await service.CompleteAsync(draftId, ValidInput());

        Assert.Equal(ExtractionDraftResult.Expired, completion.Result);
        Assert.Null(completion.ApplicationId);
        Assert.Empty(database.ExtractionDrafts);
        Assert.Empty(database.JobApplications);
    }

    [Fact]
    public async Task DraftsAreOwnerScoped_ForReadCompleteAndCancel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"owned-extraction-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var ownerA = User("extraction-owner-a");
        var ownerB = User("extraction-owner-b");
        database.Users.AddRange(ownerA, ownerB);
        await database.SaveChangesAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
        var serviceB = Service(database, ownerB.Id, time);
        var draftId = await serviceB.CreatePastedTextDraftAsync(
            "Job Title: Private Engineer\nCompany: Synthetic Private Labs",
            new DateOnly(2026, 8, 4));
        var serviceA = Service(database, ownerA.Id, time);

        Assert.Null(await serviceA.FindReviewAsync(draftId));
        Assert.Equal(
            ExtractionDraftResult.NotFound,
            (await serviceA.CompleteAsync(draftId, ValidInput())).Result);
        Assert.Equal(ExtractionDraftResult.NotFound, await serviceA.CancelAsync(draftId));
        Assert.Single(database.ExtractionDrafts);
        Assert.Empty(database.JobApplications);
    }

    [Fact]
    public async Task ConfirmedDraft_ReusesMatchingOwnedCompany()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"company-extraction-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("extraction-company-owner");
        var company = new Company
        {
            OwnerId = user.Id,
            Name = "Synthetic Existing Labs",
            Location = "Perth, WA",
        };
        database.Users.Add(user);
        database.Companies.Add(company);
        await database.SaveChangesAsync();
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 4, 3, 0, 0, TimeSpan.Zero));
        var service = Service(database, user.Id, time);
        var draftId = await service.CreatePastedTextDraftAsync(
            "Job Title: Test Engineer\nCompany: Synthetic Existing Labs\nLocation: Perth, WA",
            new DateOnly(2026, 8, 4));
        var input = ValidInput() with
        {
            CompanyName = " synthetic existing labs ",
            CompanyLocation = "perth, wa",
        };

        var completion = await service.CompleteAsync(draftId, input);

        Assert.Equal(ExtractionDraftResult.Success, completion.Result);
        Assert.Single(database.Companies);
        Assert.Equal(company.Id, (await database.JobApplications.SingleAsync()).CompanyId);
    }

    [Fact]
    public async Task ConfirmedOldUnsavedDraft_ReceivesFullGracePeriod()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"old-extraction-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("old-extraction-owner");
        database.Users.Add(user);
        await database.SaveChangesAsync();
        var now = new DateTimeOffset(2026, 8, 14, 5, 0, 0, TimeSpan.Zero);
        var time = new MutableTimeProvider(now);
        var service = Service(database, user.Id, time);
        var draftId = await service.CreatePastedTextDraftAsync(
            "Job Title: Old Test Engineer\nCompany: Synthetic Retention Labs",
            new DateOnly(2026, 5, 14));

        var completion = await service.CompleteAsync(
            draftId,
            ValidInput() with { AppliedOn = new DateOnly(2026, 5, 14) });

        Assert.Equal(ExtractionDraftResult.Success, completion.Result);
        Assert.Equal(
            now.AddDays(RetentionPolicy.GracePeriodDays),
            (await database.JobApplications.SingleAsync()).DeletionScheduledAt);
    }

    private static ExtractionDraftService Service(
        ApplicationDbContext database,
        string userId,
        TimeProvider timeProvider) =>
        new(database, new FixedCurrentUser(userId), timeProvider, new PastedJobTextExtractor());

    private static ExtractionReviewInput ValidInput() => new(
        "Reviewed Test Engineer",
        "Synthetic Review Company",
        "Perth, WA",
        new DateOnly(2026, 8, 4),
        "Hybrid",
        "https://example.test/jobs/reviewed",
        "Synthetic Careers",
        "REF-1",
        "$100,000",
        "Full-time",
        new DateOnly(2026, 8, 30),
        "Morgan Example",
        "morgan@example.test",
        "About the role\n\nTest a deterministic application workflow.",
        "Reviewed notes",
        false);

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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
