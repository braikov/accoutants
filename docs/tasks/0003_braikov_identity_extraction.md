# 0003 — Extract reusable auth foundation: Braikov.Identity.* packages + dotnet new template

**Status:** ✅ Завършена
**Owner:** Claude agent + user
**Depends on:** Task 0002 (Accountant has the most complete password-Identity flow — it's the donor for template scaffolding).

## Goal

Stop re-implementing user management for every new project. Extract the genuinely reusable parts of ASP.NET Core Identity setup into shared `Braikov.Identity.*` NuGet packages (parallel to `Braikov.Notifications.*`), and ship a `dotnet new` template for the branded shell so new projects boot from `register → login → authenticated landing` in minutes, not half a day.

Targets 4 existing projects (in migration order): **Squash**, **Jokes**, **Accountant**. **Assistant** is out of scope — it's API-driven, different paradigm.

## Non-goals (this task)

- Migrating `Assistant.Web` — uses an external API for auth, would require API-side rewrite.
- 2FA, external logins (Google/Microsoft/etc.), magic links.
- Role / claim / multi-tenant infrastructure beyond what the projects already do today.
- A web-hosted package gallery — local NuGet feed at `C:\Projects\Miro\NuGet\` stays the canonical distribution channel.

## Decisions captured

1. **TKey:** Generic over `TKey` (where `TKey : IEquatable<TKey>`), default to `string` for new projects. Existing projects keep their key types (`int` for Accountant, `string` for Jokes/Squash). Zero migration cost.
2. **Localization:** Package ships resource-key-based ViewModels + `SharedResource.bg.resx` / `SharedResource.en.resx`. Hosts add new languages by dropping `SharedResource.<culture>.resx` in their own `Resources/` folder. Per-key override works via same resource fallback chain.
3. **Passwordless short-code:** Separate optional package (`Braikov.Identity.Passwordless`). Accountant doesn't need it; doesn't pay the cost.
4. **Authenticated landing:** Template includes a placeholder `Areas/App/` so new projects boot to a working `/App` page after login.
5. **Email dispatch path:** `BaseAccountController` depends on `IIdentityEmailDispatcher` abstraction (declared in Core). Core ships `DirectEmailSenderDispatcher` (default, forwards to `IEmailSender`). Optional integration package `Braikov.Identity.Notifications` ships `NotificationServiceDispatcher` that routes auth email through `INotificationService` — same audit/retry/persistence treatment as feature notifications. Opt-in via `.UseNotificationDispatcher()`.
6. **Migrations ownership:**
   - **Identity core tables** (AspNetUsers, AspNetRoles, ...) — host's responsibility. Host's DbContext inherits `IdentityDbContext<TUser, TRole, TKey>`; host generates and applies the migration in its own `*.MySql` project. Package ships only a recipe.
   - **Identity.Events table** (AccountEvent) — `Braikov.Identity.Events` ships its own `AccountEventDbContext` + bundled migration + `__BraikovIdentityEventsMigrationsHistory` history table.
   - **Identity.Passwordless tables** (LoginShortCode) — same pattern: own `LoginShortCodeDbContext` + bundled migration + `__BraikovIdentityPasswordlessMigrationsHistory`.
   - Same separation as `Braikov.Notifications.MySql` already does.
7. **Destructive operations on Squash/Jokes:** Acceptable to lose existing AccountEvent / LoginShortCode audit data. Drop via host-context migration, then create fresh tables under the package contexts.

## Architecture overview

```
Standalone repo:           C:\Projects\Miro\Braikov\
Local NuGet feed:          C:\Projects\Miro\NuGet\

Braikov.Identity.Core              (NuGet, mandatory)
  ├── Options (IdentityCookieOptions, IdentityPolicyOptions)
  ├── SetupIdentityCore<TUser, TRole, TKey>(IConfiguration, sectionName)
  ├── IEmailSender + IEmailTemplateRenderer abstractions
  ├── IIdentityEmailDispatcher abstraction + DirectEmailSenderDispatcher (default impl)
  ├── ViewModels (Login / Register / Forgot / Reset / ChangePassword) with resource keys
  ├── SharedResource.bg.resx + SharedResource.en.resx
  ├── BaseAccountController<TUser, TKey> — virtual actions for override; depends on IIdentityEmailDispatcher
  ├── Token URL helpers (Base64UrlEncode round-trip)
  └── IdentityDbContextBase<TUser, TRole, TKey> (optional base; hosts can also inherit IdentityDbContext directly)

Braikov.Identity.Notifications     (NuGet, optional integration with Braikov.Notifications.*)
  ├── NotificationServiceDispatcher : IIdentityEmailDispatcher (routes auth email through INotificationService)
  ├── Pre-defined NotificationTypeDefinition seeds: auth.email_confirmation / auth.password_reset / auth.password_changed
  ├── AddBraikovIdentityNotifications() extension — overrides DirectEmailSenderDispatcher registration
  └── Depends on Braikov.Identity.Core + Braikov.Notifications.Core

Braikov.Identity.Events            (NuGet, optional add-on)
  ├── AccountEvent entity + EF configuration
  ├── IAccountEventLog service
  ├── AccountEventType enum (LoginSucceeded, LoginFailed, LoginBlockedUnconfirmed, PasswordChanged, etc.)
  ├── AccountEventDbContext (own DbContext + own __BraikovIdentityEventsMigrationsHistory)
  └── Bundled initial migration (CreateAccountEventTable)

Braikov.Identity.Passwordless      (NuGet, optional add-on)
  ├── LoginShortCode entity + EF configuration
  ├── IShortCodeService (generate, verify, expire)
  ├── Passwordless flow controllers (BaseShortCodeController)
  ├── ShortCodeOptions
  ├── LoginShortCodeDbContext (own DbContext + own __BraikovIdentityPasswordlessMigrationsHistory)
  └── Bundled initial migration (CreateLoginShortCodeTable)

dotnet template:  braikov-identity   (separate package install)
  └── Scaffolds into target project:
      ├── ApplicationUser shell (inherits IdentityUser<TKey> with TKey of choice)
      ├── ApplicationDbContext shell (inherits IdentityDbContext<...>)
      ├── Areas/Identity/Controllers/AccountController.cs (inherits BaseAccountController, optional overrides)
      ├── Areas/Identity/Views/Account/* (branded HTML, all 10 views)
      ├── Areas/Identity/Views/Shared/_IdentityLayout.cshtml
      ├── Areas/App/ (Controllers/HomeController.cs + Views/Home/Index.cshtml + _AppLayout.cshtml)
      ├── wwwroot/identity/css/identity.css + wwwroot/app/css/app.css
      ├── Email templates: Templates/EmailConfirmation.bg-BG.cshtml + .en-GB.cshtml + same for PasswordReset / PasswordChanged
      └── Program.cs additions wired (AddBraikovIdentity, ConfigureApplicationCookie paths, AddDataAnnotationsLocalization)
```

## Phases

### Phase A — Braikov.Identity.Core ✅

A1. ✅ [Braikov.Identity.Core.csproj](../../../Braikov/Braikov.Identity.Core/Braikov.Identity.Core.csproj) — packaging mirroring Braikov.Notifications.Core. `FrameworkReference Include="Microsoft.AspNetCore.App"` (for MVC types), `PackageReference Microsoft.AspNetCore.Identity.EntityFrameworkCore 9.0.0`. Added to Braikov.Notifications.slnx solution.
A2. ✅ [BraikovIdentityOptions](../../../Braikov/Braikov.Identity.Core/BraikovIdentityOptions.cs) — bound from `Identity:` config section. Sub-sections: Cookie (Name/HttpOnly/SameSite/SecurePolicy/ExpireTimeSpanDays/SlidingExpiration/Login+Logout+AccessDenied paths), Password (length/digit/lowercase/uppercase/nonalphanumeric/uniquechars), User (RequireUniqueEmail, AllowedUserNameCharacters), SignIn (3 ConfirmedX flags), Lockout (Allowed/MaxFailedAccessAttempts/DefaultLockoutTimeSpanMinutes).
A3. ✅ [DependencyInjection.AddBraikovIdentity<TUser, TRole>](../../../Braikov/Braikov.Identity.Core/DependencyInjection.cs) — binds options, calls AddIdentity, wires ConfigureApplicationCookie, registers default IIdentityEmailDispatcher. Returns IdentityBuilder so host chains `.AddEntityFrameworkStores<MyDbContext>().AddDefaultTokenProviders()`.
A4. ✅ ViewModels with resource keys: [LoginViewModel](../../../Braikov/Braikov.Identity.Core/ViewModels/LoginViewModel.cs), [RegisterViewModel](../../../Braikov/Braikov.Identity.Core/ViewModels/RegisterViewModel.cs), [ForgotPasswordViewModel](../../../Braikov/Braikov.Identity.Core/ViewModels/ForgotPasswordViewModel.cs), [ResetPasswordViewModel](../../../Braikov/Braikov.Identity.Core/ViewModels/ResetPasswordViewModel.cs), [ChangePasswordViewModel](../../../Braikov/Braikov.Identity.Core/ViewModels/ChangePasswordViewModel.cs). All `[Required]/[Display]` use resource keys (`Auth.Email.Required`, etc.), not literals.
A5. ✅ Resources: [SharedResource.resx](../../../Braikov/Braikov.Identity.Core/Resources/SharedResource.resx) (neutral/EN fallback), `.bg.resx` (BG translations), `.en.resx` (explicit EN). 19 keys covering field labels, validation messages, controller error messages. SDK auto-embeds as satellite assemblies (verified in nupkg: `lib/net10.0/bg/Braikov.Identity.Core.resources.dll`).
A6. ✅ [IEmailSender](../../../Braikov/Braikov.Identity.Core/Abstractions/IEmailSender.cs) + [IEmailTemplateRenderer](../../../Braikov/Braikov.Identity.Core/Abstractions/IEmailTemplateRenderer.cs) abstractions, contract identical to Accountant.Email. No concrete sender — that stays per host.
A7. ✅ [IIdentityEmailDispatcher](../../../Braikov/Braikov.Identity.Core/Abstractions/IIdentityEmailDispatcher.cs) + [DirectEmailSenderDispatcher](../../../Braikov/Braikov.Identity.Core/Dispatchers/DirectEmailSenderDispatcher.cs) default impl (registered Scoped by AddBraikovIdentity).
A8. ✅ [BaseAccountController<TUser, TKey>](../../../Braikov/Braikov.Identity.Core/Controllers/BaseAccountController.cs) — 14 virtual actions (Login + Logout + Register + RegisterConfirmation + ConfirmEmail + ForgotPassword + ForgotPasswordConfirmation + ResetPassword + ResetPasswordConfirmation + ChangePassword + ChangePasswordConfirmation + AccessDenied). `[Area("Identity")] [Route("Identity/Account/[action]")]` baked into the base. Abstract `CreateUser(RegisterViewModel)` factory for host-specific ApplicationUser construction. `LocalRedirectSafe` / `RedirectAfterLogout` virtual override points. Depends on IIdentityEmailDispatcher.
A9. ✅ [IdentityTokenEncoder](../../../Braikov/Braikov.Identity.Core/Tokens/IdentityTokenEncoder.cs) — Encode / Decode / TryDecode for Identity tokens through Base64Url.
A10. ✅ README.md + LICENSE.txt embedded; `dotnet pack -c Release` produced `C:\Projects\Miro\NuGet\Braikov.Identity.Core.0.1.0.nupkg` (28 KB). Verified Accountant.Web can `dotnet add package Braikov.Identity.Core` and build clean — package restorable from local feed.

**Deliverable:** ✅ package builds clean (0 warnings, 0 errors), installs into Accountant.Web from local feed, compiles transitively. Auth email flows through `IIdentityEmailDispatcher` → `DirectEmailSenderDispatcher` → `IEmailSender` by default; ready for Phase B's optional override.

### Phase B — Braikov.Identity.Notifications ✅

B1. ✅ [Braikov.Identity.Notifications.csproj](../../../Braikov/Braikov.Identity.Notifications/Braikov.Identity.Notifications.csproj) with PackageReferences to `Braikov.Identity.Core`, `Braikov.Notifications.Core`, `Braikov.Notifications.Email` (all 0.1.0 from local feed). FrameworkReference `Microsoft.AspNetCore.App`.
B2. ✅ [NotificationServiceDispatcher<TUser>](../../../Braikov/Braikov.Identity.Notifications/NotificationServiceDispatcher.cs) — generic over the host's user type. Resolves `UserManager<TUser>` to look up user-by-email and pass the host-stable id as `RecipientId`. Sends through `INotificationService.SendAsync(NotificationRequest)` with TypeKey + serialized payload (EmailConfirmationPayload / PasswordResetPayload / PasswordChangedPayload). Defensive: logs + returns silently if user not found.
B3. ✅ [BraikovIdentityNotificationTypeKeys + BraikovIdentityEmailTemplateKeys](../../../Braikov/Braikov.Identity.Notifications/BraikovIdentityNotificationTypeKeys.cs) — wire-level constants for the three auth flows. `NotificationTypeDefinition` seeds in DI: each is Required policy + Email-only + `CreateInboxRecord=false`.
B4. ✅ [UseNotificationDispatcher()](../../../Braikov/Braikov.Identity.Notifications/DependencyInjection.cs) — extension on `IdentityBuilder` (UserType auto-detected, no generic specification needed at call site). Removes the default `DirectEmailSenderDispatcher` registration via `services.Remove(existing)` then registers `NotificationServiceDispatcher<TUser>` via reflection (`MakeGenericType`). Also seeds the three `NotificationTypeDefinition`s.
B5. ✅ README + pack — `C:\Projects\Miro\NuGet\Braikov.Identity.Notifications.0.1.0.nupkg`. Build clean.

**Deliverable:** ✅ projects with `Braikov.Notifications.*` already wired can route auth email through the notification pipeline with a single chained `.UseNotificationDispatcher()` call after `AddBraikovIdentity`. Projects without notifications skip the package entirely.

### Phase C — Braikov.Identity.Events ✅

C1-C8. ✅ Package built + packed. [Braikov.Identity.Events.0.1.0.nupkg](../../../NuGet/Braikov.Identity.Events.0.1.0.nupkg) in local feed.
   - [AccountEvent.cs](../../../Braikov/Braikov.Identity.Events/AccountEvent.cs) — entity (Id bigint identity, EventType varchar(64), UserId varchar(450), Email varchar(256), IpAddress varchar(45), UserAgent varchar(512), ContextJson text, OccurredAtUtc datetime)
   - [AccountEventDbContext.cs](../../../Braikov/Braikov.Identity.Events/AccountEventDbContext.cs) — separate DbContext with own `__BraikovIdentityEventsMigrationsHistory`
   - 3 indexes on (UserId), (EventType, OccurredAtUtc), (Email, OccurredAtUtc)
   - [EfAccountEventLog.cs](../../../Braikov/Braikov.Identity.Events/EfAccountEventLog.cs) — persists each call; swallows DB exceptions (log warning) so audit failure never blocks auth flow
   - [DesignTimeAccountEventDbContextFactory.cs](../../../Braikov/Braikov.Identity.Events/DesignTimeAccountEventDbContextFactory.cs) — for EF tools
   - Migration `20260511083322_CreateAccountEventTable` generated, compiled into DLL
   - [DependencyInjection.cs](../../../Braikov/Braikov.Identity.Events/DependencyInjection.cs) — `AddBraikovIdentityEvents(connectionString)` wires DbContext + replaces NullAccountEventLog with EfAccountEventLog
   - Also extended Core with [IAccountEventLog](../../../Braikov/Braikov.Identity.Core/Abstractions/IAccountEventLog.cs) abstraction + AccountEventEntry record + [AccountEventTypes](../../../Braikov/Braikov.Identity.Core/Abstractions/IAccountEventLog.cs) constants (11 wire strings) + [NullAccountEventLog](../../../Braikov/Braikov.Identity.Core/Dispatchers/NullAccountEventLog.cs) default. Updated `BaseAccountController` to call `EventLog.LogAsync` at every relevant hook (Login succeeded/failed/locked/unconfirmed, Register, EmailConfirmed, EmailConfirmationFailed, PasswordResetRequested, PasswordResetCompleted, PasswordChanged, Logout). Repacked Core.

**Deliverable:** ✅ drop-in audit log opt-in via single `AddBraikovIdentityEvents` call + `dotnet ef database update --context AccountEventDbContext`. Projects that don't install the package get NullAccountEventLog (no-op).

### Phase D — Braikov.Identity.Passwordless ✅

D1-D9. ✅ Package built + packed. [Braikov.Identity.Passwordless.0.1.0.nupkg](../../../NuGet/Braikov.Identity.Passwordless.0.1.0.nupkg) in local feed.
   - [ShortCodeOptions.cs](../../../Braikov/Braikov.Identity.Passwordless/ShortCodeOptions.cs) — CodeLength=6, LifetimeMinutes=10, MaxAttempts=5, RequestCooldownSeconds=60
   - [LoginShortCode.cs](../../../Braikov/Braikov.Identity.Passwordless/LoginShortCode.cs) — entity (Id bigint, Email varchar(256), CodeHash varchar(128), CreatedAtUtc, ExpiresAtUtc, ConsumedAtUtc?, AttemptCount)
   - [LoginShortCodeDbContext.cs](../../../Braikov/Braikov.Identity.Passwordless/LoginShortCodeDbContext.cs) — separate context, own `__BraikovIdentityPasswordlessMigrationsHistory`, 2 indexes for active-code lookup + cooldown check
   - [IShortCodeService](../../../Braikov/Braikov.Identity.Passwordless/IShortCodeService.cs) — GenerateForLoginAsync (cooldown-aware) / VerifyAsync (attempt-counted) / ExpireAllAsync
   - [EfShortCodeService.cs](../../../Braikov/Braikov.Identity.Passwordless/EfShortCodeService.cs) — cryptographically-secure 6-digit code, SHA256+Id-salt hash at rest, FixedTimeEquals verify
   - [BaseShortCodeController<TUser>](../../../Braikov/Braikov.Identity.Passwordless/Controllers/BaseShortCodeController.cs) — generic, `[Area("Identity")]`. Routes: PasswordlessRequest GET/POST, PasswordlessVerify GET/POST. Anti-enumeration on Request (silent succeed for missing/unconfirmed). Successful Verify → SignIn + AccountEventTypes.LoginSucceeded + ExpireAllAsync (invalidates sibling codes)
   - [PasswordlessRequestViewModel + PasswordlessVerifyViewModel](../../../Braikov/Braikov.Identity.Passwordless/ViewModels/) — resource-keyed
   - 3 new resource keys in Core's SharedResource.resx (BG+EN+neutral): Auth.Code.Display/Required/Invalid
   - Migration `20260511_CreateLoginShortCodeTable` generated, compiled into DLL
   - [DependencyInjection.cs](../../../Braikov/Braikov.Identity.Passwordless/DependencyInjection.cs) — `AddBraikovIdentityPasswordless(configuration, connectionString)` wires DbContext + binds options + registers IShortCodeService

**Out of scope (deferred):** the legacy `ShortCodeToToken` table in Squash/Jokes is a different feature (short-friendly Identity tokens for mobile UX) — not the same as `LoginShortCode` (passwordless login state). Phase F/G drops it; a future task can re-add as `Braikov.Identity.Passwordless` v0.2 if/when needed.

**Deliverable:** ✅ new project can opt in to short-code login with `AddBraikovIdentityPasswordless` + inherit `BaseShortCodeController` + scaffold 2 views + 1 email template. Accountant skips this package entirely.

### Phase E — Pre-flight check (Squash + Jokes) ⏳ Чакаща

Cheap investigation before destructive migration work in Phase F/G. ~1 hour total.

E1. Inspect `dev_squash` + `dev_jokes` schemas: confirm presence of `accountevents`, `loginshortcodes`, and the unknown `shortcodetotokens` table. Note column names + row counts.
E2. Foreign-key check:
   ```sql
   SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME
   FROM information_schema.KEY_COLUMN_USAGE
   WHERE REFERENCED_TABLE_NAME IN ('accountevents','loginshortcodes','shortcodetotokens');
   ```
   Any FK pointing IN means DROP will fail; flag for follow-up.
E3. Identify what `ShortCodeToToken` is for (grep source for entity references). Decide: drop alongside the others, or carry-forward as a separate entity in `Braikov.Identity.Passwordless`.
E4. Confirm Squash/Jokes use the same migration history table name (`__IdentityMigrationsHistory`). Document column shape so the drop migration applies cleanly.
E5. Inventory `Squash.Identity` + `Jokes.Identity` overrides on top of vanilla Identity (custom EF configurations, route constraints, localization keys). These stay in the host — package only takes over the truly common surface.

**Deliverable:** a short note in this file (under "Pre-flight findings") documenting exactly what's where, plus a yes/no on whether Phase F/G can run as designed or need adjustment.

### Phase F — Migrate Squash ⏳ Чакаща

F1. Add package references to `Squash.Identity` + `Squash.Web`: `Braikov.Identity.Core`, `Braikov.Identity.Events`, `Braikov.Identity.Passwordless`. Optional: `Braikov.Identity.Notifications` if Squash has Notifications wired.
F2. **Code:** Replace `SetupIdentityCore` extension method body with call to package's `SetupIdentityCore`. Keep wrapper as thin shim so existing call sites compile.
F3. **Code:** Remove entity types `AccountEvent`, `LoginShortCode`, (`ShortCodeToToken` if Phase E says yes) from `Squash.Identity.ApplicationDbContext`. Remove the EF configurations and DbSet properties.
F4. **Code:** Replace local ViewModels with package-shipped ones; verify localization resolves through `SharedResource.{bg,en}.resx` fallback chain.
F5. **Code:** Refactor `AccountController` to inherit `BaseAccountController<ApplicationUser, string>`; keep only Squash-specific overrides (short-code UI thin layer, custom event logging hooks).
F6. **Migration (host context, destructive):**
   ```
   cd C:\Projects\Miro\Squash\Source
   dotnet ef migrations add DropLegacyIdentityTables --project Squash.MySql --context ApplicationDbContext
   ```
   Verify generated SQL — it should be DROP TABLE for the 2-3 affected tables. Reject and abort if it tries to drop AspNet* tables.
F7. **Apply locally:** `dotnet ef database update --context ApplicationDbContext`. Verify in MariaDB: tables gone, `__IdentityMigrationsHistory` has new entry.
F8. **Apply locally — package contexts:** create fresh tables.
   ```
   dotnet ef database update --context AccountEventDbContext        # creates accountevents + __BraikovIdentityEventsMigrationsHistory
   dotnet ef database update --context LoginShortCodeDbContext       # creates loginshortcodes + __BraikovIdentityPasswordlessMigrationsHistory
   ```
F9. **Apply prod:** Generate idempotent script for the host-context DROP migration; SCP + run on `vic.bg` against `squash` DB (same pattern as Accountant Phase A5 + 0002 B7). Then apply package-context migrations via dotnet ef against prod connection.
F10. **Smoke test:** Run Squash, exercise login / register / passwordless flow / change password. Compare against pre-migration behavior. Acceptance bar: no functional regression in user-visible flows. Event audit starts fresh from this point — that's accepted data loss.

**Deliverable:** Squash runs entirely on `Braikov.Identity.*` packages; legacy AccountEvent/LoginShortCode data is gone (intentional); auth flows behave identically.

### Phase G — Migrate Jokes ⏳ Чакаща

G1. Same migration as Squash. Structurally identical, less complete, no real user data. Should be ~30 minutes if Squash succeeds cleanly.
G2. While here — wire up real `IEmailSender` (Jokes was `NullEmailSender` only). Validates that the package's email contract works end-to-end in a second host. Optional but cheap.

**Deliverable:** Jokes on packages, real email working.

### Phase H — Migrate Accountant ⏳ Чакаща

H1. Add package references: `Braikov.Identity.Core`, `Braikov.Identity.Notifications` (we have notifications), optionally `Braikov.Identity.Events`. Skip `Braikov.Identity.Passwordless`.
H2. Refactor `Program.cs` from inline `AddIdentity` block to `services.AddBraikovIdentity<ApplicationUser, IdentityRole<int>, int>(Configuration, "Identity")`. Move BG-specific policy values into `appsettings.json` under `Identity:` section.
H3. Add `services.UseNotificationDispatcher()` (from Phase B). Now auth email routes through `INotificationService` → `AccountantNotificationEmailSender` (the bridge stays — it's the host-specific `INotificationEmailSender` impl) → `IEmailSender` (Accountant.Email).
H4. **Delete duplicate registrations** in `Accountant.Notifications/DependencyInjection.cs`: the three `NotificationTypeDefinition` seeds for `auth.email_confirmation` / `auth.password_reset` / `auth.password_changed` move to `Braikov.Identity.Notifications`. `Accountant.Notifications` keeps only `AccountantRecipientResolver` (host-specific) and `AccountantNotificationEmailSender` (host-specific bridge).
H5. Refactor `AccountController` to inherit `BaseAccountController<ApplicationUser, int>`. Validate `[Route("Identity/Account/[action]")]` routing still works (or shift to base class).
H6. Replace local ViewModels with package ones. Verify BG validation messages render correctly through the localizer (Accountant was BG-hardcoded literals; this is the real test of the resource-key approach).
H7. Optional: add `Braikov.Identity.Events` + `dotnet ef database update --context AccountEventDbContext`. Purely additive — no destructive ops in Accountant.
H8. Re-run yesterday's end-to-end smoke (register → confirm → login → /App → ChangePassword → logout) — must produce identical behavior. With Notifications integration, verify `notifications` + `notificationdeliveries` rows now appear for auth emails.

**Deliverable:** Accountant on packages. No DB destructive ops. Auth email now flows through the notification pipeline (audited + persistent).

### Phase I — `dotnet new` template ⏳ Чакаща

I1. Create a `templates/braikov-identity/` folder in the standalone Braikov repo.
I2. Author the template's `.template.config/template.json` with parameters (project name, key type, default culture, include-passwordless, include-events, include-notifications-integration).
I3. Scaffold content from migrated Accountant (cleanest password Identity surface) — `Areas/Identity/Views/*`, `Areas/Identity/Views/Shared/_IdentityLayout.cshtml`, `Areas/App/*`, `wwwroot/identity/css`, `wwwroot/app/css`, email templates, `ApplicationUser` shell, `ApplicationDbContext` shell, `Program.cs` insertions wrapped in a partial method or wired comment block.
I4. Pack as `Braikov.Templates.Identity` and install with `dotnet new install`.
I5. Smoke test: `dotnet new mvc -n Foo.Web && cd Foo.Web && dotnet new braikov-identity` produces a project that builds, registers, logs in, and renders the App placeholder.
I6. Update [useful_commands.md](../../useful_commands.md) with the bootstrap recipe.

**Deliverable:** a new project from zero to working register/login in under 10 minutes.

### Phase J — Documentation ⏳ Чакаща

J1. README per package (Core, Notifications, Events, Passwordless) — usage, version, breaking-change policy.
J2. Top-level README in `C:\Projects\Miro\Braikov\` listing all packages + template.
J3. Update [Project_Structure.md](../Project_Structure.md) in Accountant — section 11 already covers Braikov.Notifications, add a parallel section about Braikov.Identity.
J4. Add to [useful_commands.md](../../useful_commands.md): pack/publish workflow (already exists for Notifications; this is one more entry in the same section).

## Pre-flight findings (Phase E)

**Critical discovery (2026-05-11):** Squash + Jokes use **SQL Server**, Accountant uses **MySQL**. This was missed during initial planning; we assumed all four were MySQL. Verified via grep:

```
/c/Projects/Miro/Squash/Squash.Identity/ServiceCollectionExtensions.cs: options.UseSqlServer(...)
/c/Projects/Miro/Jokes/Jokes.Identity/ServiceCollectionExtensions.cs:   options.UseSqlServer(...)
/c/Projects/Miro/Accountant/Source/Accountant.Web/Program.cs:           options.UseMySql(...)
```

Squash dev: `Server=(local)\NoFate;Database=Dev-squash;Trusted_Connection=True` (NOFATE service running locally).
Jokes dev: SQL Server (similar setup — confirm in Phase G).

**Resolution: refactored Events + Passwordless into provider-agnostic core + per-provider sub-packages.** From 4 packages to **8 total**:

| Package | Purpose |
|---|---|
| `Braikov.Identity.Core` | Options, ViewModels, BaseAccountController, IIdentityEmailDispatcher, IAccountEventLog abstractions, BG+EN resources |
| `Braikov.Identity.Notifications` | NotificationServiceDispatcher (opt-in INotificationService routing) |
| `Braikov.Identity.Events` | AccountEvent entity + DbContext + EfAccountEventLog (provider-agnostic) |
| `Braikov.Identity.Events.MySql` | Pomelo provider + bundled MySQL migration |
| `Braikov.Identity.Events.SqlServer` | Microsoft.EntityFrameworkCore.SqlServer + bundled SqlServer migration |
| `Braikov.Identity.Passwordless` | LoginShortCode entity + IShortCodeService + BaseShortCodeController (provider-agnostic) |
| `Braikov.Identity.Passwordless.MySql` | Pomelo provider + bundled MySQL migration |
| `Braikov.Identity.Passwordless.SqlServer` | Microsoft.EntityFrameworkCore.SqlServer + bundled SqlServer migration |

All 8 are built + packed in `C:\Projects\Miro\NuGet\` at version 0.1.0. New combined `Braikov.slnx` (replaces narrow `Braikov.Notifications.slnx`).

**Foreign key check (deferred):** Need to verify on `Dev-squash` / `dev-jokes` SqlServer DBs whether any tables reference `AccountEvents`, `LoginShortCodes`, or `ShortCodeToTokens` before the drop migration in Phase F/G. Will run as first step of Phase F.

**ShortCodeToToken:** Confirmed as separate feature from LoginShortCode (short-friendly Identity tokens for mobile UX, not passwordless login state). Out of scope for this task — drop alongside others in Phase F/G; future task adds it back if needed.

## Open implementation questions (resolved before Phase A)

- **Pakage naming:** `Braikov.Identity.Core` chosen for parallelism with `Braikov.Notifications.Core`. Template name `braikov-identity` (short-name) / `Braikov.Templates.Identity` (package).
- **EF migrations strategy for Events / Passwordless:** Each optional package owns its own migrations + history table (`__BraikovIdentityEventsMigrationsHistory`, `__BraikovIdentityPasswordlessMigrationsHistory`). Same pattern as `Braikov.Notifications.MySql`.
- **MailKit transport:** Stays per host (Accountant.Email, Squash.Email, etc.). Package only ships the `IEmailSender` contract + template renderer + razor-light support. Branded email templates ship in the `dotnet new` template, not in the package.

## Status icons per phase / step

When working a phase, mark steps as done inline:

- ⏳ Чакаща
- 🔄 В процес
- ✅ Завършена

## Risks

- **BG resx fallback:** ASP.NET Core localizer resolution from a NuGet'd resource file has gotchas with embedded vs satellite assemblies. Validate end-of-Phase-A before Phase D commits.
- **Squash's session management + bilingual routing:** Squash has more than vanilla Identity — `culture:regex(^bg|en$)` route constraint, session timeout pattern. These don't belong in the package; keep them in Squash's wrapper. Migration's risk: forgetting that and breaking Squash's existing UX.
- **Generic over TKey:** `BaseAccountController<TUser, TKey>` action method signatures + EF queries get gnarlier with two type params. Need a clean smoke before committing.
- **Template install footprint:** `dotnet new` templates have their own quirks (post-actions, file conditions). Phase G needs proper testing on a clean folder.
