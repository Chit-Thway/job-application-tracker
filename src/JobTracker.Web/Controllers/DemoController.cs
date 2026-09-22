using JobTracker.Web.Data;
using JobTracker.Web.Demo;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

[AllowAnonymous]
[ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, VaryByHeader = "Accept-Encoding")]
public sealed class DemoController(DemoCatalog catalog) : Controller
{
    [HttpGet("/demo")]
    public IActionResult Index()
    {
        SetPage("dashboard");
        return View(catalog.GetDashboard());
    }

    [HttpGet("/demo/applications")]
    public IActionResult Applications(string? query, PipelineStage? stage)
    {
        SetPage("applications");
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var applications = catalog.GetApplications()
            .Where(item => normalizedQuery is null
                || item.RoleTitle.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || item.CompanyName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .Where(item => stage is null || item.Stage == stage)
            .OrderByDescending(item => item.AppliedOn)
            .ToList();

        return View(new DemoApplicationIndexViewModel(applications, normalizedQuery, stage));
    }

    [HttpGet("/demo/applications/{slug}")]
    public IActionResult Details(string slug)
    {
        SetPage("applications");
        var application = catalog.Find(slug);
        if (application is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("NotFound");
        }

        return View(application);
    }

    [HttpGet("/demo/actions")]
    public IActionResult ActionCentre()
    {
        SetPage("actions");
        var dashboard = catalog.GetDashboard();
        return View(new DemoActionCentreViewModel(dashboard.Today, dashboard.Applications));
    }

    [HttpGet("/demo/extension")]
    public IActionResult Extension()
    {
        SetPage("extension");
        return View();
    }

    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE")]
    [Route("/demo/{**path}")]
    [IgnoreAntiforgeryToken]
    public IActionResult MutationNotAllowed()
    {
        Response.Headers.Allow = "GET";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    private void SetPage(string section)
    {
        ViewData["DemoSection"] = section;
    }
}
