using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Web.Admin;

public sealed class AdminManagementService(
    ApplicationDbContext database,
    UserManager<ApplicationUser> userManager,
    InvitationService invitationService,
    IAccountEmailSender emailSender,
    TimeProvider timeProvider)
{
    public async Task<AdminDashboardViewModel> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var users = await database.Users.AsNoTracking().ToListAsync(cancellationToken);
        var admins = await userManager.GetUsersInRoleAsync(AdminRole.Name);
        var availableInvitations = await database.Invitations.CountAsync(
            item => item.UsedAt == null && item.RevokedAt == null && item.ExpiresAt > now,
            cancellationToken);
        var activity = await database.AdminAuditEntries
            .AsNoTracking()
            .OrderByDescending(item => item.OccurredAt)
            .Take(12)
            .ToListAsync(cancellationToken);
        var actorIds = activity.Select(item => item.ActorUserId).Distinct().ToArray();
        var actors = await database.Users
            .AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => user.Email ?? user.DisplayName,
                cancellationToken);

        return new AdminDashboardViewModel(
            users.Count,
            users.Count(user => user.EmailConfirmed),
            admins.Count,
            users.Count(user => user.AccountTier == AccountTier.Tier2),
            availableInvitations,
            activity.Select(item => new AdminAuditRow(
                item.OccurredAt,
                actors.GetValueOrDefault(item.ActorUserId, "Administrator"),
                item.Action,
                item.Details)).ToList());
    }

    public async Task<AdminUsersViewModel> GetUsersAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var users = await database.Users
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .ToListAsync(cancellationToken);
        if (normalizedQuery.Length > 0)
        {
            users = users.Where(user =>
                (user.Email?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false)
                || (user.PhoneNumber?.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ?? false)
                || user.DisplayName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        var admins = await userManager.GetUsersInRoleAsync(AdminRole.Name);
        var adminIds = admins.Select(user => user.Id).ToHashSet(StringComparer.Ordinal);
        var now = timeProvider.GetUtcNow();
        var visibleUserIds = users.Select(user => user.Id).ToArray();
        var applicationCounts = await database.JobApplications
            .AsNoTracking()
            .Where(application => visibleUserIds.Contains(application.OwnerId))
            .GroupBy(application => application.OwnerId)
            .Select(group => new { OwnerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.OwnerId, item => item.Count, cancellationToken);

        return new AdminUsersViewModel(
            normalizedQuery,
            users.Select(user => new AdminUserRow(
                user.Id,
                user.Email ?? string.Empty,
                user.PhoneNumber,
                user.DisplayName,
                user.CreatedAt,
                user.LastLoginAt,
                user.EmailConfirmed,
                user.AccountTier,
                applicationCounts.GetValueOrDefault(user.Id),
                adminIds.Contains(user.Id),
                user.LockoutEnd > now)).ToList());
    }

    public async Task<AdminOperationResult> SetTierAsync(
        string actorUserId,
        string targetUserId,
        AccountTier tier,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(tier))
        {
            return new(false, "Choose Tier 1 or Tier 2.");
        }

        var target = await userManager.FindByIdAsync(targetUserId);
        if (target is null)
        {
            return new(false, "The selected account no longer exists.");
        }

        if (target.AccountTier == tier)
        {
            return new(true, $"That account is already {DisplayTier(tier)}.");
        }

        var previousTier = target.AccountTier;
        target.AccountTier = tier;
        var result = await userManager.UpdateAsync(target);
        if (!result.Succeeded)
        {
            return new(false, "The account tier could not be changed.");
        }

        await RecordAsync(
            actorUserId,
            "Account tier changed",
            $"Account tier changed from {DisplayTier(previousTier)} to {DisplayTier(tier)}.",
            target.Id,
            cancellationToken: cancellationToken);

        if (tier == AccountTier.Tier1)
        {
            var applicationCount = await database.JobApplications.CountAsync(
                application => application.OwnerId == target.Id,
                cancellationToken);
            if (applicationCount >= ApplicationQuotaService.TierOneApplicationLimit)
            {
                return new(
                    true,
                    "Tier 1 assigned. Existing applications were retained; new applications are blocked until the account holds fewer than 10.");
            }
        }

        return new(true, $"{DisplayTier(tier)} assigned.");
    }

    public async Task<AdminInvitationsViewModel> GetInvitationsAsync(
        CreateAdminInvitationInput? input = null,
        CancellationToken cancellationToken = default)
    {
        var summaries = await invitationService.ListAsync(cancellationToken);
        return new AdminInvitationsViewModel(
            input ?? new CreateAdminInvitationInput(),
            summaries.Select(item => new AdminInvitationRow(
                item.Id,
                item.RecipientEmail ?? "Private code",
                item.CreatedAt,
                item.ExpiresAt,
                item.UsedAt,
                item.Status)).ToList());
    }

    public async Task<AdminOperationResult> CreateInvitationAsync(
        string actorUserId,
        CreateAdminInvitationInput input,
        CancellationToken cancellationToken = default)
    {
        var recipient = input.Email.Trim();
        var existingUser = await userManager.FindByEmailAsync(recipient);
        if (existingUser is not null)
        {
            return new(false, "That email address already has an account.");
        }

        var invitation = await invitationService.CreateForRecipientAsync(
            input.ValidForDays,
            recipient,
            actorUserId,
            cancellationToken);
        try
        {
            await emailSender.SendInvitationAsync(recipient, invitation.Code, invitation.ExpiresAt);
        }
        catch
        {
            await invitationService.RevokeAsync(invitation.Id, cancellationToken);
            throw;
        }

        await RecordAsync(
            actorUserId,
            "Invitation sent",
            $"Invitation sent to {recipient}.",
            invitationId: invitation.Id,
            cancellationToken: cancellationToken);
        return new(true, "Invitation sent.");
    }

    public async Task<AdminOperationResult> RevokeInvitationAsync(
        string actorUserId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        if (!await invitationService.RevokeAsync(invitationId, cancellationToken))
        {
            return new(false, "That invitation is no longer available to revoke.");
        }

        await RecordAsync(
            actorUserId,
            "Invitation revoked",
            "An unused invitation was revoked.",
            invitationId: invitationId,
            cancellationToken: cancellationToken);
        return new(true, "Invitation revoked.");
    }

    public async Task<AdminOperationResult> PromoteAsync(
        string actorUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        var target = await userManager.FindByIdAsync(targetUserId);
        if (target is null)
        {
            return new(false, "The selected account no longer exists.");
        }

        if (await userManager.IsInRoleAsync(target, AdminRole.Name))
        {
            return new(true, "That account is already an administrator.");
        }

        var result = await userManager.AddToRoleAsync(target, AdminRole.Name);
        if (!result.Succeeded)
        {
            return new(false, "The administrator role could not be assigned.");
        }

        await userManager.UpdateSecurityStampAsync(target);

        await RecordAsync(actorUserId, "Administrator granted", "Administrator access was granted.", target.Id, cancellationToken: cancellationToken);
        return new(true, "Administrator access granted.");
    }

    public async Task<AdminOperationResult> RemoveAdminAsync(
        string actorUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
        {
            return new(false, "You cannot remove your own administrator access.");
        }

        var target = await userManager.FindByIdAsync(targetUserId);
        if (target is null || !await userManager.IsInRoleAsync(target, AdminRole.Name))
        {
            return new(false, "The selected account is not an administrator.");
        }

        var administrators = await userManager.GetUsersInRoleAsync(AdminRole.Name);
        if (administrators.Count <= 1)
        {
            return new(false, "The final administrator cannot be removed.");
        }

        var result = await userManager.RemoveFromRoleAsync(target, AdminRole.Name);
        if (!result.Succeeded)
        {
            return new(false, "Administrator access could not be removed.");
        }

        await userManager.UpdateSecurityStampAsync(target);

        await RecordAsync(actorUserId, "Administrator removed", "Administrator access was removed.", target.Id, cancellationToken: cancellationToken);
        return new(true, "Administrator access removed.");
    }

    public async Task<AdminOperationResult> SetLockedAsync(
        string actorUserId,
        string targetUserId,
        bool locked,
        CancellationToken cancellationToken = default)
    {
        if (locked && string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
        {
            return new(false, "You cannot lock your own account.");
        }

        var target = await userManager.FindByIdAsync(targetUserId);
        if (target is null)
        {
            return new(false, "The selected account no longer exists.");
        }

        var result = await userManager.SetLockoutEndDateAsync(
            target,
            locked ? timeProvider.GetUtcNow().AddYears(100) : null);
        if (!result.Succeeded)
        {
            return new(false, "The account lock could not be changed.");
        }

        if (!locked)
        {
            await userManager.ResetAccessFailedCountAsync(target);
        }

        await userManager.UpdateSecurityStampAsync(target);

        await RecordAsync(
            actorUserId,
            locked ? "Account locked" : "Account unlocked",
            locked ? "Sign-in access was locked." : "Sign-in access was restored.",
            target.Id,
            cancellationToken: cancellationToken);
        return new(true, locked ? "Account locked." : "Account unlocked.");
    }

    public async Task<AdminOperationResult> ResendVerificationAsync(
        string actorUserId,
        string targetUserId,
        Func<ApplicationUser, Task<string>> verificationUrlFactory,
        CancellationToken cancellationToken = default)
    {
        var target = await userManager.FindByIdAsync(targetUserId);
        if (target is null)
        {
            return new(false, "The selected account no longer exists.");
        }

        if (target.EmailConfirmed)
        {
            return new(false, "That email address is already verified.");
        }

        var url = await verificationUrlFactory(target);
        await emailSender.SendVerificationAsync(target, url);
        await RecordAsync(actorUserId, "Verification resent", "A new verification message was sent.", target.Id, cancellationToken: cancellationToken);
        return new(true, "Verification message sent.");
    }

    private async Task RecordAsync(
        string actorUserId,
        string action,
        string details,
        string? targetUserId = null,
        Guid? invitationId = null,
        CancellationToken cancellationToken = default)
    {
        database.AdminAuditEntries.Add(new AdminAuditEntry
        {
            ActorUserId = actorUserId,
            Action = action,
            Details = details,
            TargetUserId = targetUserId,
            InvitationId = invitationId,
            OccurredAt = timeProvider.GetUtcNow(),
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    private static string DisplayTier(AccountTier tier) => tier switch
    {
        AccountTier.Tier1 => "Tier 1",
        AccountTier.Tier2 => "Tier 2",
        _ => "Unsupported tier",
    };
}
