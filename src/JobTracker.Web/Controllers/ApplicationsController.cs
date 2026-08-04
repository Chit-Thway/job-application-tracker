using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ApplicationsController(
    ApplicationTrackerService applications,
    CompanyTrackerService companies,
    TimeProvider timeProvider) : Controller
{
    [HttpGet("/applications")]
    public async Task<IActionResult> Index(
        ApplicationFilterViewModel filters,
        CancellationToken cancellationToken)
    {
        SetSection();
        filters.Sort = filters.Sort is "oldest" or "title" or "company"
            ? filters.Sort
            : "newest";
        var items = await applications.SearchAsync(
            new ApplicationSearch(
                filters.Query,
                filters.CompanyId,
                filters.Stage,
                filters.Outcome,
                filters.IsSavedForever,
                filters.AppliedFrom,
                filters.AppliedTo,
                filters.Sort),
            cancellationToken);
        return View(new ApplicationIndexViewModel(
            filters,
            items,
            await companies.ListOptionsAsync(cancellationToken)));
    }

    [HttpGet("/applications/new")]
    public async Task<IActionResult> Create(
        Guid? companyId,
        CancellationToken cancellationToken)
    {
        SetSection("add");
        var perth = TimeZoneInfo.FindSystemTimeZoneById("Australia/Perth");
        var localNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), perth);
        return View(await PopulateCompaniesAsync(new ApplicationFormViewModel
        {
            AppliedOn = DateOnly.FromDateTime(localNow.DateTime),
            CompanyId = companyId,
        }, cancellationToken));
    }

    [HttpPost("/applications/new")]
    public async Task<IActionResult> Create(
        ApplicationFormViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection("add");
        if (!ModelState.IsValid)
        {
            return View(await PopulateCompaniesAsync(model, cancellationToken));
        }

        var result = await applications.CreateAsync(InputFrom(model), cancellationToken);
        if (result.Result == ApplicationWriteResult.InvalidCompany)
        {
            ModelState.AddModelError(nameof(model.CompanyId), "Choose one of your own companies.");
            return View(await PopulateCompaniesAsync(model, cancellationToken));
        }

        TempData["Success"] = "Application added.";
        return RedirectToAction(nameof(Details), new { id = result.Id });
    }

    [HttpGet("/applications/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var application = await applications.FindAsync(id, cancellationToken);
        return application is null ? NotFound() : View(application);
    }

    [HttpGet("/applications/{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var application = await applications.FindAsync(id, cancellationToken);
        if (application is null)
        {
            return NotFound();
        }

        return View(await PopulateCompaniesAsync(new ApplicationFormViewModel
        {
            Id = application.Id,
            RoleTitle = application.RoleTitle,
            CompanyId = application.CompanyId,
            AppliedOn = application.AppliedOn,
            SourceUrl = application.SourceUrl,
            Notes = application.Notes,
            IsSavedForever = application.IsSavedForever,
        }, cancellationToken));
    }

    [HttpPost("/applications/{id:guid}/edit")]
    public async Task<IActionResult> Edit(
        Guid id,
        ApplicationFormViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        model.Id = id;
        if (!ModelState.IsValid)
        {
            return View(await PopulateCompaniesAsync(model, cancellationToken));
        }

        var result = await applications.UpdateAsync(id, InputFrom(model), cancellationToken);
        if (result == ApplicationWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result == ApplicationWriteResult.InvalidCompany)
        {
            ModelState.AddModelError(nameof(model.CompanyId), "Choose one of your own companies.");
            return View(await PopulateCompaniesAsync(model, cancellationToken));
        }

        TempData["Success"] = "Application updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("/applications/{id:guid}/saved")]
    public async Task<IActionResult> SetSavedForever(
        Guid id,
        bool isSavedForever,
        string? returnTo,
        CancellationToken cancellationToken)
    {
        var result = await applications.SetSavedForeverAsync(id, isSavedForever, cancellationToken);
        if (result == ApplicationWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = isSavedForever
            ? "Application saved."
            : "Application is no longer saved.";
        return string.Equals(returnTo, "index", StringComparison.Ordinal)
            ? RedirectToAction(nameof(Index))
            : RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("/applications/{id:guid}/delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var application = await applications.FindAsync(id, cancellationToken);
        return application is null
            ? NotFound()
            : View(new ApplicationDeleteViewModel(application));
    }

    [HttpPost("/applications/{id:guid}/delete")]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        var result = await applications.DeleteAsync(id, cancellationToken);
        if (result == ApplicationWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = "Application deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ApplicationFormViewModel> PopulateCompaniesAsync(
        ApplicationFormViewModel model,
        CancellationToken cancellationToken)
    {
        model.Companies = await companies.ListOptionsAsync(cancellationToken);
        return model;
    }

    private static ApplicationInput InputFrom(ApplicationFormViewModel model) => new(
        model.CompanyId,
        model.RoleTitle,
        model.AppliedOn!.Value,
        model.SourceUrl,
        model.Notes,
        model.IsSavedForever);

    private void SetSection(string section = "applications") => ViewData["Section"] = section;
}
