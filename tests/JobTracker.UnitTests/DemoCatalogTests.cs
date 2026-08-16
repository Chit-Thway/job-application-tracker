using JobTracker.Web.Demo;

namespace JobTracker.UnitTests;

public sealed class DemoCatalogTests
{
    [Fact]
    public void Catalog_IsDeterministicAndClearlySynthetic()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 14, 1, 0, 0, TimeSpan.Zero));
        var catalog = new DemoCatalog(clock);

        var first = catalog.GetDashboard();
        var second = catalog.GetDashboard();

        Assert.Equal(new DateOnly(2026, 6, 1), first.StartsOn);
        Assert.Equal(new DateOnly(2026, 8, 31), first.EndsOn);
        Assert.Equal(6, first.Applications.Count);
        Assert.Equal(
            first.Applications.Select(item => item.Slug),
            second.Applications.Select(item => item.Slug));
        Assert.Equal(
            first.Applications.Count,
            first.Applications.Select(item => item.Slug).Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            first.Applications.SelectMany(item => item.Contacts),
            contact => Assert.EndsWith("@example.test", contact.Email, StringComparison.Ordinal));
        Assert.Contains(first.Applications, item => item.DeletionScheduledAt is not null);
        Assert.Contains(first.Applications, item => item.Salary.Contains("an hour", StringComparison.Ordinal));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
