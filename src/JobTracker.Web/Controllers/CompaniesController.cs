using JobTracker.Web.Applications;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class CompaniesController(CompanyTrackerService companies) : Controller
{
    [HttpGet("/companies")]
    public async Task<IActionResult> Index(
        CompanyFilterViewModel filters,
        CancellationToken cancellationToken)
    {
        SetSection();
        return View(new CompanyIndexViewModel(
            filters,
            await companies.SearchAsync(filters.Query, cancellationToken)));
    }

    [HttpGet("/companies/new")]
    public IActionResult Create()
    {
        SetSection();
        return View(new CompanyFormViewModel());
    }

    [HttpPost("/companies/new")]
    public async Task<IActionResult> Create(
        CompanyFormViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await companies.CreateAsync(InputFrom(model), cancellationToken);
        if (result.Result == CompanyWriteResult.Duplicate)
        {
            ModelState.AddModelError(
                nameof(model.Name),
                "You already have a company with this name and location. Edit that company instead.");
            return View(model);
        }

        TempData["Success"] = "Company added.";
        return RedirectToAction(nameof(Details), new { id = result.Id });
    }

    [HttpGet("/companies/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var company = await companies.FindAsync(id, cancellationToken);
        return company is null ? NotFound() : View(company);
    }

    [HttpGet("/companies/{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var company = await companies.FindAsync(id, cancellationToken);
        if (company is null)
        {
            return NotFound();
        }

        return View(new CompanyFormViewModel
        {
            Id = company.Id,
            Name = company.Name,
            Location = company.Location,
            Website = company.Website,
            Notes = company.Notes,
        });
    }

    [HttpPost("/companies/{id:guid}/edit")]
    public async Task<IActionResult> Edit(
        Guid id,
        CompanyFormViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        model.Id = id;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await companies.UpdateAsync(id, InputFrom(model), cancellationToken);
        if (result.Result == CompanyWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result.Result == CompanyWriteResult.Duplicate)
        {
            ModelState.AddModelError(
                nameof(model.Name),
                "You already have a company with this name and location. The records were not merged.");
            return View(model);
        }

        TempData["Success"] = "Company updated.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("/companies/{id:guid}/delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var company = await companies.FindAsync(id, cancellationToken);
        return company is null
            ? NotFound()
            : View(new CompanyDeleteViewModel(company));
    }

    [HttpPost("/companies/{id:guid}/delete")]
    public async Task<IActionResult> DeleteConfirmed(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var result = await companies.DeleteAsync(id, cancellationToken);
        if (result == CompanyWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result == CompanyWriteResult.InUse)
        {
            var company = await companies.FindAsync(id, cancellationToken);
            if (company is null)
            {
                return NotFound();
            }

            Response.StatusCode = StatusCodes.Status409Conflict;
            return View("Delete", new CompanyDeleteViewModel(
                company,
                "This company is still linked to applications. Move or delete those applications first."));
        }

        TempData["Success"] = "Company deleted.";
        return RedirectToAction(nameof(Index));
    }

    private static CompanyInput InputFrom(CompanyFormViewModel model) => new(
        model.Name,
        model.Location,
        model.Website,
        model.Notes);

    private void SetSection() => ViewData["Section"] = "companies";
}
