using JobTracker.Web.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace JobTracker.UnitTests;

public sealed class RetentionMigrationContractTests
{
    [Fact]
    public void DismissalMigration_AddsPersistentNullableTimestamp()
    {
        var operation = Assert.Single(
            new AddDeletionWarningDismissal().UpOperations.OfType<AddColumnOperation>());

        Assert.Equal("JobApplications", operation.Table);
        Assert.Equal("DeletionWarningDismissedAt", operation.Name);
        Assert.True(operation.IsNullable);
    }

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

    [Fact]
    public void UserPreferencesMigration_ConstrainsChoices_AndUsesThemInTheScheduler()
    {
        var migration = new AddUserRetentionPreferences();
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToList();
        var constraints = migration.UpOperations.OfType<AddCheckConstraintOperation>().ToList();
        var sql = string.Join(
            Environment.NewLine,
            migration.UpOperations
                .OfType<SqlOperation>()
                .Select(operation => operation.Sql));

        Assert.Contains(columns, operation =>
            operation.Name == "RetentionMonths" && Equals(operation.DefaultValue, 3));
        Assert.Contains(columns, operation =>
            operation.Name == "DeletionGraceDays" && Equals(operation.DefaultValue, 14));
        Assert.Contains(constraints, operation =>
            operation.Name == "CK_AspNetUsers_RetentionMonths");
        Assert.Contains(constraints, operation =>
            operation.Name == "CK_AspNetUsers_DeletionGraceDays");
        Assert.Contains("owner.\"RetentionMonths\" * interval '1 month'", sql, StringComparison.Ordinal);
        Assert.Contains("owner.\"DeletionGraceDays\" * interval '1 day'", sql, StringComparison.Ordinal);
        Assert.Contains("\"IsSavedForever\" = FALSE", sql, StringComparison.Ordinal);
        Assert.Contains("\"DeletionScheduledAt\" <= p_now", sql, StringComparison.Ordinal);
    }
}
