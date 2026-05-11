using Accountant.Web.Areas.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.App.Controllers;

/// Default controller for the App area — renders the workspace page (folder
/// tree + documents grid) and serves the polling endpoint used by the
/// page's JavaScript.
[Area("App")]
[Authorize]
public sealed class WorkspaceController : Controller
{
    private readonly WorkspaceService _workspace;
    private readonly IActiveTenantAccessor _activeTenant;

    public WorkspaceController(WorkspaceService workspace, IActiveTenantAccessor activeTenant)
    {
        _workspace = workspace;
        _activeTenant = activeTenant;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? folderId, CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current;
        if (tenant is null)
        {
            // The active-tenant middleware bootstraps a default tenant; if
            // we hit this branch the user has no claim — bounce to login.
            return Challenge();
        }
        var model = await _workspace.LoadAsync(tenant.Id, tenant.Name, folderId, cancellationToken);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Statuses(
        [FromQuery(Name = "ids")] string? ids,
        CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current;
        if (tenant is null)
        {
            return Unauthorized();
        }
        var parsed = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => int.TryParse(s, out _))
            .Select(int.Parse)
            .ToArray();
        if (parsed.Length == 0)
        {
            return Json(Array.Empty<object>());
        }
        var rows = await _workspace.GetStatusesAsync(tenant.Id, parsed, cancellationToken);
        return Json(rows.Select(r => new
        {
            id = r.Id,
            status = r.Status.ToString(),
            hasThumbnail = r.HasThumbnail,
        }));
    }
}
