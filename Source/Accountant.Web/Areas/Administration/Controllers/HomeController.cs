using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.Administration.Controllers;

/// Dashboard landing for the Admin area. Gated by the `Admin` role —
/// non-admin users hitting `/Administration/*` get redirected to AccessDenied
/// via `ConfigureApplicationCookie.AccessDeniedPath`.
///
/// Override the role name per project by setting `[Authorize(Roles = "...")]`
/// or replacing with `[Authorize(Policy = "...")]`.
[Area("Administration")]
[Authorize(Roles = "Admin")]
public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
