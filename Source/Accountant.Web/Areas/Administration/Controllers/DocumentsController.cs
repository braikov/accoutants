using Accountant.Web.Areas.Administration.Services;
using Accountant.Web.Areas.Administration.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.Administration.Controllers;

[Area("Administration")]
[Authorize(Roles = "Admin")]
public sealed class DocumentsController : Controller
{
    private readonly AdminCatalogService _catalog;

    public DocumentsController(AdminCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DocumentsFilter filter, CancellationToken cancellationToken)
    {
        var page = await _catalog.ListDocumentsAsync(filter, cancellationToken);
        var tenants = await _catalog.ListAllTenantsAsync(cancellationToken);
        var users = await _catalog.ListAllUsersAsync(cancellationToken);
        var model = new DocumentsListViewModel
        {
            Filter = filter,
            Page = page,
            Tenants = tenants,
            Users = users,
        };
        return View(model);
    }
}
