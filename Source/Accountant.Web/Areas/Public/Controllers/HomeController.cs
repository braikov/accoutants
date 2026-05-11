using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.Public.Controllers;

[Area("Public")]
public sealed class HomeController : Controller
{
    /// Public landing page. Logged-in users bypass the marketing page and go
    /// straight to the workspace — otherwise an authenticated user hitting `/`
    /// has no visible way back into the app.
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Workspace", new { area = "App" });
        }
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
