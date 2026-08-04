using JobTracker.Web.Identity;
using Microsoft.AspNetCore.Mvc;

namespace JobTracker.Web.Controllers;

public sealed class DevelopmentMailController(
    DevelopmentMailStore store,
    IHostEnvironment environment) : Controller
{
    [HttpGet("/dev/mail")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index()
    {
        if ((!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
            || !IsLocalHost(Request.Host.Host))
        {
            return NotFound();
        }

        return View(store.Messages);
    }

    private static bool IsLocalHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(host, "::1", StringComparison.Ordinal);
}
