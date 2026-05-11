using Accountant.Web.Areas.Administration.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.Administration.Controllers;

/// JSON endpoints feeding Chart.js on the admin dashboard.
[Area("Administration")]
[Authorize(Roles = "Admin")]
[Route("Administration/Charts/[action]")]
public sealed class ChartsController : Controller
{
    private readonly AdminDashboardService _dashboard;

    public ChartsController(AdminDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet]
    public async Task<IActionResult> DocumentsPerDay(int days, CancellationToken cancellationToken)
    {
        var points = await _dashboard.GetDocumentsPerDayAsync(days <= 0 ? 30 : days, cancellationToken);
        return Json(points);
    }

    [HttpGet]
    public async Task<IActionResult> VendorDistribution(CancellationToken cancellationToken)
    {
        var slices = await _dashboard.GetVendorDistributionAsync(cancellationToken);
        return Json(slices);
    }

    [HttpGet]
    public async Task<IActionResult> AvgLatency(CancellationToken cancellationToken)
    {
        var rows = await _dashboard.GetAvgLatencyAsync(cancellationToken);
        return Json(rows);
    }
}
