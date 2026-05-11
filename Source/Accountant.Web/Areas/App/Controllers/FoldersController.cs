using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Accountant.DataAccess;
using Accountant.DataAccess.Entities.Product;
using Accountant.Web.Areas.App.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accountant.Web.Areas.App.Controllers;

[Area("App")]
[Authorize]
public sealed class FoldersController : Controller
{
    private readonly AccountantDbContext _db;
    private readonly IActiveTenantAccessor _activeTenant;

    public FoldersController(AccountantDbContext db, IActiveTenantAccessor activeTenant)
    {
        _db = db;
        _activeTenant = activeTenant;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        [FromForm] CreateFolderRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        if (!ModelState.IsValid)
        {
            return BadRequest(new { error = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)) });
        }

        if (request.ParentFolderId is int parentId)
        {
            var parentBelongs = await _db.Folders
                .AnyAsync(f => f.Id == parentId && f.TenantId == tenant.Id, cancellationToken);
            if (!parentBelongs)
            {
                return BadRequest(new { error = "Невалидна родителска папка." });
            }
        }

        var name = request.Name.Trim();
        var duplicate = await _db.Folders.AnyAsync(
            f => f.TenantId == tenant.Id
                && f.ParentFolderId == request.ParentFolderId
                && f.Name == name,
            cancellationToken);
        if (duplicate)
        {
            return BadRequest(new { error = "Вече има папка с това име на същото ниво." });
        }

        var folder = new Folder
        {
            TenantId = tenant.Id,
            ParentFolderId = request.ParentFolderId,
            Name = name,
            CreatedByUserId = CurrentUserId(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        _db.Folders.Add(folder);
        await _db.SaveChangesAsync(cancellationToken);

        return Json(new { id = folder.Id, name = folder.Name, parentFolderId = folder.ParentFolderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(
        [FromForm] RenameFolderRequest request,
        CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        if (!ModelState.IsValid)
        {
            return BadRequest(new { error = "Невалидно име." });
        }

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == request.Id && f.TenantId == tenant.Id, cancellationToken);
        if (folder is null)
        {
            return NotFound();
        }
        folder.Name = request.Name.Trim();
        await _db.SaveChangesAsync(cancellationToken);
        return Json(new { id = folder.Id, name = folder.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        var folder = await _db.Folders
            .Include(f => f.Children)
            .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenant.Id, cancellationToken);
        if (folder is null)
        {
            return NotFound();
        }
        if (folder.Children.Count > 0)
        {
            return BadRequest(new { error = "Папката съдържа подпапки — изтрийте първо тях." });
        }
        // Documents in this folder will be orphaned to root (FolderId set to
        // NULL by the FK rule — see DocumentConfiguration).
        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync(cancellationToken);
        return Json(new { ok = true });
    }

    private int CurrentUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
}

public sealed class CreateFolderRequest
{
    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; set; } = "";

    public int? ParentFolderId { get; set; }
}

public sealed class RenameFolderRequest
{
    public int Id { get; set; }

    [Required, StringLength(120, MinimumLength = 1)]
    public string Name { get; set; } = "";
}
