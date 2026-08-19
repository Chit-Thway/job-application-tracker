using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace JobTracker.UnitTests;

public sealed class ApplicationStatusSimplificationTests
{
    [Fact]
    public void ActiveChoices_ExposeOnlyTheSimplifiedWorkflowInTheRequestedOrder()
    {
        Assert.Equal(
            [
                PipelineStage.Applied,
                PipelineStage.Screening,
                PipelineStage.Assessment,
                PipelineStage.Interview,
                PipelineStage.Offer,
            ],
            ApplicationDisplay.ActiveStages);
        Assert.Equal(
            [
                ApplicationOutcome.Active,
                ApplicationOutcome.Rejected,
                ApplicationOutcome.Ghosted,
                ApplicationOutcome.Accepted,
                ApplicationOutcome.Withdrawn,
            ],
            ApplicationDisplay.ActiveOutcomes);
    }

    [Theory]
    [InlineData(PipelineStage.RecruiterContact, PipelineStage.Screening)]
    [InlineData(PipelineStage.ReferenceCheck, PipelineStage.Interview)]
    public void LegacyStages_NormalizeWithoutRemovingHistoricalEnumValues(
        PipelineStage legacy,
        PipelineStage expected)
    {
        Assert.Equal(expected, ApplicationDisplay.NormalizeStage(legacy));
        Assert.False(ApplicationDisplay.IsActive(legacy));
    }

    [Fact]
    public void LegacyOfferDeclined_NormalizesToWithdrawn()
    {
        Assert.Equal(
            ApplicationOutcome.Withdrawn,
            ApplicationDisplay.NormalizeOutcome(ApplicationOutcome.OfferDeclined));
        Assert.False(ApplicationDisplay.IsActive(ApplicationOutcome.OfferDeclined));
    }

    [Fact]
    public void DataMigration_ConsolidatesOnlyCurrentApplicationState()
    {
        var migration = new SimplifyApplicationStatuses();
        var sql = string.Join(
            Environment.NewLine,
            migration.UpOperations
                .OfType<SqlOperation>()
                .Select(operation => operation.Sql));

        Assert.Contains("UPDATE \"JobApplications\"", sql, StringComparison.Ordinal);
        Assert.Contains("'RecruiterContact'", sql, StringComparison.Ordinal);
        Assert.Contains("'ReferenceCheck'", sql, StringComparison.Ordinal);
        Assert.Contains("'OfferDeclined'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusHistory", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DataMigration_IsDiscoverableByTheApplicationDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=discovery_only;Username=test;Password=test")
            .Options;
        using var database = new ApplicationDbContext(options);

        Assert.Contains(
            "20260819090000_SimplifyApplicationStatuses",
            database.Database.GetMigrations());
    }
}
