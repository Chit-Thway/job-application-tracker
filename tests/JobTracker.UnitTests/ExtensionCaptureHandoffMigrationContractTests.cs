using JobTracker.Web.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace JobTracker.UnitTests;

public sealed class ExtensionCaptureHandoffMigrationContractTests
{
    [Fact]
    public void Migration_StoresOnlyProtectedPayloadAndIndexedTokenHash()
    {
        var migration = new AddExtensionCaptureHandoffs();
        var table = Assert.Single(
            migration.UpOperations.OfType<CreateTableOperation>());
        var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToList();

        Assert.Equal("ExtensionCaptureHandoffs", table.Name);
        Assert.Contains(table.Columns, column =>
            column.Name == "ProtectedPayload" && column.ColumnType == "text");
        Assert.Contains(table.Columns, column =>
            column.Name == "TokenHash" && column.MaxLength == 64);
        Assert.DoesNotContain(table.Columns, column =>
            column.Name.Contains("PayloadJson", StringComparison.Ordinal));
        Assert.Contains(indexes, index =>
            index.Name == "IX_ExtensionCaptureHandoffs_TokenHash" && index.IsUnique);
        Assert.Contains(indexes, index =>
            index.Name == "IX_ExtensionCaptureHandoffs_ExpiresAt");
    }
}
