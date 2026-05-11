# 0002 — Identity + Email + Notifications

**Status:** ⏳ Чакаща
**Owner:** Claude agent (with user)
**Depends on:** Braikov.Notifications.* extracted to standalone repo + local NuGet feed (done — see [useful_commands.md](../../useful_commands.md) > Braikov.Notifications.\* packages).

## Goal

Minimum viable user authentication for `Accountant.Web`:

- **Register** with email confirmation
- **Login** / Logout
- **Forgot password** with email-driven reset

Wire the notification infrastructure (Braikov.Notifications.* packages + new `Accountant.Email` + `Accountant.Notifications` host adapter) so the auth flows can send transactional email. Auth UI lives in a new `Areas/Identity/` mirroring the `Areas/Public/` self-contained pattern.

## Out of scope (separate tasks later)

- Authorization roles (admin vs clients)
- Two-factor auth, external logins (Google/Microsoft/etc.)
- Marketing / non-auth email templates
- Hangfire scheduler / outbox-based delivery — initial impl uses synchronous SMTP send. Outbox upgrade is a follow-up if/when async delivery becomes a real need.

## Architecture overview

```
Accountant.Web ─┬─► Accountant.Identity   (ApplicationUser model, integers as Id)
                ├─► Accountant.DataAccess (IdentityDbContext base, AspNetUsers tables co-located in same DB)
                ├─► Accountant.Notifications (host adapter — RecipientResolver, EmailBridge, DI wiring)
                ├─► Accountant.Email      (Razor templates + RazorRendererService, ASP.NET Identity IEmailSender impl)
                └─► Braikov.Notifications.* (Core + DataAccess + MySql + Email — via local NuGet feed)
```

Identity tables live in the same `accountant` MySQL DB and the same `AccountantDbContext` as the rest. Notification persistence (Braikov's `NotificationDbContext`) is a separate EF context with its own migrations history table — same DB, different lifecycle.

## Phases

### Phase A — Identity foundation ✅

A1. ✅ `ApplicationUser : IdentityUser<int>` in [Accountant.Identity/Models/ApplicationUser.cs](../../Source/Accountant.Identity/Models/ApplicationUser.cs). Package `Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.0` added (pinned to match Pomelo / EF 9 elsewhere).
A2. ✅ [AccountantDbContext](../../Source/Accountant.DataAccess/AccountantDbContext.cs) inherits `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`. DataAccess references Accountant.Identity for the user type.
A3. ✅ `AddIdentity<...>().AddEntityFrameworkStores<AccountantDbContext>().AddDefaultTokenProviders()` wired in [Accountant.Web/Program.cs](../../Source/Accountant.Web/Program.cs). `ConfigureApplicationCookie` sets `.Accountant.Auth` cookie + 14-day sliding expiration. `RequireConfirmedAccount = true` (email confirmation gate, comes online in Phase B/C). `UseAuthentication()` added to the pipeline before `UseAuthorization()`. Login/Logout/AccessDenied paths point to `/Identity/Account/*` (404 today, resolves in Phase C).
A4. ✅ EF migration `20260510164320_AddIdentity` created.
A5. ✅ Applied locally (`dev_accountant`) and on prod (`accountant` via SSH using the idempotent script + `MYSQL_PWD` env var pattern).
A6. ✅ Solution build clean; identity tables present in both DBs:
  - `aspnetroleclaims`, `aspnetroles`, `aspnetuserclaims`, `aspnetuserlogins`, `aspnetuserroles`, `aspnetusers`, `aspnetusertokens`

**Deliverable:** identity schema in DB; nothing user-facing yet. Web starts; cookies are configured but no Login UI exists yet.

### Phase B — Email + Braikov notifications wiring ✅

B1. ✅ `Accountant.Email` class library created ([csproj](../../Source/Accountant.Email/Accountant.Email.csproj)):
   - `Microsoft.NET.Sdk.Razor` SDK + `AddRazorSupportForMvc=true`
   - Packages: MailKit 4.16.0, RazorLight 2.3.1, Options.ConfigurationExtensions 9.0.0, Logging.Abstractions 9.0.0, Caching.Memory 9.0.0 (override of vulnerable transitive)
   - [Abstractions/IEmailSender.cs](../../Source/Accountant.Email/Abstractions/IEmailSender.cs), [IEmailTemplateRenderer.cs](../../Source/Accountant.Email/Abstractions/IEmailTemplateRenderer.cs)
   - [Models/](../../Source/Accountant.Email/Models/): EmailConfirmation, PasswordReset, PasswordChanged
   - [Services/](../../Source/Accountant.Email/Services/): `RazorEmailTemplateRenderer` (RazorLight, file-system project, culture fallback), `SmtpEmailSender` (MailKit), `NullEmailSender` (logs callback URLs for dev)
   - [Templates/](../../Source/Accountant.Email/Templates/): `_EmailLayout.cshtml` shell + `EmailConfirmation.bg-BG.cshtml` + `PasswordReset.bg-BG.cshtml` + `PasswordChanged.bg-BG.cshtml`
   - [EmailDependencyInjection.cs](../../Source/Accountant.Email/EmailDependencyInjection.cs): `AddAccountantEmail` (SMTP) + `AddAccountantEmailNullSender` (dev logging)
   - Build clean (0 warnings, 0 errors)
B2. ✅ Package layering — Braikov.Notifications.* refs land in the `Accountant.Notifications` adapter (B3); Web takes a single `ProjectReference` to the adapter and pulls them transitively. Placeholder `Braikov.Notifications.Core` PackageReference removed from `Accountant.Web`. Web also picked up direct ProjectReferences to `Accountant.Email` and `Accountant.Notifications`.
B3. ✅ `Accountant.Notifications` class library created ([csproj](../../Source/Accountant.Notifications/Accountant.Notifications.csproj)):
   - PackageReferences: `Braikov.Notifications.Core / DataAccess / MySql / Email` 0.1.0 + EF Core 9.0.0 + Options.ConfigurationExtensions 9.0.0
   - ProjectReferences: `Accountant.DataAccess`, `Accountant.Email`
   - [AccountantNotificationOptions](../../Source/Accountant.Notifications/AccountantNotificationOptions.cs) (`SectionName="Notifications"`, `DefaultCulture="bg-BG"`)
   - [AccountantNotificationTypeKeys](../../Source/Accountant.Notifications/AccountantNotificationTypeKeys.cs) + `AccountantEmailTemplateKeys` constants
   - [AccountantRecipientResolver](../../Source/Accountant.Notifications/AccountantRecipientResolver.cs) — int-ID lookup against `AccountantDbContext.Users`, returns Email + EmailConfirmed + Culture + Status (Locked when LockoutEnd in future)
   - [AccountantNotificationEmailSender](../../Source/Accountant.Notifications/AccountantNotificationEmailSender.cs) — bridges Braikov `INotificationEmailSender` → `Accountant.Email.IEmailSender`. Deserializes `PayloadJson` into typed model based on TemplateKey
   - [DependencyInjection.AddAccountantNotifications](../../Source/Accountant.Notifications/DependencyInjection.cs) — wires Options, `AddMySqlNotifications` (connection string `"Accountant"`, same DB as Identity), `AddBraikovNotificationServices`, three `NotificationTypeDefinition`s (EmailConfirmation, PasswordReset, PasswordChanged — Required policy, Email-only), recipient resolver, and conditionally registers `EmailChannelSender` + `AccountantNotificationEmailSender` only when the host has registered an `IEmailSender`
   - Build clean (0 warnings, 0 errors); Web builds clean too
B4. ✅ Email + Notifications config sections wired:
   - [appsettings.json](../../Source/Accountant.Web/appsettings.json) — placeholder `Email` block (Enabled=false, From, Smtp StartTls/587), placeholder `Notifications` block (DefaultCulture=bg-BG)
   - [appsettings.Development.json](../../Source/Accountant.Web/appsettings.Development.json) — `Email.Enabled=false` for log-only dev (flip to true + spin up smtp4dev on port 25 to test real send)
B5. ✅ DI wired in [Accountant.Web/Program.cs](../../Source/Accountant.Web/Program.cs):
   - `AddAccountantEmail(builder.Configuration)` — registers EmailOptions + RazorEmailTemplateRenderer + SmtpEmailSender
   - `AddAccountantNotifications(builder.Configuration)` — registers Braikov MySql + core services + recipient resolver + email channel sender + 3 type definitions. Order matters: Email must come first because the notifications adapter only wires the email channel sender if `IEmailSender` is already registered.
   - Added `Microsoft.EntityFrameworkCore.Design` 9.0.0 to Web for EF tooling on `NotificationDbContext`.
B6. ✅ Migration `20260506151647_InitialNotificationsSchema` discovered — shipped pre-built inside the `Braikov.Notifications.MySql` package DLL. Per global rule (`~/.claude/CLAUDE.md`): schema changes go through migrations only; we don't author the schema in our repo, the package owns it.
B7. ✅ Migration applied to both DBs:
   - Local `dev_accountant` via `dotnet ef database update`
   - Prod `accountant` via `dotnet ef migrations script --idempotent` → SCP to `vic.bg` → `mariadb -e "source ..."` driven by `apply_notifications.ps1` that pulls the connection string from the IIS App Pool env vars (no creds on the command line). Verified tables on both: `notifications`, `notificationdeliveries`, `usernotificationpreferences`, `__braikovnotificationsmigrationshistory`.
B8. ✅ ASP.NET Core Identity adapter [AccountantIdentityEmailSender](../../Source/Accountant.Web/Identity/AccountantIdentityEmailSender.cs) implements `IEmailSender<ApplicationUser>` (the typed contract from `Microsoft.AspNetCore.Identity` core — no extra Identity.UI package needed) and delegates to `Accountant.Email.IEmailSender`. Registered scoped in [Program.cs](../../Source/Accountant.Web/Program.cs). Also added `IEmailSender.SendHtmlAsync(toEmail, subject, htmlBody, ct)` for the case when the framework / caller already has rendered HTML and wants to bypass the template renderer.
B9. ✅ Smoke test via dev-only [DevDiagnosticsController](../../Source/Accountant.Web/Controllers/DevDiagnosticsController.cs) (`/dev/test-email`, `/dev/render-template`). Routes are gated by `app.Environment.IsDevelopment()` in Program.cs so they never ship to prod. Required two fixes:
   - Added `<PreserveCompilationContext>true</PreserveCompilationContext>` to `Accountant.Web.csproj` — RazorLight needs the host's compilation metadata at runtime to compile email templates.
   - Added `app.MapControllers()` (dev-only) to the pipeline so attribute-routed controllers get registered alongside the area-conventional routes.
   - Verified all three flows hit `SmtpEmailSender` with localized BG subjects: `'Потвърдете акаунта си'`, `'Нулиране на парола'`, `'Паролата ви беше променена'`. With `Email:Enabled=false` the sender logs a warning instead of touching SMTP — confirming the renderer + DI graph + Identity adapter all work end-to-end.

**Deliverable:** the Web host can send a transactional email end-to-end.

### Phase B status — ✅ Завършена

All 9 sub-steps green. Email pipeline is functional end-to-end (template render + SMTP send hook). Notification persistence is in place (3 prod tables + history). Identity has a typed adapter into the email pipeline. Phase C can layer the Account UI on top with no further plumbing.

### Phase C — Identity area + auth UI ✅

C1. ✅ [Areas/Identity/](../../Source/Accountant.Web/Areas/Identity/) skeleton — Controllers, ViewModels, Views/Account, Views/Shared/_IdentityLayout, Views/_ViewImports, Views/_ViewStart, [README](../../Source/Accountant.Web/Areas/Identity/README.md). Layout is self-contained (does NOT depend on `_PublicLayout`) so the area can be dropped independently.
C2. ✅ [AccountController](../../Source/Accountant.Web/Areas/Identity/Controllers/AccountController.cs) with actions Login, Logout, Register, RegisterConfirmation, ConfirmEmail, ConfirmEmailFailed (view), ForgotPassword, ForgotPasswordConfirmation, ResetPassword, ResetPasswordConfirmation, AccessDenied. All POSTs are antiforgery-protected. Token round-trip is `WebEncoders.Base64UrlEncode` so it survives URL transport. Login uses `lockoutOnFailure: true` to honor the 5-attempt lockout configured in Program.cs.
C3. ✅ ViewModels in [Areas/Identity/ViewModels/](../../Source/Accountant.Web/Areas/Identity/ViewModels/): Login, Register, ForgotPassword, ResetPassword. All validation messages are Bulgarian.
C4. ✅ 9 Razor views under [Views/Account/](../../Source/Accountant.Web/Areas/Identity/Views/Account/) — three forms (Login, Register, ForgotPassword, ResetPassword) + five status pages (RegisterConfirmation, ConfirmEmail, ConfirmEmailFailed, ForgotPasswordConfirmation, ResetPasswordConfirmation, AccessDenied). Self-contained CSS at [wwwroot/identity/css/identity.css](../../Source/Accountant.Web/wwwroot/identity/css/identity.css).
C5. ✅ Templates already created in B1.
C6. ✅ Confirmation flow: `Register POST` → `CreateAsync` → `GenerateEmailConfirmationTokenAsync` → URL-encode token → `Url.Action(ConfirmEmail, ...)` → `IEmailSender.SendEmailConfirmationAsync` → redirect to `RegisterConfirmation`. `ConfirmEmail GET` decodes the token, calls `ConfirmEmailAsync`, renders success or `ConfirmEmailFailed`.
C7. ✅ Forgot-password flow: `ForgotPassword POST` looks up user, generates reset token (only if user exists AND email confirmed — silent succeed otherwise to avoid leaking account existence), emails the link via `IEmailSender.SendPasswordResetAsync`, redirects to `ForgotPasswordConfirmation`. `ResetPassword POST` decodes token, calls `ResetPasswordAsync`, then best-effort `SendPasswordChangedAsync` security notification (errors logged, not surfaced).
C8. ✅ Public area header in [_PublicLayout.cshtml](../../Source/Accountant.Web/Areas/Public/Views/Shared/_PublicLayout.cshtml) reads `User.Identity.IsAuthenticated`: anonymous → "Вход" + "Регистрация" buttons; authenticated → email + "Изход" form (POST). New header styles in `public.css` (`.head-auth*`).

**Deliverable: ✅ user can register, confirm email, log in, log out, reset password through the browser.**

End-to-end smoke test verified (Web on https://localhost:7162):
- `GET /Identity/Account/{Login,Register,ForgotPassword}` → 200
- `POST /Identity/Account/Register` (new email + password) → 302 to `/Identity/Account/RegisterConfirmation`, user lands in `aspnetusers` with `EmailConfirmed=0`, confirmation email logged with subject "Потвърдете акаунта си"
- After flipping `EmailConfirmed=1`: `POST /Identity/Account/Login` → 302 to `/Public`, sets `.Accountant.Auth` cookie with `secure; samesite=lax; httponly`
- `GET /` while authenticated shows the user email + "Изход" logout form in the header

### Phase D — Polish ✅

D1. ✅ Authenticated landing — new [Areas/App/](../../Source/Accountant.Web/Areas/App/) with `[Authorize]` HomeController + `_AppLayout` (header has Logout + Change password + email). Self-contained CSS at `wwwroot/app/css/app.css`. `AccountController.LocalRedirectSafe` defaults to `/App/Home/Index` after login when no `returnUrl`. Anonymous request to `/App/*` → 302 to `/Identity/Account/Login?ReturnUrl=/App` (per `ConfigureApplicationCookie.LoginPath`).
D2. ✅ Change-password flow:
   - [ChangePasswordViewModel](../../Source/Accountant.Web/Areas/Identity/ViewModels/ChangePasswordViewModel.cs) with BG validation
   - `ChangePassword` GET/POST in [AccountController](../../Source/Accountant.Web/Areas/Identity/Controllers/AccountController.cs) — `[Authorize]`-gated, calls `UserManager.ChangePasswordAsync` + `SignInManager.RefreshSignInAsync` (rotates security stamp, invalidates other live sessions)
   - Best-effort `SendPasswordChangedAsync` email (errors logged, not surfaced)
   - Views: `ChangePassword.cshtml` (form) + `ChangePasswordConfirmation.cshtml` (success)
D3. ✅ Email-confirmation policy enforced via `RequireConfirmedAccount = true` in Identity options (Phase A3) — `SignInResult.IsNotAllowed` blocks unconfirmed accounts at sign-in time. Login view surfaces "Акаунтът не е потвърден…" message.
D4. ✅ Session config: 14-day sliding expiration on `.Accountant.Auth` cookie, `HttpOnly`, `SameSite=Lax`, `SecurePolicy=SameAsRequest` — wired in Phase A3. Logout button (POST + antiforgery) lives in `_PublicLayout` (anonymous-aware) and `_AppLayout` (authenticated).
D5. ✅ [useful_commands.md](../../useful_commands.md) updated:
   - Added EF script generation (`migrations script --idempotent`) for prod-safe rollouts
   - Added "Notifications schema (`NotificationDbContext`)" section
   - Added "Identity / dev-only helpers" section (manual confirm, wipe users, smtp4dev install)
   - Updated mariadb path 10.4 → 12.2
D6. ✅ [Project_Structure.md](../Project_Structure.md) updated:
   - Section "Текущ scope" rewritten to reflect active Web/persistence/auth/email
   - Section 2 (root layout) lists the new Source projects
   - Section 3 (solution structure) describes Areas + Web Email + Notifications + ReviewSite
   - Section 4 (dependencies) reflects the actual Web → DataAccess+MySql+Email+Notifications topology
   - Section 11 (Notifications) rewritten — packages live in standalone `Braikov\` repo, consumed via local NuGet feed
   - Section 13 (deferred) updated — Identity foundation done, remaining: roles, Hangfire, Web feature surface

**Deliverable: ✅ complete auth flow with sensible defaults; ready for product features to layer on top.**

End-to-end smoke tested:
- Anonymous `GET /App` → 302 to Login with correct `ReturnUrl=/App`
- Register → email logged → manual confirm → Login → 302 to `/App` → page renders user email
- ChangePassword GET/POST → success → 302 to ChangePasswordConfirmation, security email triggered
- Old password no longer works (HTTP 200 + "Невалиден имейл или парола" rendered HTML-encoded)
- New password works → 302 to /App
- Logout → 302 to /Public, cookie cleared, subsequent /App → 302 to Login

## Status icons per phase / step

When working a phase, mark steps as done inline:

- ⏳ Чакаща
- 🔄 В процес
- ✅ Завършена

Example after Phase A:

> A1. ✅
> A2. ✅
> ...

## Notes for execution

- Each phase ends in a verifiable deliverable. Stop and verify before moving to the next.
- Phase A's migration touches the production DB — apply during a quiet window. The `dotnet ef migrations script --idempotent` pattern from [useful_commands.md](../../useful_commands.md) is safer than `database update` directly against prod.
- Phase B requires real SMTP credentials. For local dev, [smtp4dev](https://github.com/rnwood/smtp4dev) gives a local fake SMTP server with a web UI for inspecting sent mail — recommended over real SMTP during development.
- Phase C styling decisions can be iterated; do NOT block phase completion on perfect design — function first, polish later.
