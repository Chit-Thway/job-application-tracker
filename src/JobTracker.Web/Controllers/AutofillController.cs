using JobTracker.Web.Autofill;
using JobTracker.Web.Configuration;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobTracker.Web.Controllers;

[Authorize]
[Route("extension/profile")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AutofillController(
    AutofillProfileService profiles,
    UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken) =>
        View(await profiles.GetAsync(OwnerId, cancellationToken) ?? new ApplicantProfile());

    [HttpPost("")]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Save(ApplicantProfile profile, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            if (await profiles.SaveAsync(OwnerId, profile, cancellationToken))
            {
                TempData["Success"] = "Autofill profile saved.";
                return RedirectToAction(nameof(Profile));
            }
            ModelState.AddModelError(string.Empty, "Your profile changed while saving. Review and save again.");
        }
        return View(nameof(Profile), profile);
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        await profiles.DeleteAsync(OwnerId, cancellationToken);
        TempData["Success"] = "Autofill profile deleted.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await profiles.RevokeAsync(OwnerId, cancellationToken);
        TempData["Success"] = "Extension disconnected.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet("connect")]
    public IActionResult Connect(string? challenge) =>
        View(new AutofillConnectionView(challenge ?? string.Empty));

    [HttpPost("connect")]
    [EnableRateLimiting(RateLimitPolicies.AccountActions)]
    public async Task<IActionResult> Approve(string? challenge, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        var code = user is null || challenge is null
            ? null
            : await profiles.CreatePairingCodeAsync(user, challenge, cancellationToken);
        return View(nameof(Connect), new AutofillConnectionView(challenge ?? string.Empty, code, true));
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("exchange")]
    [RequestSizeLimit(2048)]
    [EnableRateLimiting(RateLimitPolicies.AccountActions)]
    public async Task<IActionResult> Exchange([FromBody] AutofillExchange? input, CancellationToken cancellationToken)
    {
        var token = await profiles.ExchangeAsync(input?.Code, input?.Verifier, cancellationToken);
        return token is null ? Unauthorized(new { message = "Connection code is invalid or expired." }) : Json(new { token });
    }

    [AllowAnonymous]
    [HttpGet("data")]
    public async Task<IActionResult> Data(CancellationToken cancellationToken)
    {
        var header = Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : null;
        var profile = await profiles.GetConnectedProfileAsync(token, cancellationToken);
        return profile is null ? Unauthorized(new { message = "Profile connection is unavailable." }) : Json(profile);
    }

    private string OwnerId => users.GetUserId(User)!;
}

public sealed record AutofillConnectionView(string Challenge, string? Code = null, bool Attempted = false);
public sealed record AutofillExchange(string? Code, string? Verifier);
