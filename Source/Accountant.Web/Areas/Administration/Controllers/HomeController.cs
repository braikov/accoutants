using Accountant.Web.Areas.Administration.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.Administration.Controllers;

/// Dashboard landing for the Admin area. Gated by the `Admin` role —
/// non-admin users hitting `/Administration/*` get redirected to AccessDenied
/// via `ConfigureApplicationCookie.AccessDeniedPath`.
[Area("Administration")]
[Authorize(Roles = "Admin")]
public sealed class HomeController : Controller
{
    private readonly AdminDashboardService _dashboard;

    public HomeController(AdminDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var metrics = await _dashboard.LoadMetricsAsync(cancellationToken);
        return View(metrics);
    }
}
