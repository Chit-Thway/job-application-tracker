using JobTracker.Web.Foundation;

namespace JobTracker.UnitTests;

public sealed class FoundationPageCatalogTests
{
    [Fact]
    public void Catalog_DefinesEveryMilestoneOneDestinationOnce()
    {
        string[] expectedRoutes =
        [
            "/dashboard",
            "/actions",
            "/settings",
            "/demo",
        ];

        var pages = FoundationPageCatalog.All;

        Assert.Equal(expectedRoutes.Length, pages.Count);
        Assert.Equal(
            expectedRoutes.Order(StringComparer.Ordinal),
            pages.Select(page => page.Route).Order(StringComparer.Ordinal));
        Assert.Equal(pages.Count, pages.Select(page => page.Action).Distinct().Count());
        Assert.All(pages, page =>
        {
            Assert.False(string.IsNullOrWhiteSpace(page.Title));
            Assert.False(string.IsNullOrWhiteSpace(page.Description));
            Assert.NotEmpty(page.PreviewItems);
        });
    }

    [Fact]
    public void GetByAction_ReturnsTheMatchingDestination()
    {
        var page = FoundationPageCatalog.GetByAction("ActionCentre");

        Assert.Equal("/actions", page.Route);
        Assert.Equal("Action Centre", page.NavigationLabel);
    }
}
