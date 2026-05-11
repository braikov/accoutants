# Braikov Admin — post-scaffold setup

The template scaffolded:

- `Areas/Administration/` — controller + dashboard view + CoreUI-based admin layout. `[Authorize(Roles = "Admin")]`-gated.
- `wwwroot/admin/css/admin.css` — admin-area palette overrides.
- This guide. Delete once you've done the steps below.

CoreUI 5.x is pulled from jsdelivr CDN — no vendor files in your project. If you need offline assets, replace the CDN URLs in `_AdminLayout.cshtml` with locally-served files (libman / npm / static copy).

## 1. Prerequisite: auth must be wired

This template assumes `Braikov.Identity.*` is already installed (via `dotnet new braikov-identity` + the wire-up steps in `BRAIKOV_IDENTITY_SETUP.md`). The Admin area depends on:

- `Microsoft.AspNetCore.Identity` (for `[Authorize(Roles = "Admin")]`).
- A cookie auth pipeline + the `Admin` role configured.

If you scaffolded with `braikov-identity` already, you're fine. Otherwise, do that first.

## 2. Seed the Admin role + your first admin user

Roles aren't auto-created. You need to:

1. Ensure `RoleManager<IdentityRole<int>>` is registered (it is by default after `.AddDefaultTokenProviders()`).
2. On first startup, seed the `Admin` role and assign it to yourself. Add this snippet near the end of `Program.cs`, right before `app.Run()`:

```csharp
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<int>>>();
    if (!await roleManager.RoleExistsAsync("Admin"))
    {
        await roleManager.CreateAsync(new IdentityRole<int>("Admin"));
    }

    // First-time bootstrap: promote a known email to Admin.
    // Drop this block once the bootstrap user exists in prod.
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var bootstrapEmail = builder.Configuration["Bootstrap:AdminEmail"];
    if (!string.IsNullOrEmpty(bootstrapEmail))
    {
        var user = await userManager.FindByEmailAsync(bootstrapEmail);
        if (user is not null && !await userManager.IsInRoleAsync(user, "Admin"))
        {
            await userManager.AddToRoleAsync(user, "Admin");
        }
    }
}
```

And in `appsettings.Development.json` (NOT production):

```jsonc
{
  "Bootstrap": {
    "AdminEmail": "you@example.com"
  }
}
```

Register that email through the normal auth UI, confirm it, then restart — the bootstrap promotes it.

For prod: don't ship `Bootstrap:AdminEmail`; manage roles via a future UI or one-off SQL `INSERT INTO aspnetuserroles ...`.

## 3. Verify routing

After scaffold:

- `/Administration/Home/Index` — dashboard (Admin role required).
- Anonymous → 302 to `/Identity/Account/Login`.
- Authenticated but not Admin → 302 to `/Identity/Account/AccessDenied`.

If routes don't resolve, check `Program.cs` has the area route registered (this is default for `dotnet new mvc`):

```csharp
app.MapControllerRoute(
    name: "default_area",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
```

## 4. Customize

- **Brand mark**: replace the `<span class="badge bg-light text-dark fw-bold rounded-2">a.</span>` in the sidebar with your logo (img or SVG).
- **Sidebar items**: see `Areas/Administration/README.md` for the snippet pattern.
- **Theme**: `wwwroot/admin/css/admin.css` overrides `--cui-primary`. Adjust for brand colour.
- **Localization**: layout is English-ish; add a Bulgarian variant by either using `IViewLocalizer` or splitting the layout into `_AdminLayout.bg.cshtml` + `_AdminLayout.en.cshtml`.

## CoreUI icon reference

The layout uses `cil-*` classes from [CoreUI free icons](https://coreui.io/icons/). Common picks:

- `cil-speedometer` (Dashboard)
- `cil-user` (Users)
- `cil-people` (Groups)
- `cil-settings` (Settings)
- `cil-list` (List view)
- `cil-bell` (Notifications)
- `cil-chart-line` (Analytics)
- `cil-cloud-upload` (Imports)
