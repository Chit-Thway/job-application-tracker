using JobTracker.Web.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace JobTracker.UnitTests;

public sealed class RetentionMigrationContractTests
{
    [Fact]
    public void DatabaseFunction_RechecksSafety_AndLogsOnlyOperationalResults()
    {
        var migration = new AddRetentionCleanup();
        var sql = string.Join(
            Environment.NewLine,
            migration.UpOperations
                .OfType<SqlOperation>()
                .Select(operation => operation.Sql));

        Assert.Contains("process_job_application_retention", sql, StringComparison.Ordinal);
        Assert.Contains("\"IsSavedForever\" = FALSE", sql, StringComparison.Ordinal);
        Assert.Contains("\"DeletionScheduledAt\" <= p_now", sql, StringComparison.Ordinal);
        Assert.Contains("p_now + interval '14 days'", sql, StringComparison.Ordinal);
        Assert.Contains("application.\"AppliedOn\" + interval '3 months'", sql, StringComparison.Ordinal);
        Assert.Contains("EXCEPTION WHEN OTHERS", sql, StringComparison.Ordinal);
        Assert.Contains("RETURNED_SQLSTATE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("RoleTitle", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CompanyName", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SourceText", sql, StringComparison.Ordinal);
    }
}
