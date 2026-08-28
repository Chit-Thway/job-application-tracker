using JobTracker.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Applications;

public sealed record ApplicationQuotaDecision(
    bool CanCreate,
    AccountTier Tier,
    int CurrentApplicationCount,
    int? ApplicationLimit);

public sealed class ApplicationQuotaService(ApplicationDbContext database)
{
    public const int TierOneApplicationLimit = 10;

    public const string LimitReachedMessage =
        "You’ve reached your 10-application limit. Contact the administrator to request a Tier 2 upgrade.";

    public async Task<ApplicationQuotaDecision> CheckCreationAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        if (database.Database.IsRelational())
        {
            if (database.Database.CurrentTransaction is null)
            {
                throw new InvalidOperationException(
                    "Application quota checks must run inside the application creation transaction.");
            }

            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({ownerId}, 0))",
                cancellationToken);
        }

        var tier = await database.Users
            .AsNoTracking()
            .Where(user => user.Id == ownerId)
            .Select(user => user.AccountTier)
            .SingleAsync(cancellationToken);
        if (!Enum.IsDefined(tier))
        {
            throw new InvalidOperationException("The account has an unsupported application tier.");
        }

        var currentCount = await database.JobApplications.CountAsync(
            application => application.OwnerId == ownerId,
            cancellationToken);
        int? limit = tier == AccountTier.Tier1 ? TierOneApplicationLimit : null;

        return new ApplicationQuotaDecision(
            limit is null || currentCount < limit.Value,
            tier,
            currentCount,
            limit);
    }
}
