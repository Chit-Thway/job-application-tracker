using JobTracker.Web.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace JobTracker.UnitTests;

public sealed class AccountTierMigrationContractTests
{
    [Fact]
    public void Migration_DefaultsOrdinaryAccountsToTierOne_AndGrandfathersAdministrators()
    {
        var migration = new AddAccountTiers();
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        var constraint = Assert.Single(
            migration.UpOperations.OfType<AddCheckConstraintOperation>());
        var dataUpdate = Assert.Single(migration.UpOperations.OfType<SqlOperation>());

        Assert.Equal("AspNetUsers", column.Table);
        Assert.Equal("AccountTier", column.Name);
        Assert.Equal(1, column.DefaultValue);
        Assert.False(column.IsNullable);
        Assert.Equal("CK_AspNetUsers_AccountTier", constraint.Name);
        Assert.Contains("IN (1, 2)", constraint.Sql, StringComparison.Ordinal);
        Assert.Contains("AspNetUserRoles", dataUpdate.Sql, StringComparison.Ordinal);
        Assert.Contains("roles.\"Name\" = 'Admin'", dataUpdate.Sql, StringComparison.Ordinal);
        Assert.Contains("SET \"AccountTier\" = 2", dataUpdate.Sql, StringComparison.Ordinal);
    }
}
