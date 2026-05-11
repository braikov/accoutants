# Identity area — auth UI

Self-contained ASP.NET Core MVC area covering the auth flows: login, logout, register, email confirmation, forgot/reset password, access denied. Mirrors the same drop-as-a-unit pattern as `Areas/Public/`.

## What's inside

- `Controllers/AccountController.cs` — all auth actions, `[Area("Identity")]` + `[Route("Identity/Account/[action]")]`.
- `ViewModels/` — typed form models with Bulgarian validation messages.
- `Views/Account/` — one view per action; status pages (`*Confirmation`, `*Failed`, `AccessDenied`) reuse the `auth-status` shell.
- `Views/Shared/_IdentityLayout.cshtml` — area-specific layout (no dependency on Public area).
- `Views/_ViewImports.cshtml`, `Views/_ViewStart.cshtml` — area-scoped imports + layout default.

CSS lives in `wwwroot/identity/css/identity.css`. Self-contained — does **not** import `public.css` so the two areas can be removed independently.

## Routing

`Program.cs` already registers the `area:exists` conventional route, so `/Identity/Account/Login` etc. resolve out of the box. The cookie config in `ConfigureApplicationCookie` points `LoginPath` / `LogoutPath` / `AccessDeniedPath` at `/Identity/Account/*`.

## Email integration

Register / ForgotPassword / ResetPassword call `Accountant.Email.IEmailSender` directly:

- `SendEmailConfirmationAsync` — after a successful Register, with a callback URL that round-trips through `ConfirmEmail` action.
- `SendPasswordResetAsync` — after ForgotPassword, with a callback URL that round-trips through `ResetPassword` action.
- `SendPasswordChangedAsync` — security notification after a successful ResetPassword (best-effort, errors logged not surfaced).

In `appsettings.Development.json` the sender has `Email:Enabled=false` — the email is logged with the callback URL instead of sent. Read the warning line in stdout to grab the link and complete the flow manually.

## How removal works

When the auth UI is replaced (e.g. switching to a SPA / external IdP):

1. Delete `Areas/Identity/`.
2. Delete `wwwroot/identity/`.
3. In `Program.cs`:
   - Update `ConfigureApplicationCookie` paths to point at the new auth UI.
4. The `AccountantIdentityEmailSender` and DI registrations stay — the typed Identity contract is independent of the UI.
