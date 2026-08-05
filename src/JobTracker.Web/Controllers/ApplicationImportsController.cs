using JobTracker.Web.Extraction;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ApplicationImportsController(
    ExtractionDraftService drafts,
    TimeProvider timeProvider) : Controller
{
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

    [HttpGet("/applications/import/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var draft = await drafts.FindReviewAsync(id, cancellationToken);
        return draft is null ? NotFound() : View(ModelFrom(draft));
    }

    [HttpPost("/applications/import/{id:guid}/review")]
    public async Task<IActionResult> Review(
        Guid id,
        ExtractionReviewViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        model.DraftId = id;
        if (string.IsNullOrWhiteSpace(model.CompanyName)
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
            return View(model);
        }

        var completion = await drafts.CompleteAsync(
            id,
            new ExtractionReviewInput(
                model.RoleTitle,
                model.CompanyName,
                model.CompanyLocation,
                model.AppliedOn!.Value,
                model.WorkplaceMode,
                model.SourceUrl,
                model.SourceSite,
                model.JobReference,
                model.SalaryText,
                model.EmploymentType,
                model.ClosingDate,
                model.ContactName,
                model.ContactEmail,
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
    };

    private static void CopyReviewContext(
        ExtractionDraftReview draft,
        ExtractionReviewViewModel model)
    {
        model.SourceText = draft.SourceText;
        model.ExpiresAt = draft.ExpiresAt;
        model.Evidence = draft.Evidence;
        model.Warnings = draft.Warnings;
    }

    private void SetSection() => ViewData["Section"] = "add";
}
