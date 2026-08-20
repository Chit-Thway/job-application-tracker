using System.Text;
using JobTracker.Web.Admin;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

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
        return View(await admin.GetUsersAsync(query, cancellationToken));
    }

    [HttpGet("/admin/invitations")]
    public async Task<IActionResult> Invitations(CancellationToken cancellationToken)
    {
        ViewData["Section"] = "admin";
        return View(await admin.GetInvitationsAsync(cancellationToken: cancellationToken));
    }

    [HttpPost("/admin/invitations")]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> CreateInvitation(
        [Bind(Prefix = "Input")] CreateAdminInvitationInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Section"] = "admin";
            return View(
                "Invitations",
                await admin.GetInvitationsAsync(input, cancellationToken));
        }

        try
        {
            SetMessage(await admin.CreateInvitationAsync(
                ActorUserId(),
                input,
                cancellationToken));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Administrator invitation delivery failed.");
            TempData["Error"] = "The invitation could not be delivered. Its code was revoked; try again later.";
        }

        return RedirectToAction(nameof(Invitations));
    }

    [HttpPost("/admin/invitations/{invitationId:guid}/revoke")]
    public async Task<IActionResult> RevokeInvitation(
        Guid invitationId,
        CancellationToken cancellationToken)
    {
        SetMessage(await admin.RevokeInvitationAsync(
            ActorUserId(),
            invitationId,
            cancellationToken));
        return RedirectToAction(nameof(Invitations));
    }

    [HttpPost("/admin/users/{userId}/promote")]
    public async Task<IActionResult> Promote(
        string userId,
        CancellationToken cancellationToken)
    {
        SetMessage(await admin.PromoteAsync(ActorUserId(), userId, cancellationToken));
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
    [EnableRateLimiting("account")]
    public async Task<IActionResult> ResendVerification(
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            SetMessage(await admin.ResendVerificationAsync(
                ActorUserId(),
                userId,
                async user =>
                {
                    var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                    return Url.Action(
                        "ConfirmEmail",
                        "Account",
                        new { userId = user.Id, code = EncodeToken(token) },
                        Request.Scheme) ?? throw new InvalidOperationException(
                            "Could not create verification URL.");
                },
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

    private static string EncodeToken(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
}
