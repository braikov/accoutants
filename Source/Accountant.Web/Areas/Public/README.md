# Public area — pre-launch landing

A self-contained ASP.NET Core MVC area that ships the public-facing landing page while the real product is being built. Designed to be **dropped as a unit** when the real homepage / authenticated app takes over.

## What's inside

- `Controllers/HomeController.cs` — `Index` (landing) and `Error` actions, both `[Area("Public")]`.
- `Views/Home/Index.cshtml` — landing page content (Bulgarian).
- `Views/Home/Error.cshtml` — error fallback (referenced by `UseExceptionHandler("/Public/Home/Error")` in `Program.cs`).
- `Views/Shared/_PublicLayout.cshtml` — area-specific layout.
- `Views/_ViewImports.cshtml`, `Views/_ViewStart.cshtml` — area-scoped Razor imports + layout default.

CSS lives in `wwwroot/public/css/public.css` (namespaced path so it's co-located but easily removable).

## How removal works

When the real product UI is ready and this landing should go:

1. Delete `Areas/Public/`.
2. Delete `wwwroot/public/`.
3. In `Program.cs`:
   - Remove the `public_area` route registration.
   - Restore the `default` route to point at the new home (or whatever):
     ```csharp
     app.MapControllerRoute(
         name: "default",
         pattern: "{controller=Home}/{action=Index}/{id?}");
     ```
   - Update `UseExceptionHandler("/Public/Home/Error")` to whatever your new error path is.
4. Add the new root-level `Controllers/HomeController.cs`, `Views/Home/`, `Views/Shared/_Layout.cshtml`, etc.

No other project file references this area. Removing it should not require touching `Accountant.DataAccess`, vendor projects, or the database schema.
