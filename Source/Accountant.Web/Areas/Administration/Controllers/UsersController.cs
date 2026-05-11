using Accountant.Web.Areas.Administration.Services;
using Accountant.Web.Areas.Administration.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accountant.Web.Areas.Administration.Controllers;

[Area("Administration")]
[Authorize(Roles = "Admin")]
public sealed class UsersController : Controller
{
    private readonly AdminCatalogService _catalog;

    public UsersController(AdminCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var pagedResult = await _catalog.ListUsersAsync(page, pageSize, cancellationToken);
        return View(new UsersListViewModel { Page = pagedResult });
    }
}
