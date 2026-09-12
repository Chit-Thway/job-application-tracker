using JobTracker.Web.Admin;
using JobTracker.Web.Configuration;
using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobTracker.Web.Controllers;

[Authorize(Roles = AdminRole.Name)]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AdminController(
    AdminManagementService admin,
    UserManager<ApplicationUser> userManager,
    ILogger<AdminController> logger) : Controller
{
    [HttpGet("/admin")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Section"] = "admin";
        return View(await admin.GetDashboardAsync(cancellationToken));
    }

    [HttpGet("/admin/users")]
    public async Task<IActionResult> Users(
        string? query,
        CancellationToken cancellationToken)
    {
        ViewData["Section"] = "admin";
        ViewData["ActorUserId"] = ActorUserId();
        return View(await admin.GetUsersAsync(query, cancellationToken));
    }

    [HttpGet("/admin/users/{userId}/delete")]
    public async Task<IActionResult> DeleteAccount(
        string userId,
        CancellationToken cancellationToken)
    {
        var model = await admin.GetDeleteAccountAsync(userId, cancellationToken);
        if (model is null)
        {
            TempData["Error"] = "The selected account no longer exists.";
            return RedirectToAction(nameof(Users));
        }

        if (string.Equals(ActorUserId(), userId, StringComparison.Ordinal))
        {
            TempData["Error"] = "You cannot delete your own account from administration.";
            return RedirectToAction(nameof(Users));
        }

        if (model.IsAdmin)
        {
            TempData["Error"] = "Remove administrator access before deleting this account.";
            return RedirectToAction(nameof(Users));
        }

        ViewData["Section"] = "admin";
        return View(model);
    }

    [HttpPost("/admin/users/{userId}/delete")]
    public async Task<IActionResult> DeleteAccount(
        string userId,
        AdminDeleteAccountViewModel input,
        CancellationToken cancellationToken)
    {
        var model = await admin.GetDeleteAccountAsync(userId, cancellationToken);
        if (model is null)
        {
            TempData["Error"] = "The selected account no longer exists.";
            return RedirectToAction(nameof(Users));
        }

        model.ConfirmationEmail = input.ConfirmationEmail;
        ViewData["Section"] = "admin";
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await admin.DeleteAccountAsync(
            ActorUserId(),
            userId,
            input.ConfirmationEmail,
            cancellationToken);
        if (result.Succeeded)
        {
            SetMessage(result);
            return RedirectToAction(nameof(Users));
        }

        ModelState.AddModelError(nameof(input.ConfirmationEmail), result.Message);
        return View(model);
    }

    [HttpPost("/admin/users/{userId}/promote")]
    public async Task<IActionResult> Promote(
        string userId,
        CancellationToken cancellationToken)
    {
        SetMessage(await admin.PromoteAsync(ActorUserId(), userId, cancellationToken));
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("/admin/users/{userId}/tier")]
    public async Task<IActionResult> SetTier(
        string userId,
        AccountTier tier,
        CancellationToken cancellationToken)
    {
        SetMessage(await admin.SetTierAsync(
            ActorUserId(),
            userId,
            tier,
            cancellationToken));
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("/admin/users/{userId}/remove-admin")]
    public async Task<IActionResult> RemoveAdmin(
        string userId,
        CancellationToken cancellationToken)
    {
        SetMessage(await admin.RemoveAdminAsync(ActorUserId(), userId, cancellationToken));
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("/admin/users/{userId}/lock")]
    public async Task<IActionResult> Lock(
        string userId,
        CancellationToken cancellationToken)
    {
        SetMessage(await admin.SetLockedAsync(
            ActorUserId(),
            userId,
            true,
            cancellationToken));
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("/admin/users/{userId}/unlock")]
    public async Task<IActionResult> Unlock(
        string userId,
        CancellationToken cancellationToken)
    {
        SetMessage(await admin.SetLockedAsync(
            ActorUserId(),
            userId,
            false,
            cancellationToken));
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("/admin/users/{userId}/resend-verification")]
    [EnableRateLimiting(RateLimitPolicies.AccountActions)]
    public async Task<IActionResult> ResendVerification(
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            SetMessage(await admin.ResendVerificationAsync(
                ActorUserId(),
                userId,
                challengeId => Url.Action(
                        "VerifyEmail",
                        "Account",
                        new { challengeId },
                        Request.Scheme) ?? throw new InvalidOperationException(
                            "Could not create verification URL."),
                cancellationToken));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Administrator verification delivery failed.");
            TempData["Error"] = "The verification message could not be delivered; try again later.";
        }

        return RedirectToAction(nameof(Users));
    }

    private string ActorUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The administrator identity is unavailable.");

    private void SetMessage(AdminOperationResult result) =>
        TempData[result.Succeeded ? "Success" : "Error"] = result.Message;

}
