using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Accountant.DataAccess.Services;
using Accountant.Jobs.Extraction;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.Administration.Controllers;

[Area("Administration")]
[Authorize(Roles = "Admin")]
public sealed class SettingsController : Controller
{
    private readonly IAppSettingsService _settings;

    public SettingsController(IAppSettingsService settings)
    {
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var current = await _settings.GetAsync(AppSettingKeys.ExtractionDefaultVendor, cancellationToken);
        return View(new SettingsViewModel
        {
            DefaultVendor = string.IsNullOrWhiteSpace(current) ? VendorName.Codex : current,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SettingsViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        var allowed = new[] { VendorName.Claude, VendorName.Codex, VendorName.Gemini };
        if (!allowed.Contains(model.DefaultVendor))
        {
            ModelState.AddModelError(nameof(model.DefaultVendor), "Невалиден vendor.");
            return View(model);
        }
        await _settings.SetAsync(
            AppSettingKeys.ExtractionDefaultVendor,
            model.DefaultVendor,
            CurrentUserId(),
            cancellationToken);
        TempData["SavedAt"] = DateTime.UtcNow.ToString("o");
        return RedirectToAction(nameof(Index));
    }

    private int CurrentUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}

public sealed class SettingsViewModel
{
    [Required]
    public string DefaultVendor { get; set; } = VendorName.Codex;
}
