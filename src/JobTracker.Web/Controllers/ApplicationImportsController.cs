using JobTracker.Web.Extraction;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobTracker.Web.Controllers;

[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ApplicationImportsController(
    ExtractionDraftService drafts,
    JobPostingUrlImportService urlImports,
    BrowserExtensionImportService browserExtensionImports,
    ExtensionCaptureHandoffService extensionCaptureHandoffs,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("/applications/import/url")]
    public IActionResult ImportUrl()
    {
        SetSection();
        return View(new JobPostingUrlInputViewModel());
    }

    [HttpPost("/applications/import/url")]
    [EnableRateLimiting("imports")]
    [RequestSizeLimit(32_000)]
    public async Task<IActionResult> ImportUrl(
        JobPostingUrlInputViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await urlImports.ImportAsync(
            model.SourceUrl,
            LocalToday(),
            cancellationToken);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(nameof(model.SourceUrl), UrlFailureMessage(result.Failure));
            return View(model);
        }

        return RedirectToAction(nameof(Review), new { id = result.DraftId });
    }

    [HttpGet("/applications/import/text")]
    public IActionResult PasteText()
    {
        SetSection();
        return View(new PastedTextInputViewModel());
    }

    [HttpPost("/applications/import/text")]
    [RequestSizeLimit(512_000)]
    public async Task<IActionResult> PasteText(
        PastedTextInputViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var draftId = await drafts.CreatePastedTextDraftAsync(
            model.SourceText,
            LocalToday(),
            cancellationToken);
        return RedirectToAction(nameof(Review), new { id = draftId });
    }

    [HttpGet("/applications/import/extension")]
    public async Task<IActionResult> ImportExtension(
        string? token,
        string? handoffError,
        CancellationToken cancellationToken)
    {
        SetSection();

        if (!string.IsNullOrWhiteSpace(handoffError))
        {
            ModelState.AddModelError(
                string.Empty,
                "That browser capture could not be accepted. Return to the job page and capture it again.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return View(new BrowserExtensionCaptureInputViewModel());
        }

        var redeemed = await extensionCaptureHandoffs.RedeemAsync(token, cancellationToken);
        if (redeemed.Status != ExtensionCaptureHandoffStatus.Success)
        {
            ModelState.AddModelError(
                string.Empty,
                redeemed.Error
                    ?? "That browser capture is unavailable. Return to the job page and capture it again.");
            return View(new BrowserExtensionCaptureInputViewModel());
        }

        var draftId = await drafts.CreateBrowserExtensionDraftAsync(
            redeemed.Extraction!,
            LocalToday(),
            cancellationToken);
        return RedirectToAction(nameof(Review), new { id = draftId });
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("/applications/import/extension/handoff")]
    [EnableRateLimiting("imports")]
    [RequestSizeLimit(512_000)]
    public async Task<IActionResult> CreateExtensionHandoff(
        BrowserExtensionCaptureInputViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToAction(nameof(ImportExtension), new { handoffError = "invalid" });
        }

        var created = await extensionCaptureHandoffs.CreateAsync(
            model.PayloadJson,
            cancellationToken);
        return created.Status == ExtensionCaptureHandoffStatus.Success
            ? RedirectToAction(nameof(ImportExtension), new { token = created.Token })
            : RedirectToAction(nameof(ImportExtension), new { handoffError = "invalid" });
    }

    [HttpPost("/applications/import/extension")]
    [EnableRateLimiting("imports")]
    [RequestSizeLimit(256_000)]
    public async Task<IActionResult> ImportExtension(
        BrowserExtensionCaptureInputViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = browserExtensionImports.Import(model.PayloadJson);
        if (!result.IsSuccess)
        {
            ModelState.AddModelError(nameof(model.PayloadJson), result.Error!);
            return View(model);
        }

        var draftId = await drafts.CreateBrowserExtensionDraftAsync(
            result.Extraction!,
            LocalToday(),
            cancellationToken);
        return RedirectToAction(nameof(Review), new { id = draftId });
    }

    [HttpGet("/applications/import/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var draft = await drafts.FindReviewAsync(id, cancellationToken);
        if (draft is null)
        {
            return NotFound();
        }

        var model = ModelFrom(draft);
        await PopulateCompanySuggestionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("/applications/import/{id:guid}/review")]
    public async Task<IActionResult> Review(
        Guid id,
        ExtractionReviewViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        model.DraftId = id;
        if (model.ExistingCompanyId is null
            && string.IsNullOrWhiteSpace(model.CompanyName)
            && !string.IsNullOrWhiteSpace(model.CompanyLocation))
        {
            ModelState.AddModelError(
                nameof(model.CompanyName),
                "Enter a company name before adding a company location.");
        }

        if (!ModelState.IsValid)
        {
            var existingDraft = await drafts.FindReviewAsync(id, cancellationToken);
            if (existingDraft is null)
            {
                return NotFound();
            }

            CopyReviewContext(existingDraft, model);
            await PopulateCompanySuggestionsAsync(model, cancellationToken);
            return View(model);
        }

        var completion = await drafts.CompleteAsync(
            id,
            new ExtractionReviewInput(
                model.RoleTitle,
                model.CompanyName,
                model.CompanyLocation,
                model.ExistingCompanyId,
                model.AppliedOn!.Value,
                model.WorkplaceMode,
                model.SourceUrl,
                model.ApplicationPortalUrl,
                model.SourceSite,
                model.JobReference,
                model.SalaryText,
                model.EmploymentType,
                model.ClosingDate,
                model.ContactName,
                model.ContactEmail,
                model.DescriptionText,
                model.Notes,
                model.IsSavedForever),
            cancellationToken);

        if (completion.Result == ExtractionDraftResult.NotFound)
        {
            return NotFound();
        }

        if (completion.Result == ExtractionDraftResult.Expired)
        {
            TempData["Error"] = "That review draft expired. Paste the job text again to create a fresh review.";
            return RedirectToAction(nameof(PasteText));
        }

        if (completion.Result == ExtractionDraftResult.InvalidCompanySelection)
        {
            ModelState.AddModelError(
                nameof(model.ExistingCompanyId),
                "That company is no longer available. Choose another match or keep the extracted company.");
            var existingDraft = await drafts.FindReviewAsync(id, cancellationToken);
            if (existingDraft is null)
            {
                return NotFound();
            }

            CopyReviewContext(existingDraft, model);
            await PopulateCompanySuggestionsAsync(model, cancellationToken);
            return View(model);
        }

        TempData["Success"] = "Reviewed application added.";
        return RedirectToAction(
            nameof(ApplicationsController.Details),
            "Applications",
            new { id = completion.ApplicationId });
    }

    [HttpPost("/applications/import/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await drafts.CancelAsync(id, cancellationToken);
        if (result == ExtractionDraftResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = "Import cancelled. No application was created.";
        return RedirectToAction(nameof(ApplicationsController.Index), "Applications");
    }

    private DateOnly LocalToday()
    {
        var perth = TimeZoneInfo.FindSystemTimeZoneById("Australia/Perth");
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), perth);
        return DateOnly.FromDateTime(localNow.DateTime);
    }

    private static ExtractionReviewViewModel ModelFrom(ExtractionDraftReview draft) => new()
    {
        DraftId = draft.Id,
        SourceType = draft.SourceType,
        SourceText = draft.SourceText,
        ExpiresAt = draft.ExpiresAt,
        Evidence = draft.Evidence,
        Warnings = draft.Warnings,
        RoleTitle = draft.Fields.RoleTitle ?? string.Empty,
        CompanyName = draft.Fields.CompanyName,
        CompanyLocation = draft.Fields.CompanyLocation,
        AppliedOn = draft.Fields.AppliedOn,
        WorkplaceMode = draft.Fields.WorkplaceMode,
        SourceUrl = draft.Fields.SourceUrl,
        SourceSite = draft.Fields.SourceSite,
        JobReference = draft.Fields.JobReference,
        SalaryText = draft.Fields.SalaryText,
        EmploymentType = draft.Fields.EmploymentType,
        ClosingDate = draft.Fields.ClosingDate,
        ContactName = draft.Fields.ContactName,
        ContactEmail = draft.Fields.ContactEmail,
        DescriptionText = draft.Fields.DescriptionText,
    };

    private static void CopyReviewContext(
        ExtractionDraftReview draft,
        ExtractionReviewViewModel model)
    {
        model.SourceText = draft.SourceText;
        model.SourceType = draft.SourceType;
        model.ExpiresAt = draft.ExpiresAt;
        model.Evidence = draft.Evidence;
        model.Warnings = draft.Warnings;
    }

    private async Task PopulateCompanySuggestionsAsync(
        ExtractionReviewViewModel model,
        CancellationToken cancellationToken)
    {
        model.CompanySuggestions = await drafts.FindCompanySuggestionsAsync(
            model.CompanyName,
            cancellationToken);
    }

    private void SetSection() => ViewData["Section"] = "add";

    private static string UrlFailureMessage(JobPostingImportFailure failure) => failure switch
    {
        JobPostingImportFailure.InvalidUrl => "Enter a complete public HTTP or HTTPS job-posting URL.",
        JobPostingImportFailure.BlockedDestination => "That address points to a network destination the importer is not allowed to contact.",
        JobPostingImportFailure.CouldNotResolve => "We could not find that public website. Check the address and try again.",
        JobPostingImportFailure.TimedOut => "The job site took too long to respond. Paste the job text or use manual entry instead.",
        JobPostingImportFailure.TooManyRedirects => "The job site redirected too many times. Paste the job text or use manual entry instead.",
        JobPostingImportFailure.ResponseTooLarge => "That page is too large to import safely. Paste the relevant job text instead.",
        JobPostingImportFailure.UnsupportedContentType => "That address did not return a normal HTML job page. Paste the job text instead.",
        JobPostingImportFailure.EmptyContent => "The page did not contain readable job text. Paste the job text instead.",
        _ => "We could not safely read that job page. It may block automated access; paste the job text or use manual entry instead.",
    };
}
