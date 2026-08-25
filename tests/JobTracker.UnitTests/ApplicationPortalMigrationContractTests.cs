using JobTracker.Web.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace JobTracker.UnitTests;

public sealed class ApplicationPortalMigrationContractTests
{
    [Fact]
    public void Migration_AddsOnlyAnOptionalLengthLimitedPortalUrl()
    {
        var migration = new AddApplicationPortalUrl();
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        var rollback = Assert.Single(migration.DownOperations.OfType<DropColumnOperation>());

        Assert.Equal("JobApplications", column.Table);
        Assert.Equal("ApplicationPortalUrl", column.Name);
        Assert.Equal(2048, column.MaxLength);
        Assert.True(column.IsNullable);
        Assert.Equal("JobApplications", rollback.Table);
        Assert.Equal("ApplicationPortalUrl", rollback.Name);
    }
}
