using JobTracker.Web.Data;
using JobTracker.Web.Identity;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace JobTracker.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IAccountEmailSender emailSender,
    EmailVerificationService emailVerification,
    TimeProvider timeProvider,
    ILogger<AccountController> logger) : Controller
{
    [HttpGet("/account/register")]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Dashboard", "Home");
        }

        return View(new RegisterViewModel());
    }

    [HttpPost("/account/register")]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var now = timeProvider.GetUtcNow();
        var email = model.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = model.DisplayName.Trim(),
            TimeZoneId = "Australia/Perth",
            AccountTier = AccountTier.Tier1,
            CreatedAt = now,
            TermsAcceptedAt = now,
            TermsVersion = LegalDocumentVersions.Terms,
            PrivacyAcknowledgedAt = now,
            PrivacyVersion = LegalDocumentVersions.Privacy,
        };
        var result = await userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            AddRegistrationErrors(model, result.Errors);
            return View(model);
        }

        try
        {
            var issue = await emailVerification.IssueAsync(
                user,
                VerificationUrl,
                HttpContext.RequestAborted);
            if (issue.Succeeded && issue.ChallengeId is Guid challengeId)
            {
                return RedirectToAction(nameof(VerifyEmail), new { challengeId });
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Registration verification-code delivery failed.");
        }

        return View("AccountResult", new AccountResultViewModel(
            "Account created",
            "Your account was created, but the verification code could not be delivered. Use resend verification to try again.",
            false));
    }

    [HttpGet("/account/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Dashboard", "Home");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost("/account/login")]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not null)
        {
            var result = await signInManager.PasswordSignInAsync(
                user,
                model.Password,
                model.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                user.LastLoginAt = timeProvider.GetUtcNow();
                await userManager.UpdateAsync(user);
                return LocalRedirect(SafeReturnUrl(model.ReturnUrl));
            }

            if (result.IsNotAllowed
                && !await userManager.IsEmailConfirmedAsync(user)
                && await userManager.CheckPasswordAsync(user, model.Password))
            {
                model.EmailVerificationRequired = true;
                return View(model);
            }
        }

        ModelState.AddModelError(
            string.Empty,
            "Unable to sign in. Check your details and confirm your email.");
        return View(model);
    }

    [Authorize]
    [HttpPost("/account/logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("/account/forgot-password")]
    public IActionResult ForgotPassword() => View(new EmailActionViewModel());

    [HttpPost("/account/forgot-password")]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> ForgotPassword(EmailActionViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not null && await userManager.IsEmailConfirmedAsync(user))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = Url.Action(
                nameof(ResetPassword),
                "Account",
                new { userId = user.Id, code = EncodeToken(token) },
                Request.Scheme) ?? throw new InvalidOperationException("Could not create reset URL.");
            await emailSender.SendPasswordResetAsync(user, resetUrl);
        }

        return View("AccountResult", new AccountResultViewModel(
            "Check your messages",
            "If a verified account matches that email address, a password-reset message has been sent.",
            true));
    }

    [HttpGet("/account/resend-verification")]
    public IActionResult ResendVerification() => View(new EmailActionViewModel());

    [HttpPost("/account/resend-verification")]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> ResendVerification(EmailActionViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email);
        if (user is not null && !await userManager.IsEmailConfirmedAsync(user))
        {
            try
            {
                await emailVerification.IssueAsync(
                    user,
                    VerificationUrl,
                    HttpContext.RequestAborted);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Verification-code resend delivery failed.");
            }
        }

        return View("AccountResult", new AccountResultViewModel(
            "Check your messages",
            "If an unverified account matches that email address and is eligible for a resend, a new six-digit code has been sent.",
            true));
    }

    [HttpGet("/account/verify-email")]
    public async Task<IActionResult> VerifyEmail(
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        var model = await BuildVerificationModelAsync(challengeId, cancellationToken);
        if (model is null)
        {
            return InvalidLink("That verification request is invalid, completed, or expired.");
        }

        return View(model);
    }

    [HttpPost("/account/verify-email")]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> VerifyEmail(
        EmailVerificationViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await VerificationViewOrInvalidAsync(model, cancellationToken);
        }

        var result = await emailVerification.VerifyAsync(
            model.ChallengeId,
            model.Code,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(model.Code), result.Message);
            return await VerificationViewOrInvalidAsync(model, cancellationToken);
        }

        return View("AccountResult", new AccountResultViewModel(
            "Email verified",
            "Your account is verified. You can now sign in.",
            true));
    }

    [HttpPost("/account/verify-email/resend")]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> ResendVerificationCode(
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        var pending = await emailVerification.FindPendingAsync(challengeId, cancellationToken);
        if (pending is null)
        {
            return InvalidLink("That verification request is no longer available.");
        }

        var user = await userManager.FindByEmailAsync(pending.Email);
        if (user is null)
        {
            return InvalidLink("That verification request is no longer available.");
        }

        try
        {
            var result = await emailVerification.IssueAsync(
                user,
                VerificationUrl,
                cancellationToken);
            TempData[result.Succeeded ? "Success" : "Error"] = result.Message;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Verification-code resend delivery failed.");
            TempData["Error"] = "The code could not be delivered. Try again shortly.";
        }

        return RedirectToAction(nameof(VerifyEmail), new { challengeId });
    }

    [HttpGet("/account/reset-password")]
    public IActionResult ResetPassword(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return InvalidLink("That password-reset link is incomplete.");
        }

        return View(new ResetPasswordViewModel { UserId = userId, Code = code });
    }

    [HttpPost("/account/reset-password")]
    [EnableRateLimiting("account")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByIdAsync(model.UserId);
        if (user is null || !TryDecodeToken(model.Code, out var token))
        {
            return InvalidLink("That password-reset link is invalid or has expired.");
        }

        var result = await userManager.ResetPasswordAsync(user, token, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(model.Password), error.Description);
            }

            return View(model);
        }

        return View("AccountResult", new AccountResultViewModel(
            "Password changed",
            "Your password has been changed. You can now sign in with the new password.",
            true));
    }

    [HttpGet("/account/access-denied")]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("AccountResult", new AccountResultViewModel(
            "Access denied",
            "You do not have permission to open that page.",
            false));
    }

    private IActionResult InvalidLink(string message)
    {
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return View("AccountResult", new AccountResultViewModel("Link unavailable", message, false));
    }

    private string SafeReturnUrl(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? returnUrl : Url.Action("Dashboard", "Home")!;

    private string VerificationUrl(Guid challengeId) =>
        Url.Action(
            nameof(VerifyEmail),
            "Account",
            new { challengeId },
            Request.Scheme) ?? throw new InvalidOperationException(
            "Could not create the email-verification URL.");

    private async Task<EmailVerificationViewModel?> BuildVerificationModelAsync(
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        var pending = await emailVerification.FindPendingAsync(challengeId, cancellationToken);
        return pending is null
            ? null
            : new EmailVerificationViewModel
            {
                ChallengeId = pending.ChallengeId,
                Email = pending.Email,
                CodeExpiresAt = pending.CodeExpiresAt,
                ResendAvailableInSeconds = pending.ResendAvailableInSeconds,
            };
    }

    private async Task<IActionResult> VerificationViewOrInvalidAsync(
        EmailVerificationViewModel model,
        CancellationToken cancellationToken)
    {
        var refreshed = await BuildVerificationModelAsync(model.ChallengeId, cancellationToken);
        if (refreshed is null)
        {
            return InvalidLink("That verification request is no longer available.");
        }

        refreshed.Code = model.Code;
        return View(nameof(VerifyEmail), refreshed);
    }

    private void AddRegistrationErrors(
        RegisterViewModel model,
        IEnumerable<IdentityError> errors)
    {
        foreach (var error in errors)
        {
            if (error.Code.StartsWith("Password", StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(model.Password), error.Description);
            }
            else if (error.Code is "DuplicateEmail" or "DuplicateUserName" or "InvalidEmail" or "InvalidUserName")
            {
                ModelState.AddModelError(nameof(model.Email), error.Description);
            }
            else
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
    }

    private static string EncodeToken(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static bool TryDecodeToken(string encodedToken, out string token)
    {
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken));
            return true;
        }
        catch (FormatException)
        {
            token = string.Empty;
            return false;
        }
    }
}
