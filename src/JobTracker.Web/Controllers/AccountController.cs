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
    InvitationRegistrationService invitationRegistration) : Controller
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

        var result = await invitationRegistration.RegisterAsync(
            model.InvitationCode,
            model.DisplayName,
            model.Email,
            model.Password,
            HttpContext.RequestAborted);

        if (!result.Succeeded || result.User is null)
        {
            var passwordErrors = result.IdentityErrors
                .Where(error => error.Code.StartsWith("Password", StringComparison.Ordinal))
                .ToList();

            foreach (var error in passwordErrors)
            {
                ModelState.AddModelError(nameof(model.Password), error.Description);
            }

            if (passwordErrors.Count == 0)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Registration could not be completed. Check your invitation code and details.");
            }

            return View(model);
        }

        var token = await userManager.GenerateEmailConfirmationTokenAsync(result.User);
        var verificationUrl = Url.Action(
            nameof(ConfirmEmail),
            "Account",
            new { userId = result.User.Id, code = EncodeToken(token) },
            Request.Scheme) ?? throw new InvalidOperationException("Could not create verification URL.");
        await emailSender.SendVerificationAsync(result.User, verificationUrl);

        return View("AccountResult", new AccountResultViewModel(
            "Check your messages",
            "Your account was created. Open the verification message before signing in.",
            true));
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
            var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
            var verificationUrl = Url.Action(
                nameof(ConfirmEmail),
                "Account",
                new { userId = user.Id, code = EncodeToken(token) },
                Request.Scheme) ?? throw new InvalidOperationException("Could not create verification URL.");
            await emailSender.SendVerificationAsync(user, verificationUrl);
        }

        return View("AccountResult", new AccountResultViewModel(
            "Check your messages",
            "If an unverified account matches that email address, a new verification message has been sent.",
            true));
    }

    [HttpGet("/account/confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return InvalidLink("That verification link is incomplete.");
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !TryDecodeToken(code, out var token))
        {
            return InvalidLink("That verification link is invalid or has expired.");
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            return InvalidLink("That verification link is invalid or has expired.");
        }

        return View("AccountResult", new AccountResultViewModel(
            "Email verified",
            "Your account is verified. You can now sign in.",
            true));
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
