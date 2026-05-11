# Administration area

CoreUI-based admin dashboard scaffolded by `dotnet new braikov-admin`. Sister to `Areas/Identity/` (auth UI) and `Areas/App/` (authenticated user landing) from `braikov-identity`.

## What's inside

- `Controllers/HomeController.cs` — `[Authorize(Roles = "Admin")]`-gated, single `Index` action that renders the dashboard.
- `Views/Home/Index.cshtml` — placeholder dashboard with 4 metric cards + "next steps" panel.
- `Views/Shared/_AdminLayout.cshtml` — sidebar + sticky header (theme toggle + user dropdown + logout) + footer. CoreUI loaded from jsdelivr CDN.
- `Views/_ViewImports.cshtml`, `Views/_ViewStart.cshtml` — area-scoped Razor defaults.

CSS sweeps live in `wwwroot/admin/css/admin.css` (sidebar palette, brand, footer tweaks).

## Routing

`/Administration/Home/Index` resolves via the `area:exists` route registered by `dotnet new mvc`. No additional routing config needed.

Non-Admin users hitting any `/Administration/*` URL get redirected to `AccessDeniedPath` (from `ConfigureApplicationCookie`) — typically `/Identity/Account/AccessDenied`.

## Adding nav items

Open `Views/Shared/_AdminLayout.cshtml` and look for the comment block under the sidebar `<ul>`. Drop in a new `<li class="nav-item">` with `asp-area="Administration"` + your controller/action.

Example:

```cshtml
<li class="nav-item">
    <a class="nav-link @(ctrlIs("users") ? "active" : "")"
       asp-area="Administration" asp-controller="Users" asp-action="Index">
        <i class="nav-icon cil-user"></i>
        Users
    </a>
</li>
```

For grouped items use CoreUI's `nav-group` — see [CoreUI sidebar docs](https://coreui.io/docs/components/sidebar/#navigation).

## CoreUI icons

The layout pulls free CoreUI icons via `@coreui/icons` CDN. Icon names follow the `cil-*` pattern (e.g. `cil-user`, `cil-list`, `cil-settings`). Full list: <https://coreui.io/icons/>.

## How removal works

If the admin surface ever needs to be removed:

1. Delete `Areas/Administration/`.
2. Delete `wwwroot/admin/`.
3. No `Program.cs` cleanup required — the area auto-registered via `area:exists` will simply have nothing to resolve.
