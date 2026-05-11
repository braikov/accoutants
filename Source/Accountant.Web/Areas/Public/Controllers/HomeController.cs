using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.Public.Controllers;

[Area("Public")]
public sealed class HomeController : Controller
{
    public IActionResult Index() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View();
}
