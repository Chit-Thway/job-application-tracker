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
    ApplicationWorkflowService workflow,
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
        if (filters.Stage is not null && !ApplicationDisplay.IsActive(filters.Stage.Value))
        {
            filters.Stage = ApplicationDisplay.NormalizeStage(filters.Stage.Value);
        }

        if (filters.Outcome is not null && !ApplicationDisplay.IsActive(filters.Outcome.Value))
        {
            filters.Outcome = ApplicationDisplay.NormalizeOutcome(filters.Outcome.Value);
        }

        var items = await applications.SearchAsync(
            new ApplicationSearch(
                filters.Query,
                filters.CompanyId,
                filters.Stage,
                filters.Outcome,
                filters.IsSavedForever,
                filters.IsDeletionScheduled,
                filters.AppliedFrom,
                filters.AppliedTo,
                filters.Sort),
            cancellationToken);
        return View(new ApplicationIndexViewModel(
            filters,
            items,
            await companies.ListOptionsAsync(cancellationToken),
            await applications.GetCurrentTimeZoneIdAsync(cancellationToken)));
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

        if (result.Result == ApplicationWriteResult.LimitReached)
        {
            ModelState.AddModelError(string.Empty, ApplicationQuotaService.LimitReachedMessage);
            return View(await PopulateCompaniesAsync(model, cancellationToken));
        }

        TempData["Success"] = "Application added.";
        return RedirectToAction(nameof(Details), new { id = result.Id });
    }

    [HttpGet("/applications/{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken)
    {
        SetSection();
        var page = await BuildWorkflowPageAsync(id, cancellationToken);
        return page is null ? NotFound() : View(page);
    }

    [HttpPost("/applications/{id:guid}/status")]
    public async Task<IActionResult> TransitionStatus(
        Guid id,
        [Bind(Prefix = nameof(ApplicationWorkflowPageViewModel.Status))]
        StatusTransitionFormViewModel model,
        string? returnTo,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ApplicationDisplay.IsActive(model.Stage) || !ApplicationDisplay.IsActive(model.Outcome))
        {
            ModelState.AddModelError(
                $"{nameof(ApplicationWorkflowPageViewModel.Status)}.{nameof(model.Stage)}",
                "Choose one of the available pipeline stages and outcomes.");
        }

        if (!ModelState.IsValid)
        {
            if (returnTo == "index")
            {
                TempData["Error"] = "Choose one of the available pipeline stages and outcomes.";
                return RedirectToAction(nameof(Index));
            }

            return await WorkflowViewAsync(
                id,
                page => page.Status = model,
                cancellationToken);
        }

        var result = await workflow.TransitionAsync(
            id,
            new StatusTransitionInput(model.Stage, model.Outcome, model.Note),
            cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result == WorkflowWriteResult.InvalidTransition)
        {
            if (returnTo == "index")
            {
                TempData["Error"] = "That status change is not available. Ghosted can be confirmed only after 30 days, and the new state must differ from the current state.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError(
                $"{nameof(ApplicationWorkflowPageViewModel.Status)}.{nameof(model.Stage)}",
                "Choose a valid change. Ghosted can be confirmed only after 30 days, and the new state must differ from the current state.");
            return await WorkflowViewAsync(
                id,
                page => page.Status = model,
                cancellationToken);
        }

        TempData["Success"] = "Application status updated and added to the history.";
        if (returnTo == "index")
        {
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Details), null, new { id }, "history");
    }

    [HttpPost("/applications/{id:guid}/contacts")]
    public async Task<IActionResult> AddContact(
        Guid id,
        [Bind(Prefix = nameof(ApplicationWorkflowPageViewModel.Contact))]
        ContactFormViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ModelState.IsValid)
        {
            return await WorkflowViewAsync(
                id,
                page => page.Contact = model,
                cancellationToken);
        }

        var result = await workflow.AddContactAsync(
            id,
            new ContactInput(
                model.Name,
                model.JobTitle,
                model.Email,
                model.Phone,
                model.Notes),
            cancellationToken);
        if (result.Result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = "Contact added.";
        return RedirectToAction(nameof(Details), null, new { id }, "contacts");
    }

    [HttpPost("/applications/{id:guid}/contacts/{contactId:guid}/delete")]
    public async Task<IActionResult> DeleteContact(
        Guid id,
        Guid contactId,
        CancellationToken cancellationToken)
    {
        var result = await workflow.DeleteContactAsync(id, contactId, cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData[result == WorkflowWriteResult.ContactInUse ? "Error" : "Success"] =
            result == WorkflowWriteResult.ContactInUse
                ? "Delete or correct this contact's interactions before removing the contact."
                : "Contact deleted.";
        return RedirectToAction(nameof(Details), null, new { id }, "contacts");
    }

    [HttpPost("/applications/{id:guid}/interactions")]
    public async Task<IActionResult> AddInteraction(
        Guid id,
        [Bind(Prefix = nameof(ApplicationWorkflowPageViewModel.Interaction))]
        InteractionFormViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ModelState.IsValid)
        {
            return await WorkflowViewAsync(
                id,
                page => page.Interaction = model,
                cancellationToken);
        }

        var result = await workflow.AddInteractionAsync(
            id,
            InteractionInputFrom(model),
            cancellationToken);
        if (result.Result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result.Result == WorkflowWriteResult.InvalidRelationship)
        {
            return BadRequest("Choose a contact linked to this application.");
        }

        if (result.Result == WorkflowWriteResult.InvalidLocalTime)
        {
            ModelState.AddModelError(
                $"{nameof(ApplicationWorkflowPageViewModel.Interaction)}.{nameof(model.OccurredAtLocal)}",
                "Choose a valid local date and time for your configured timezone.");
            return await WorkflowViewAsync(
                id,
                page => page.Interaction = model,
                cancellationToken);
        }

        TempData["Success"] = model.IsEmployerResponse
            ? "Employer response recorded."
            : "Interaction recorded.";
        return RedirectToAction(nameof(Details), null, new { id }, "history");
    }

    [HttpPost("/applications/{id:guid}/interactions/{interactionId:guid}/edit")]
    public async Task<IActionResult> UpdateInteraction(
        Guid id,
        Guid interactionId,
        InteractionFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "The interaction could not be corrected. Check its date and fields.";
            return RedirectToAction(nameof(Details), null, new { id }, "history");
        }

        var result = await workflow.UpdateInteractionAsync(
            id,
            interactionId,
            InteractionInputFrom(model),
            cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result == WorkflowWriteResult.InvalidRelationship)
        {
            return BadRequest("Choose a contact linked to this application.");
        }

        if (result == WorkflowWriteResult.InvalidLocalTime)
        {
            TempData["Error"] = "Choose a valid local date and time for your configured timezone.";
            return RedirectToAction(nameof(Details), null, new { id }, "history");
        }

        TempData["Success"] = "Interaction corrected. Response timing has been recalculated.";
        return RedirectToAction(nameof(Details), null, new { id }, "history");
    }

    [HttpPost("/applications/{id:guid}/interactions/{interactionId:guid}/delete")]
    public async Task<IActionResult> DeleteInteraction(
        Guid id,
        Guid interactionId,
        CancellationToken cancellationToken)
    {
        var result = await workflow.DeleteInteractionAsync(id, interactionId, cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = "Interaction deleted. Response timing has been recalculated.";
        return RedirectToAction(nameof(Details), null, new { id }, "history");
    }

    [HttpPost("/applications/{id:guid}/tasks")]
    public async Task<IActionResult> AddTask(
        Guid id,
        [Bind(Prefix = nameof(ApplicationWorkflowPageViewModel.Task))]
        WorkflowTaskFormViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ModelState.IsValid)
        {
            return await WorkflowViewAsync(
                id,
                page => page.Task = model,
                cancellationToken);
        }

        var result = await workflow.AddTaskAsync(
            id,
            new WorkflowTaskInput(model.Title, model.DueAtLocal, model.Notes),
            cancellationToken);
        if (result.Result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result.Result == WorkflowWriteResult.InvalidLocalTime)
        {
            ModelState.AddModelError(
                $"{nameof(ApplicationWorkflowPageViewModel.Task)}.{nameof(model.DueAtLocal)}",
                "Choose a valid local due date and time for your configured timezone.");
            return await WorkflowViewAsync(
                id,
                page => page.Task = model,
                cancellationToken);
        }

        TempData["Success"] = "Task added.";
        return RedirectToAction(nameof(Details), null, new { id }, "tasks");
    }

    [HttpPost("/applications/{id:guid}/tasks/{taskId:guid}/completed")]
    public async Task<IActionResult> SetTaskCompleted(
        Guid id,
        Guid taskId,
        bool completed,
        CancellationToken cancellationToken)
    {
        var result = await workflow.SetTaskCompletedAsync(
            id,
            taskId,
            completed,
            cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = completed ? "Task completed." : "Task reopened.";
        return RedirectToAction(nameof(Details), null, new { id }, "tasks");
    }

    [HttpPost("/applications/{id:guid}/tasks/{taskId:guid}/delete")]
    public async Task<IActionResult> DeleteTask(
        Guid id,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var result = await workflow.DeleteTaskAsync(id, taskId, cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = "Task deleted.";
        return RedirectToAction(nameof(Details), null, new { id }, "tasks");
    }

    [HttpPost("/applications/{id:guid}/appointments")]
    public async Task<IActionResult> AddAppointment(
        Guid id,
        [Bind(Prefix = nameof(ApplicationWorkflowPageViewModel.Appointment))]
        AppointmentFormViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!ModelState.IsValid)
        {
            return await WorkflowViewAsync(
                id,
                page => page.Appointment = model,
                cancellationToken);
        }

        var result = await workflow.AddAppointmentAsync(
            id,
            new WorkflowAppointmentInput(
                model.Type,
                model.StartsAtLocal!.Value,
                model.EndsAtLocal!.Value,
                model.LocationOrLink,
                model.Notes),
            cancellationToken);
        if (result.Result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result.Result == WorkflowWriteResult.InvalidLocalTime)
        {
            ModelState.AddModelError(
                $"{nameof(ApplicationWorkflowPageViewModel.Appointment)}.{nameof(model.EndsAtLocal)}",
                "Choose a valid time range in your configured timezone.");
            return await WorkflowViewAsync(
                id,
                page => page.Appointment = model,
                cancellationToken);
        }

        TempData["Success"] = "Appointment added.";
        return RedirectToAction(nameof(Details), null, new { id }, "appointments");
    }

    [HttpPost("/applications/{id:guid}/appointments/{appointmentId:guid}/delete")]
    public async Task<IActionResult> DeleteAppointment(
        Guid id,
        Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var result = await workflow.DeleteAppointmentAsync(
            id,
            appointmentId,
            cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = "Appointment deleted.";
        return RedirectToAction(nameof(Details), null, new { id }, "appointments");
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
            ApplicationPortalUrl = application.ApplicationPortalUrl,
            DescriptionText = application.DescriptionText,
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
            ? "Application saved. Any pending automatic deletion was cancelled."
            : "Application is no longer saved. If it is already old enough, a fresh 14-day grace period has started.";
        return returnTo switch
        {
            "index" => RedirectToAction(nameof(Index)),
            "dashboard" => RedirectToAction("Dashboard", "Home"),
            "actions" => RedirectToAction("ActionCentre", "Home"),
            _ => RedirectToAction(nameof(Details), new { id }),
        };
    }

    [HttpPost("/applications/bulk/delete")]
    public async Task<IActionResult> ReviewBulkDelete(
        BulkApplicationActionViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!NormalizeSelection(model))
        {
            return RedirectToAction(nameof(Index));
        }

        var selected = await applications.FindSelectedAsync(
            model.SelectedApplicationIds,
            cancellationToken);
        if (selected.Count == 0)
        {
            TempData["Error"] = "None of the selected applications could be found.";
            return RedirectToAction(nameof(Index));
        }

        return View("BulkDelete", new BulkApplicationDeleteViewModel(selected));
    }

    [HttpPost("/applications/bulk/delete/confirm")]
    public async Task<IActionResult> ConfirmBulkDelete(
        BulkApplicationActionViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!NormalizeSelection(model))
        {
            return RedirectToAction(nameof(Index));
        }

        var result = await applications.BulkDeleteAsync(
            model.SelectedApplicationIds,
            cancellationToken);
        TempData[result.ChangedCount == 0 ? "Error" : "Success"] = result.ChangedCount == 0
            ? "None of the selected applications could be deleted."
            : $"Deleted {result.ChangedCount} selected application{(result.ChangedCount == 1 ? string.Empty : "s")} and their linked records.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/applications/bulk/stage")]
    public async Task<IActionResult> BulkChangeStage(
        BulkApplicationActionViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!NormalizeSelection(model))
        {
            return RedirectToAction(nameof(Index));
        }

        if (model.Stage is null || !ApplicationDisplay.IsActive(model.Stage.Value))
        {
            TempData["Error"] = "Choose a valid pipeline stage for the selected applications.";
            return RedirectToAction(nameof(Index));
        }

        var result = await applications.BulkChangeStageAsync(
            model.SelectedApplicationIds,
            model.Stage.Value,
            cancellationToken);
        TempData[result.MatchedCount == 0 ? "Error" : "Success"] = result.MatchedCount == 0
            ? "None of the selected applications could be found."
            : result.ChangedCount == 0
                ? "The selected applications were already at that pipeline stage."
                : $"Changed the pipeline stage for {result.ChangedCount} application{(result.ChangedCount == 1 ? string.Empty : "s")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/applications/bulk/note")]
    public async Task<IActionResult> BulkAddNote(
        BulkApplicationActionViewModel model,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!NormalizeSelection(model))
        {
            return RedirectToAction(nameof(Index));
        }

        var note = model.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note) || note.Length > 2_000)
        {
            TempData["Error"] = "Enter a note between 1 and 2,000 characters.";
            return RedirectToAction(nameof(Index));
        }

        var result = await applications.BulkAddNoteAsync(
            model.SelectedApplicationIds,
            note,
            cancellationToken);
        TempData[result.ChangedCount == 0 ? "Error" : "Success"] = result.ChangedCount == 0
            ? "None of the selected applications could be found."
            : $"Added the note to {result.ChangedCount} application histor{(result.ChangedCount == 1 ? "y" : "ies")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/applications/bulk/saved")]
    public async Task<IActionResult> BulkSetSavedForever(
        BulkApplicationActionViewModel model,
        bool isSavedForever,
        CancellationToken cancellationToken)
    {
        SetSection();
        if (!NormalizeSelection(model))
        {
            return RedirectToAction(nameof(Index));
        }

        var result = await applications.BulkSetSavedForeverAsync(
            model.SelectedApplicationIds,
            isSavedForever,
            cancellationToken);
        TempData[result.MatchedCount == 0 ? "Error" : "Success"] = result.MatchedCount == 0
            ? "None of the selected applications could be found."
            : result.ChangedCount == 0
                ? $"The selected applications were already {(isSavedForever ? "saved" : "not saved")}."
                : $"{(isSavedForever ? "Saved" : "Unsaved")} {result.ChangedCount} selected application{(result.ChangedCount == 1 ? string.Empty : "s")}.";
        return RedirectToAction(nameof(Index));
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

    private async Task<ApplicationWorkflowPageViewModel?> BuildWorkflowPageAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var details = await workflow.FindAsync(id, cancellationToken);
        if (details is null)
        {
            return null;
        }

        var localNow = ApplicationTime.ToLocal(timeProvider.GetUtcNow(), details.TimeZoneId);
        var appointmentStart = localNow.Date.AddDays(1).AddHours(9);
        return new ApplicationWorkflowPageViewModel
        {
            Workflow = details,
            Status = new StatusTransitionFormViewModel
            {
                Stage = ApplicationDisplay.NormalizeStage(details.Application.Stage),
                Outcome = ApplicationDisplay.NormalizeOutcome(details.Application.Outcome),
            },
            Interaction = new InteractionFormViewModel
            {
                OccurredAtLocal = localNow,
            },
            Appointment = new AppointmentFormViewModel
            {
                StartsAtLocal = appointmentStart,
                EndsAtLocal = appointmentStart.AddHours(1),
            },
        };
    }

    private async Task<IActionResult> WorkflowViewAsync(
        Guid id,
        Action<ApplicationWorkflowPageViewModel> configure,
        CancellationToken cancellationToken)
    {
        var page = await BuildWorkflowPageAsync(id, cancellationToken);
        if (page is null)
        {
            return NotFound();
        }

        configure(page);
        return View(nameof(Details), page);
    }

    private static InteractionInput InteractionInputFrom(InteractionFormViewModel model) => new(
        model.ContactId,
        model.Type,
        model.OccurredAtLocal!.Value,
        model.IsEmployerResponse,
        model.Notes);

    private static ApplicationInput InputFrom(ApplicationFormViewModel model) => new(
        model.CompanyId,
        model.RoleTitle,
        model.AppliedOn!.Value,
        model.SourceUrl,
        model.ApplicationPortalUrl,
        model.DescriptionText,
        model.Notes,
        model.IsSavedForever);

    private bool NormalizeSelection(BulkApplicationActionViewModel model)
    {
        model.SelectedApplicationIds = model.SelectedApplicationIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (model.SelectedApplicationIds.Count == 0)
        {
            TempData["Error"] = "Select at least one application first.";
            return false;
        }

        if (model.SelectedApplicationIds.Count > 200)
        {
            TempData["Error"] = "Select no more than 200 applications at a time.";
            return false;
        }

        return true;
    }

    private void SetSection(string section = "applications") => ViewData["Section"] = section;
}
