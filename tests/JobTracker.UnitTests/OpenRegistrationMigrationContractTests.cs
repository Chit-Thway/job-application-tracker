using JobTracker.Web.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace JobTracker.UnitTests;

public sealed class OpenRegistrationMigrationContractTests
{
    [Fact]
    public void Migration_RemovesInvitationsAndAddsHashedOtpAndPolicyRecords()
    {
        var migration = new ReplaceInvitationsWithEmailOtp();
        var addedColumns = migration.UpOperations
            .OfType<AddColumnOperation>()
            .Where(operation => operation.Table == "AspNetUsers")
            .Select(operation => operation.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(
            migration.UpOperations.OfType<DropTableOperation>(),
            operation => operation.Name == "Invitations");
        Assert.Contains(
            migration.UpOperations.OfType<DropColumnOperation>(),
            operation => operation.Table == "AdminAuditEntries"
                && operation.Name == "InvitationId");
        Assert.Contains("EmailVerificationChallengeId", addedColumns);
        Assert.Contains("EmailVerificationCodeHash", addedColumns);
        Assert.Contains("EmailVerificationCodeExpiresAt", addedColumns);
        Assert.Contains("EmailVerificationCodeLastSentAt", addedColumns);
        Assert.Contains("EmailVerificationFailedAttempts", addedColumns);
        Assert.Contains("TermsAcceptedAt", addedColumns);
        Assert.Contains("TermsVersion", addedColumns);
        Assert.Contains("PrivacyAcknowledgedAt", addedColumns);
        Assert.Contains("PrivacyVersion", addedColumns);
        Assert.Contains(
            migration.UpOperations.OfType<AddCheckConstraintOperation>(),
            operation => operation.Name == "CK_AspNetUsers_EmailVerificationFailedAttempts"
                && operation.Sql.Contains("BETWEEN 0 AND 5", StringComparison.Ordinal));
    }

    [Fact]
    public void PhoneCleanupMigration_ClearsPreviouslyCollectedAccountPhoneData()
    {
        var migration = new ClearUnusedAccountPhoneNumbers();
        var cleanup = Assert.Single(migration.UpOperations.OfType<SqlOperation>());

        Assert.Contains("UPDATE \"AspNetUsers\"", cleanup.Sql, StringComparison.Ordinal);
        Assert.Contains("\"PhoneNumber\" = NULL", cleanup.Sql, StringComparison.Ordinal);
        Assert.Contains("\"PhoneNumberConfirmed\" = FALSE", cleanup.Sql, StringComparison.Ordinal);
        Assert.Empty(migration.DownOperations);
    }
}
