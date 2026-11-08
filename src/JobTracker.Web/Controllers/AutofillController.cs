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
    ResumeImportService resumes,
    UserManager<ApplicationUser> users,
    IConfiguration configuration,
    IWebHostEnvironment environment) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken) =>
        View(await profiles.GetAsync(OwnerId, cancellationToken) ?? new ApplicantProfile());

    [HttpPost("")]
    [RequestSizeLimit(1024 * 1024)]
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

    [HttpPost("resume")]
    [EnableRateLimiting(RateLimitPolicies.JobImports)]
    [RequestSizeLimit(ResumeImportService.MaximumUploadBytes + 64 * 1024)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = ResumeImportService.MaximumUploadBytes + 1024,
        MemoryBufferThreshold = ResumeImportService.MaximumUploadBytes + 64 * 1024)]
    public async Task<IActionResult> ImportResume(IFormFile? resume, CancellationToken cancellationToken)
    {
        var draft = await profiles.GetAsync(OwnerId, cancellationToken) ?? new ApplicantProfile();
        try
        {
            ViewData["ResumeReview"] = await resumes.ImportAsync(resume, draft, cancellationToken);
        }
        catch (ResumeImportException error)
        {
            ViewData["ResumeError"] = error.Message;
        }
        return View(nameof(Profile), draft);
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

    [HttpGet("authorize")]
    [EnableRateLimiting(RateLimitPolicies.AccountActions)]
    public async Task<IActionResult> AuthorizeConnection(
        string? extensionId, string? challenge, string? state, CancellationToken cancellationToken)
    {
        var navigation = Request.Headers["Sec-Fetch-Site"].ToString();
        if (navigation is not ("" or "none" or "same-origin")
            || !IsTrustedExtension(extensionId)
            || challenge is not { Length: 64 } || !challenge.All(char.IsAsciiHexDigitLower)
            || state is not { Length: 43 } || !state.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return BadRequest(new { message = "The tracker could not authorize this extension request." });
        }

        var user = await users.GetUserAsync(User);
        var code = user is null ? null : await profiles.CreatePairingCodeAsync(user, challenge, cancellationToken);
        // End the sign-in form navigation before the browser leaves the tracker origin.
        var callback = $"https://{extensionId}.chromiumapp.org/autofill#state={state}";
        return View("CompleteConnection", code is null ? $"{callback}&error=profile_required" : $"{callback}&code={code}");
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

    private bool IsTrustedExtension(string? id)
    {
        if (id is not { Length: 32 } || !id.All(character => character is >= 'a' and <= 'p'))
        {
            return false;
        }
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return true;
        }

        return Uri.TryCreate(configuration["BrowserExtension:ChromeWebStoreUrl"], UriKind.Absolute, out var listing)
            && listing.Scheme == Uri.UriSchemeHttps && listing.Host == "chromewebstore.google.com"
            && string.Equals(listing.Segments.Last().TrimEnd('/'), id, StringComparison.Ordinal);
    }
}

public sealed record AutofillConnectionView(string Challenge, string? Code = null, bool Attempted = false);
public sealed record AutofillExchange(string? Code, string? Verifier);
