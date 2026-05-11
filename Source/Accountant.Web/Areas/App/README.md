# App area — authenticated landing

Self-contained area that ships the page authenticated users see after they log in. Today it's a placeholder; future product features will land in this area as additional controllers / views.

## What's inside

- `Controllers/HomeController.cs` — `[Area("App")]`, `[Authorize]`, `Index` action.
- `Views/Home/Index.cshtml` — welcome card with account status.
- `Views/Shared/_AppLayout.cshtml` — area-specific layout (header has Logout + Change password links).
- `Views/_ViewImports.cshtml`, `Views/_ViewStart.cshtml` — area-scoped Razor imports + layout default.

CSS lives in `wwwroot/app/css/app.css`. Self-contained — does NOT import `public.css` or `identity.css`.

## Routing

- `/App/` and `/App/Home/Index` resolve via the `area:exists` route in `Program.cs`.
- `AccountController.LocalRedirectSafe` falls back to `/App/Home/Index` after a successful login when there is no `returnUrl`.

## Authorization

Class-level `[Authorize]` on `HomeController` means anonymous requests get redirected to `/Identity/Account/Login?returnUrl=/App` automatically (per `ConfigureApplicationCookie.LoginPath`).

## Future product UI

When real product features land:

- Add controllers under `Areas/App/Controllers/` (Documents, Extractions, Settings, etc.).
- Group views under `Areas/App/Views/<Controller>/`.
- Extend `_AppLayout.cshtml` with primary navigation as the surface area grows.

The Public marketing area (`Areas/Public/`) and the App area can evolve / be removed independently — both are designed as drop-as-a-unit modules.
