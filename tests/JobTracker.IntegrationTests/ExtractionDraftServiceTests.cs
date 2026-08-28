using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Extraction;
using JobTracker.Web.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.IntegrationTests;

public sealed class ExtractionDraftServiceTests
{
    [Fact]
    public async Task TierOneLimit_PreservesReviewDraftWithoutCreatingCompanyOrApplication()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"limited-extraction-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var user = User("limited-extraction-owner");
        database.Users.Add(user);
        database.JobApplications.AddRange(Enumerable.Range(1, 10).Select(index =>
            new JobApplication
            {
                OwnerId = user.Id,
                RoleTitle = $"Existing role {index}",
                AppliedOn = new DateOnly(2026, 8, index),
            }));
        await database.SaveChangesAsync();
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 8, 28, 3, 0, 0, TimeSpan.Zero));
        var service = Service(database, user.Id, time);
        var draftId = await service.CreatePastedTextDraftAsync(
            "Job Title: Limited Engineer\nCompany: Synthetic Limit Labs",
            new DateOnly(2026, 8, 28));

        var completion = await service.CompleteAsync(draftId, ValidInput());

        Assert.Equal(ExtractionDraftResult.ApplicationLimitReached, completion.Result);
        Assert.Null(completion.ApplicationId);
        Assert.Equal(10, await database.JobApplications.CountAsync());
        Assert.Empty(database.Companies);
        Assert.True(await database.ExtractionDrafts.AnyAsync(draft => draft.Id == draftId));
    }

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
    public async Task ConfirmedDraft_ReusesExplicitlySelectedOwnedCompany()
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
            ExistingCompanyId = company.Id,
        };

        var completion = await service.CompleteAsync(draftId, input);

        Assert.Equal(ExtractionDraftResult.Success, completion.Result);
        Assert.Single(database.Companies);
        Assert.Equal(company.Id, (await database.JobApplications.SingleAsync()).CompanyId);
    }

    [Fact]
    public async Task CompanySuggestions_AreMeaningfulAndOwnerScoped()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"company-suggestions-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var owner = User("suggestion-owner");
        var otherOwner = User("suggestion-other-owner");
        var accenture = new Company
        {
            OwnerId = owner.Id,
            Name = "Accenture",
            Location = "Perth",
        };
        database.Users.AddRange(owner, otherOwner);
        database.Companies.AddRange(
            accenture,
            new Company { OwnerId = owner.Id, Name = "Air Liquide", Location = "Kwinana" },
            new Company { OwnerId = otherOwner.Id, Name = "Accenture Australia", Location = "Melbourne" });
        await database.SaveChangesAsync();
        var service = Service(
            database,
            owner.Id,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 24, 3, 0, 0, TimeSpan.Zero)));

        var suggestions = await service.FindCompanySuggestionsAsync("Accenture Australia Pty Ltd");

        var suggestion = Assert.Single(suggestions);
        Assert.Equal(accenture.Id, suggestion.Id);
        Assert.Equal(CompanyNameMatchKind.MeaningfulPhrase, suggestion.MatchKind);
    }

    [Fact]
    public async Task ConfirmedDraft_RejectsAnotherOwnersSelectedCompany()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"company-selection-isolation-{Guid.NewGuid()}")
            .Options;
        await using var database = new ApplicationDbContext(options);
        var owner = User("selection-owner");
        var otherOwner = User("selection-other-owner");
        var otherCompany = new Company
        {
            OwnerId = otherOwner.Id,
            Name = "Accenture",
            Location = "Melbourne",
        };
        database.Users.AddRange(owner, otherOwner);
        database.Companies.Add(otherCompany);
        await database.SaveChangesAsync();
        var service = Service(
            database,
            owner.Id,
            new MutableTimeProvider(new DateTimeOffset(2026, 8, 24, 3, 0, 0, TimeSpan.Zero)));
        var draftId = await service.CreatePastedTextDraftAsync(
            "Job Title: Graduate Analyst\nCompany: Accenture",
            new DateOnly(2026, 8, 24));

        var completion = await service.CompleteAsync(
            draftId,
            ValidInput() with { ExistingCompanyId = otherCompany.Id });

        Assert.Equal(ExtractionDraftResult.InvalidCompanySelection, completion.Result);
        Assert.Empty(database.JobApplications);
        Assert.Single(database.ExtractionDrafts);
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
        new(
            database,
            new FixedCurrentUser(userId),
            timeProvider,
            new PastedJobTextExtractor(),
            new ApplicationQuotaService(database));

    private static ExtractionReviewInput ValidInput() => new(
        "Reviewed Test Engineer",
        "Synthetic Review Company",
        "Perth, WA",
        null,
        new DateOnly(2026, 8, 4),
        "Hybrid",
        "https://example.test/jobs/reviewed",
        null,
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
