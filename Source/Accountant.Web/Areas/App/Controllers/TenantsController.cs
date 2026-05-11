using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Accountant.Web.Areas.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.App.Controllers;

[Area("App")]
[Authorize]
public sealed class TenantsController : Controller
{
    private readonly TenantService _tenants;

    public TenantsController(TenantService tenants)
    {
        _tenants = tenants;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var memberships = await _tenants.ListMembershipsAsync(CurrentUserId(), cancellationToken);
        return View(memberships);
    }

    [HttpGet]
    public IActionResult Create() => View(new CreateTenantViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTenantViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        var tenant = await _tenants.CreateTenantAsync(CurrentUserId(), model.Name.Trim(), cancellationToken);
        // Make the new tenant active immediately.
        Response.Cookies.Append(
            ActiveTenantCookie.Name,
            tenant.Id.ToString(CultureInfo.InvariantCulture),
            ActiveTenantCookie.DefaultOptions());
        return RedirectToAction("Index", "Workspace", new { area = "App" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Switch(int tenantId, CancellationToken cancellationToken)
    {
        if (!await _tenants.IsMemberAsync(CurrentUserId(), tenantId, cancellationToken))
        {
            return Forbid();
        }
        Response.Cookies.Append(
            ActiveTenantCookie.Name,
            tenantId.ToString(CultureInfo.InvariantCulture),
            ActiveTenantCookie.DefaultOptions());
        var returnUrl = Request.Form["returnUrl"].ToString();
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Workspace", new { area = "App" });
    }

    private int CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("NameIdentifier claim missing on authenticated request.");
        return int.Parse(raw, CultureInfo.InvariantCulture);
    }
}

public sealed class CreateTenantViewModel
{
    [Required(ErrorMessage = "Името е задължително.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Името трябва да е между 2 и 120 символа.")]
    public string Name { get; set; } = "";
}
