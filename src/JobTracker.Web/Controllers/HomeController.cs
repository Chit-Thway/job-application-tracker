using System.Diagnostics;
using JobTracker.Web.Foundation;
using JobTracker.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        SetPage("home");
        return View();
    }

    [HttpGet("/dashboard")]
    [Authorize]
    public IActionResult Dashboard() => FoundationPage(nameof(Dashboard));

    [HttpGet("/actions")]
    [Authorize]
    public IActionResult ActionCentre() => FoundationPage(nameof(ActionCentre));

    [HttpGet("/settings")]
    [Authorize]
    public IActionResult Settings() => FoundationPage(nameof(Settings));

    [HttpGet("/demo")]
    public IActionResult Demo() => FoundationPage(nameof(Demo));

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
