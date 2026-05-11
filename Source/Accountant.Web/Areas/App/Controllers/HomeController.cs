using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.App.Controllers;

[Area("App")]
[Authorize]
public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
