# Проектна структура: Accountant

Този документ описва архитектурата на C# (.NET 10 / C# 14) проекта Accountant — система за извличане на данни от изображения на български платежни документи (фактури, касови бонове, проформи и др.) през множество AI vendor-и.

Документът е жив. При промяна в структурата на solution-а или ролите на проектите — обнови този файл в същия change set.

## Текущ scope

Проектът е в преходна фаза — основната research работа (vendor prompts + extraction contract) е stable, и production foundation-а сега се вдига:

1. **Vendor extraction (research-grade, mostly stable)** — `Accountant.<Vendor>` проектите държат promptа, model config и call логиката за един vendor. Споделен contract в `Accountant.Contracts`. Console sandbox в `Accountant.Processors`.
2. **Web foundation (active)** — `Accountant.Web` (MVC + Areas) с публичен landing (`Areas/Public`), authenticated landing (`Areas/App`), и пълен auth flow (`Areas/Identity`).
3. **Persistence (active)** — `Accountant.DataAccess` + `Accountant.MySql` за бизнес entities + ASP.NET Core Identity tables в общ `AccountantDbContext`. EF Core 9.0 (pinned за Pomelo compat) + Pomelo.EntityFrameworkCore.MySql.
4. **Auth + email + notifications (active)** — `Accountant.Identity` (ApplicationUser), `Accountant.Email` (RazorLight + MailKit + SMTP), `Accountant.Notifications` (host adapter върху Braikov.Notifications.* пакетите).

**Засега не интегрирано:**

- Background jobs (Hangfire) — добавя се при scheduled batch extractions
- Vendor extractors на Web ниво — `IAccountingDocumentExtractor` все още се consume-ва само от `Accountant.Processors`
- Roles / multi-tenant boundary — Identity вдигнат с базова user table; admin vs client роли идват с първата feature task

## 1. Хранилище

**Mono-repo**, локално в `c:\Projects\Miro\Accountant\`. Засега не публикуван в GitHub.

Repo-то комбинира:
- **C# имплементация** (новата работа) — под `Source\`
- **Python research артефакти** — `Claude\`, `Codex\`, `Gemini\`, `Unified_Extraction_Contract\`, `ReviewSite\`, `docs\facturi\` (тестов corpus + референтни outputs)
- **Споделена документация** — `docs\`

Python research-ът е **frozen reference**. C# имплементацията не модифицира Python кода или историческите outputs — само ги чете като benchmark спрямо който трябва да съвпадне (или да обясни защо се различава).

## 2. Структура на root ниво

```text
c:\Projects\Miro\Accountant\
│
├── AGENTS.md                              # инструкции за всички AI агенти (sync-нат с CLAUDE.md)
├── CLAUDE.md                              # инструкции специално за Claude (sync-нат с AGENTS.md)
├── NuGet.Config                           # (бъдещ) NuGet feeds
│
├── docs\                                  # споделена документация
│   ├── Project_Structure.md               # този документ
│   ├── tasks\                             # план/прогрес на отделни tasks
│   │   └── README.md                      # task tracker
│   ├── followups.md                       # отложени cleanup-и, gaps между сесиите
│   └── facturi\                           # 23-doc тестов corpus + референтни Claude/Codex/Gemini JSON-и
│
├── Unified_Extraction_Contract\           # schema + правила (R1-R13) + example — source of truth за extraction формата
│   ├── EXTRACTION_CONTRACT.md
│   ├── NORMALIZATION_RULES.md
│   ├── accountant.document.v2.schema.json
│   ├── example.result.json
│   └── ...
│
├── Claude\, Codex\, Gemini\               # Python research (frozen, само за reference)
├── ReviewSite\                            # Python diff/review tooling (frozen)
│
└── Source\                                # C# solution (новата работа)
    ├── Accountant.slnx
    ├── Accountant.Web\                    # ASP.NET Core MVC: Areas/{Public, Identity, App}
    ├── Accountant.DataAccess\             # entities + AccountantDbContext (business + Identity tables)
    ├── Accountant.MySql\                  # MySQL provider, EF migrations за AccountantDbContext
    ├── Accountant.Identity\               # ApplicationUser : IdentityUser<int>
    ├── Accountant.Email\                  # RazorLight templates + MailKit SMTP sender + IEmailSender
    ├── Accountant.Notifications\          # host adapter върху Braikov.Notifications.* (recipient resolver, email channel bridge, type definitions)
    ├── Accountant.Contracts\              # IAccountingDocumentExtractor, ExtractionResult DTOs, R1-R13 валидатори, нормализация
    ├── Accountant.Claude\                 # IAccountingDocumentExtractor implementation (Anthropic SDK)
    ├── Accountant.Codex\                  # IAccountingDocumentExtractor implementation (OpenAI SDK)
    ├── Accountant.Gemini\                 # IAccountingDocumentExtractor implementation (Google AI SDK)
    ├── Accountant.Processors\             # console app — sandbox / batch processing CLI
    ├── Accountant.ReviewSite\             # отделен IIS site — browser tooling за review/diff на vendor outputs
    └── Accountant.Tests\                  # xUnit тестове (validators, normalization, contract conformance)
```

## 3. Solution структура (`Source\Accountant.slnx`)

```text
Source\
│
├── Accountant.Web              # (.NET MVC) Web host. MVC + Areas:
│                               #   - Areas/Public: marketing landing (drop-as-a-unit)
│                               #   - Areas/Identity: auth UI (Login/Register/Forgot/Reset/ChangePassword)
│                               #   - Areas/App: authenticated landing
│                               #   - Controllers/DevDiagnosticsController: dev-only /dev/test-email + /dev/render-template
│
├── Accountant.DataAccess       # (Class Library) AccountantDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>.
│                               # Business entities (SourceDocument, Extraction, GroundTruth, ...) + Identity tables co-located.
├── Accountant.MySql            # (Class Library) MySQL provider, EF Core migrations за AccountantDbContext (Pomelo, EF Core 9.0).
├── Accountant.Identity         # (Class Library) ApplicationUser : IdentityUser<int>. Минимална, разширява се с domain полета при нужда.
│
├── Accountant.Email            # (Razor Class Library) RazorLight email templates, MailKit-based SmtpEmailSender,
│                               # NullEmailSender (dev), IEmailSender contract. Templates под Templates/<Name>.<culture>.cshtml.
├── Accountant.Notifications    # (Class Library) Host adapter за Braikov.Notifications.* пакетите.
│                               # AccountantRecipientResolver, AccountantNotificationEmailSender (мост към Accountant.Email),
│                               # NotificationTypeDefinition-и за auth flows (email_confirmation, password_reset, password_changed).
│
├── Accountant.Contracts        # (Class Library) Споделените интерфейси (IAccountingDocumentExtractor), DTO-та (ExtractionResult и под-блоковете на v2 schema), enum-и, R1-R13 валидатори, нормализационни helpers.
│
├── Accountant.Claude           # (Class Library) IAccountingDocumentExtractor имплементация през Anthropic SDK (Claude Sonnet като primary VLM).
├── Accountant.Codex            # (Class Library) IAccountingDocumentExtractor имплементация през OpenAI SDK.
├── Accountant.Gemini           # (Class Library) IAccountingDocumentExtractor имплементация през Google.Cloud.AIPlatform или официален Gemini SDK.
│
├── Accountant.Processors       # (Console App) Sandbox / batch CLI за изпълнение на различни задачи: пакетна екстракция върху corpus, diff между vendor-и, нормализация на исторически JSON-и, eval скриптове.
│
├── Accountant.ReviewSite       # (.NET MVC) Отделен IIS site (`accountant-tune.ima.bg`) за browser-based diff/review на vendor outputs + GroundTruth editor. BasicAuth.
│
└── Accountant.Tests            # (xUnit) Unit тестове върху валидаторите, нормализацията, contract conformance срещу schema-та.
```

## 4. Зависимости между проектите

**Web host:**

```text
Accountant.Web
  ├── Accountant.DataAccess           (AccountantDbContext)
  ├── Accountant.MySql                (DI: AddAccountantMySql)
  ├── Accountant.Email                (IEmailSender, AddAccountantEmail)
  ├── Accountant.Notifications        (AddAccountantNotifications — pulls Braikov.Notifications.* transitively)
  └── (transitive) Accountant.Identity (ApplicationUser via DataAccess)
```

**Vendor extraction:**

```text
Accountant.Claude / Codex / Gemini
  └── Accountant.Contracts            (имплементират IAccountingDocumentExtractor от тук)

Accountant.Processors
  ├── Accountant.Contracts
  └── Accountant.Claude / Codex / Gemini    (per-CLI команда се избира кой extractor да се използва)
```

**Persistence + Identity:**

```text
Accountant.DataAccess
  └── Accountant.Identity             (ApplicationUser типа)

Accountant.MySql
  └── Accountant.DataAccess

Accountant.Notifications
  ├── Accountant.DataAccess           (lookup на ApplicationUser)
  ├── Accountant.Email                (IEmailSender за email channel)
  └── Braikov.Notifications.* (Core / DataAccess / MySql / Email — local NuGet feed)
```

**Tests:**

```text
Accountant.Tests
  ├── Accountant.Contracts            (тества validators, normalization)
  └── Accountant.Claude / Codex / Gemini    (по преценка — integration тестове, ръчно)
```

**Правила:**
- `Accountant.Contracts` НЕ зависи от vendor-specific SDK-ове. Това е neutral слой.
- Всеки vendor проект зависи САМО от `Accountant.Contracts` + неговия конкретен SDK. Не зависи от другите vendor-и.
- `Accountant.Web` НЕ имплементира extraction логика — само consum-ва `IAccountingDocumentExtractor` през DI (когато стане активен).
- `Accountant.Processors` е CLI sandbox — позволено е "разни задачи свързани с разработката". Не пуска production-grade код.
- `Accountant.Email` и `Accountant.Notifications` са разделени защото имейл renderer-ът е reusable (никаква нотификация специфика), а notifications adapter-ът е host-specific (специфичен за `ApplicationUser`).
- `Accountant.Web` Areas са drop-as-a-unit модули — всяко Area има own layout, own CSS namespace под `wwwroot/<area>/`, own README с removal procedure.

## 5. Контракт на основния интерфейс

`Accountant.Contracts/Extraction/IAccountingDocumentExtractor.cs`:

```csharp
public interface IAccountingDocumentExtractor
{
    Task<IReadOnlyList<ExtractionResult>> ExtractAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);
}
```

`ExtractionResult` DTO е C# огледало на v2 schema-та от
[`Unified_Extraction_Contract/accountant.document.v2.schema.json`](../Unified_Extraction_Contract/accountant.document.v2.schema.json) — top-level: `SchemaVersion`, `Source`, `Extraction`, `Validation`, `ModelAssessment`, `Evidence`, `Provider`. Всеки блок е отделен record/class със същите имена, snake_case → PascalCase, със `[JsonPropertyName]` атрибути за wire-формата.

DTO-тата са имутабилни (records с init-only properties).

## 6. Source of truth за extraction формата

C# DTO-тата и валидаторите ОТРАЗЯВАТ, не дефинират контракта. Authoritative source-ът остава:

- **Schema (machine-readable):** [`Unified_Extraction_Contract/accountant.document.v2.schema.json`](../Unified_Extraction_Contract/accountant.document.v2.schema.json)
- **Spec (human-readable):** [`Unified_Extraction_Contract/EXTRACTION_CONTRACT.md`](../Unified_Extraction_Contract/EXTRACTION_CONTRACT.md)
- **Нормализационни правила R1-R13:** [`Unified_Extraction_Contract/NORMALIZATION_RULES.md`](../Unified_Extraction_Contract/NORMALIZATION_RULES.md)
- **Пример:** [`Unified_Extraction_Contract/example.result.json`](../Unified_Extraction_Contract/example.result.json)

При промяна в контракта (нови R-правила, schema bump): първо се обновява `Unified_Extraction_Contract/`, след това се отразява в C# DTO-тата и валидаторите. Никога обратното. `Accountant.Tests` валидира че сериализиран `ExtractionResult` минава JSON Schema-та.

## 7. Конфигурация и среди

Стандартен .NET pattern. Засега в research фазата е достатъчно `appsettings.json` + user-secrets за локална разработка. Multi-environment setup (Development / Test / Production) се добавя когато се излезе от research mode.

Vendor API ключове (Anthropic, OpenAI, Google) — `dotnet user-secrets` локално, environment variables на сървър. Никога не commit-ват в repo-то.

## 8. Логване

Засега console logging е достатъчно за `Accountant.Processors`. Serilog + MySQL sink се добавя когато тръгне Web/persistence work.

Structured logging е препоръчителен pattern от старта — vendor calls, latency, token usage, cost estimates трябва да минават като structured properties, дори когато sink-ът е само console. Това спестява rewrite по-късно.

## 9. Тестване

- **`Accountant.Tests`** (xUnit):
  - Валидация на R1-R13 нормализация helpers
  - Contract conformance: сериализиран `ExtractionResult` минава JSON Schema-та
  - Validators: EIK checksum, IBAN mod-97, BIC regex, totals reconciliation, VAT breakdown
  - Snapshot тестове срещу референтни JSON-и в `docs/facturi/`

- **Integration тестове** (per-vendor): пускат се ръчно или по cron, не на всеки build (костват пари за vendor API). Резултатът се сравнява с историческия benchmark.

- **Web user-facing flows:** няма Playwright e2e foundation засега. Ако се добави Web UI работа — добавя се отделно.

## 10. Background Jobs

Hangfire (както Assistant), MySQL storage. **Засега не задължително** — добавя се когато имаме нужда от scheduled extraction batches или async re-runs. Dashboard в Web admin секция.

## 11. Notifications

`Braikov.Notifications.*` пакетите идват от standalone repo `C:\Projects\Miro\Braikov\` и се publish-ват като .nupkg в local feed `C:\Projects\Miro\NuGet\`. Accountant ги consume-ва през `NuGet.Config` (key `braikov-local`).

- **Used packages:** `Braikov.Notifications.Core / DataAccess / MySql / Email` (0.1.0)
- **Host adapter:** `Accountant.Notifications` — `AccountantRecipientResolver` (int-keyed `ApplicationUser` → `NotificationRecipient`), `AccountantNotificationEmailSender` (мост към `Accountant.Email.IEmailSender`), `NotificationTypeDefinition`-и за auth flows.
- **DI wiring:** `Accountant.Web/Program.cs` вика `AddAccountantEmail` → `AddAccountantNotifications` (order matters — email channel sender се регистрира само ако `IEmailSender` вече е там).
- **Persistence:** `NotificationDbContext` живее в Braikov.Notifications.MySql; mapped към същата `accountant` MySQL DB със собствена `__BraikovNotificationsMigrationsHistory` таблица. Тристранични таблици: `notifications`, `notificationdeliveries`, `usernotificationpreferences`.

Auth email routing към notification pipeline-а сега е активирано чрез `Braikov.Identity.Notifications` (`.UseNotificationDispatcher()` на IdentityBuilder). Всеки auth email създава `notifications` + `notificationdeliveries` row — същия audit / retry treatment като feature notifications.

## 11a. Identity (Braikov.Identity.*)

ASP.NET Core Identity foundation е изваден от Accountant в standalone repo `C:\Projects\Miro\Braikov\` и се consume-ва като NuGet пакети. Accountant.Web ползва:

- **`Braikov.Identity.Core`** — `AddBraikovIdentity<ApplicationUser, IdentityRole<int>>(config)` чете `Identity:` секцията на appsettings (cookie / password / lockout / signin / rate limit). `BaseAccountController<ApplicationUser, int>` се inherit-ва от `Accountant.Web.Areas.Identity.Controllers.AccountController` — тънка derived class с `CreateUser` factory override.
- **`Braikov.Identity.Notifications`** — `.UseNotificationDispatcher()` маршрутира auth email през `INotificationService`. Type definitions (auth.email_confirmation / password_reset / password_changed) идват от пакета.
- **`Braikov.Identity.Events.MySql`** — `AddBraikovIdentityEventsMySql(connectionString)` създава `AccountEventDbContext` + applies bundled migration. Audit log получава всеки login / register / confirm / reset / change-password / logout.
- **`Braikov.Identity.ShortCodes.MySql`** — `AddBraikovIdentityShortCodesMySql(config, connectionString)` създава `ShortCodeTokenDbContext` + applies bundled migration. Genera 6-цифрен код покрай дългия URL token в email-ите — мобилно-friendly UX.

**Тук остава host-specific (не в пакет):**

- `Accountant.Identity/Models/ApplicationUser.cs` — минимална, `: IdentityUser<int>`.
- `Accountant.DataAccess/AccountantDbContext.cs` — `: IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`, co-located business + Identity tables.
- `Accountant.Email/` — IEmailSender concrete impl (SmtpEmailSender + RazorEmailTemplateRenderer + bg-BG templates). Bridge `AccountantNotificationEmailSender` (Braikov.Notifications email channel → Accountant.Email.IEmailSender).
- `Accountant.Notifications/AccountantRecipientResolver` — int-keyed `ApplicationUser` lookup.
- `Accountant.Web/Areas/Identity/Views/` — 14 branded Razor views.
- `Accountant.Web/Areas/App/` — authenticated landing.

**Persistence:** 3 separate EF contexts, всички сочат към същата `accountant` MySQL DB със собствени history таблици: `__EFMigrationsHistory` (Accountant + Identity), `__BraikovNotificationsMigrationsHistory`, `__BraikovIdentityEventsMigrationsHistory`, `__BraikovIdentityShortCodesMigrationsHistory`.

## 12. Управление на тайните

- **Local dev:** `dotnet user-secrets` — vendor API keys, MySQL connection strings.
- **CI/CD:** GitHub Secrets (когато се добави CI).
- **Production:** Environment variables на хост сървъра.

## 13. Product MVP (task 0004)

Продуктовият surface е активен — Web host вече не е placeholder.

**Нови проекти:**

- `Accountant.Storage` (Class Library) — `IFileStore` (LocalFileStore impl, root `App_Data/uploads/yyyy/MM/{guid}{ext}`), `IThumbnailRenderer` + `ImageThumbnailRenderer` (ImageSharp 3.1.12) + `PdfThumbnailRenderer` (PDFtoImage 4.1.1 → page 0 → resize), `ThumbnailDispatcher`. DI: `AddAccountantStorage`.
- `Accountant.Jobs` (Class Library) — Hangfire.AspNetCore 1.8.23 + Hangfire.MySqlStorage 2.0.3 storage (`Hangfire_*` table prefix), `IExtractorFactory` (Claude/Codex/Gemini), `ExtractDocumentJob.RunAsync(int)` с `[AutomaticRetry(Attempts=3)]`, `AdminDashboardAuthorizationFilter`, `StuckJobRecoveryService` (IHostedService — re-enqueues Processing docs at startup, single-instance assumption). DI: `AddAccountantJobs`.

**Нови продуктови entities (всички в `AccountantDbContext` под `Accountant.DataAccess/Entities/Product/`):**

- `Tenant` — `Name`, `OwnerUserId?`, `CreatedAtUtc`. Един user → много tenants.
- `TenantMembership` — n:n `User × Tenant` с `TenantRole` enum (Owner / Member). Unique (UserId, TenantId).
- `Folder` — tenant-scoped дървовидна структура (`ParentFolderId` self-ref). Unique (TenantId, ParentFolderId, Name).
- `Document` — `(TenantId, FolderId?)`, `OriginalFileName`, `ContentType`, `ByteSize`, `StorageKey` (opaque IFileStore key), `ThumbnailKey?`, `DocumentStatus` enum (Uploaded → Queued → Processing → Extracted | Failed).
- `DocumentExtraction` — AI run (Vendor, ModelName, PromptVersion, LatencyMs, TokensIn/Out, EstimatedCostUsd, JsonResult longtext, FailureReason).
- `DocumentCorrection` — append-only user edits върху JSON-а. Latest correction wins.
- `ApplicationSetting` — key/value bag за admin-tweakable настройки (`Extraction.DefaultVendor` сега; останалото идва по нужда).

**Web surface (`Accountant.Web/Areas/`):**

- `App/` — клиентски surface. `WorkspaceController` (folder tree + drop zone + grid + polling), `TenantsController` (list/create/switch с активен tenant в `.Accountant.ActiveTenant` HttpOnly cookie), `FoldersController` (create/rename/delete JSON endpoints), `DocumentsController` (upload multipart, EnqueueExtraction, Thumbnail/File streams, Detail/Edit, Download JSON).
- `Administration/` — admin surface (`[Authorize(Roles="Admin")]`). `HomeController` (dashboard: 5 metric cards + 3 Chart.js charts), `DocumentsController` / `UsersController` / `TenantsController` (server-paginated lists), `SettingsController` (vendor picker), `ChartsController` (JSON endpoints за dashboard).
- `App/Middleware/ActiveTenantMiddleware` — след auth, преди controllers. Чете cookie, validate-ва membership, при липса създава default tenant `<email>'s firm`. Експозва `IActiveTenantAccessor.Current` като scoped.
- `Administration/AdminRoleBootstrapService` (IHostedService) — създава `Admin` role + promote-ва най-ниският UserId ако няма admin. Idempotent.

**Routing:**

- `/App` → `Workspace.Index` (area-specific default route в `Program.cs`).
- `/Administration/Hangfire` → CoreUI dashboard през `AdminDashboardAuthorizationFilter`.
- `/Public` → marketing landing, redirect-ва logged-in users към `/App`.

**Localization:**

- Auth strings → `Braikov.Identity.Core.Resources.SharedResource` (от пакета).
- Product strings → `Accountant.Web.Resources.ProductResource` (host-defined). `IStringLocalizer<ProductResource>` injected като `L` в _ViewImports на App и Administration. `ProductResource.bg.resx` seeded — `ProductResource.en-GB.resx` за EN.

## 14. Отворени въпроси (deferred)

- **Persistence модел за extractions:** MySQL entities (`SourceDocument`, `Extraction`, `GroundTruth`, `EvaluationRun`, `EvaluationDocument`) вече стоят в `AccountantDbContext`. Дали Web ще пише в тях директно или ще има service слой — решава се при първата feature.
- **Per-tenant model picker:** `Settings.DefaultVendor` сега е глобална. Per-tenant override е v1.1.
- **Multiple Hangfire instances:** `StuckJobRecoveryService` приема single-instance Web. При scaling нагоре → leader election.
- **JS localization:** `workspace.js` има status labels hard-coded BG. Превод чрез data-attributes на server-rendered template.
- **Full localization conversion:** `Documents/Detail` + `Edit` partials + Administration views са hard-coded BG (структурата позволява инкрементално мигриране).
