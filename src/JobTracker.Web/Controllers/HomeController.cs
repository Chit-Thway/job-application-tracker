using System.Diagnostics;
using JobTracker.Web.Applications;
using JobTracker.Web.Data;
using JobTracker.Web.Foundation;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

public class HomeController(
    DashboardService dashboard,
    ApplicationWorkflowService workflow,
    RetentionOperationsService retention) : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        SetPage("home");
        return View();
    }

    [HttpGet("/dashboard")]
    [Authorize]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        SetPage("dashboard");
        return View(await dashboard.GetAsync(cancellationToken));
    }

    [HttpGet("/actions")]
    [Authorize]
    public async Task<IActionResult> ActionCentre(CancellationToken cancellationToken)
    {
        SetPage("actions");
        return View(await dashboard.GetAsync(cancellationToken));
    }

    [HttpPost("/actions/applications/{applicationId:guid}/confirm-ghosted")]
    [Authorize]
    public async Task<IActionResult> ConfirmGhosted(
        Guid applicationId,
        CancellationToken cancellationToken)
    {
        var details = await workflow.FindAsync(applicationId, cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        if (!await dashboard.CanConfirmGhostedAsync(applicationId, cancellationToken))
        {
            TempData["Error"] = "Ghosted can be confirmed only after 30 days without a meaningful employer response.";
            return RedirectToAction(nameof(ActionCentre));
        }

        var result = await workflow.TransitionAsync(
            applicationId,
            new StatusTransitionInput(
                details.Application.Stage,
                ApplicationOutcome.Ghosted,
                "Confirmed Ghosted from the Action Centre after 30 days without an employer response."),
            cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        if (result != WorkflowWriteResult.Success)
        {
            TempData["Error"] = "The application could not be marked Ghosted. Refresh and check its latest response history.";
            return RedirectToAction(nameof(ActionCentre));
        }

        TempData["Success"] = "Application marked Ghosted. The change was added to its history and can be reversed later.";
        return RedirectToAction(nameof(ActionCentre));
    }

    [HttpPost("/actions/applications/{applicationId:guid}/tasks/{taskId:guid}/completed")]
    [Authorize]
    public async Task<IActionResult> CompleteTask(
        Guid applicationId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var result = await workflow.SetTaskCompletedAsync(
            applicationId,
            taskId,
            true,
            cancellationToken);
        if (result == WorkflowWriteResult.NotFound)
        {
            return NotFound();
        }

        TempData["Success"] = "Task completed.";
        return RedirectToAction(nameof(ActionCentre));
    }

    [HttpGet("/settings")]
    [Authorize]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        SetPage("settings");
        return View(await retention.GetSettingsAsync(cancellationToken));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [HttpGet("/error")]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        SetPage(string.Empty);
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [HttpGet("/Home/HandleStatusCode")]
    public IActionResult HandleStatusCode(int code)
    {
        Response.StatusCode = code;
        SetPage(string.Empty);

        if (code == StatusCodes.Status404NotFound)
        {
            return View("NotFound");
        }

        return View("Error", new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
        });
    }

    private IActionResult FoundationPage(string actionName)
    {
        var page = FoundationPageCatalog.GetByAction(actionName);
        SetPage(page.Section);
        return View("Foundation", page);
    }

    private void SetPage(string section)
    {
        ViewData["Section"] = section;
    }
}
