# 0004 — Product MVP: Workspace + Extraction + Admin Dashboard

**Status:** ⏳ Дизайн only
**Owner:** Claude agent (with user)
**Depends on:** Task 0002 (Identity + Email) ✅, Task 0003 (Braikov.Identity.*) ✅, scaffolded `Areas/Administration` ✅.

## Goal

First end-user-usable version of Accountant. Accounting firms register, manage a folder tree of uploads, drop PDF / image invoices into a workspace, watch them get extracted by AI, edit the resulting structured invoice, download JSON. Admin sees usage / cost / accuracy statistics.

Goal of v1: счетоводна фирма може да поеме реален обем работа през сайта (без external integration с техния счетоводен софтуер — това е v3).

## Decisions captured

| Question | Decision |
|---|---|
| Multi-tenant model | One user → many tenants (n:n). User works in ONE active tenant at a time, switches via dropdown в header. Multi-user-per-tenant from day 1. |
| Folder tree | User-created, free-form. New folder + rename + delete supported. |
| Model selection | Admin chooses (app-level default for v1; per-tenant override deferred). End-user has no model picker. |
| Background jobs | Hangfire (already used pattern; MySQL storage). |
| Storage | Local FS under `App_Data/uploads/`, behind `IFileStore` abstraction so S3 / other swappable later. |
| Invoice editor | Form-based (option (a)). ONE form for all documents — sections: продавач / купувач / list of stoki / плащане. No visual-replica editor. |
| Languages | BG only for MVP. Resource-file structure (`SharedResource.bg.resx`) ready so EN can be added without rewrites. |
| Status updates | Polling (every 2-3s while user is on workspace), not SignalR. |
| Re-extraction | Not in MVP. One extraction per document. Re-run with different model is v1.1 feature. |

## Non-goals (NOT in MVP)

- Export to accounting software formats (Microinvest, Ажур) — v3.
- Subscription / billing / quotas — v4.
- Re-extraction with different model.
- Visual-replica invoice editor.
- Per-tenant model override (Admin sets global default; v2).
- Vendor management UI (toggle vendors on/off, prompt version pinning) — v2.
- Continuous-evaluation dashboard (regression of accuracy vs ground truth) — v2.
- Notifications (browser/email) on extraction complete — v2 if requested.
- Multi-page PDF handling beyond first-page extraction — v1.1 if needed.

## Domain model (new entities)

```
Tenant
   ├── Id, Name, CreatedAtUtc, OwnerUserId
   └── HasMany TenantMemberships, Folders, Documents

TenantMembership   (M:N: User × Tenant)
   ├── UserId, TenantId, Role (Owner / Member)
   └── JoinedAtUtc

Folder              (per-tenant free-form tree)
   ├── Id, TenantId, ParentFolderId? (null = root), Name, CreatedAtUtc, CreatedByUserId
   └── HasMany Documents, Children Folders

Document            (one uploaded file)
   ├── Id, TenantId, FolderId, UploadedByUserId
   ├── OriginalFileName, ContentType, ByteSize
   ├── StorageKey (opaque ref consumed by IFileStore)
   ├── ThumbnailKey? (generated post-upload)
   ├── Status (Uploaded / Queued / Processing / Extracted / Failed)
   ├── CreatedAtUtc, ProcessedAtUtc?
   └── HasOne DocumentExtraction, HasMany DocumentCorrections

DocumentExtraction  (the AI run — one per Document for MVP)
   ├── Id, DocumentId
   ├── Vendor (Claude / Codex / Gemini)
   ├── ModelName, PromptVersion
   ├── StartedAtUtc, CompletedAtUtc, LatencyMs
   ├── TokensIn, TokensOut, EstimatedCostUsd
   ├── Status (Success / Failed)
   ├── FailureReason?
   └── JsonResult (text, v2 schema shape)

DocumentCorrection  (user-edited version)
   ├── Id, DocumentId, CorrectedByUserId
   ├── EditedAtUtc
   └── CorrectedJson (text, v2 schema shape)
```

**Notes:**
- Existing research entities (`SourceDocument`, `Extraction`, `GroundTruth`, `EvaluationRun`, `EvaluationDocument`) stay as-is. Не пипаме research side.
- `Document` живее в product world; не споделя таблица с `SourceDocument` за audit clarity.
- `DocumentExtraction.JsonResult` се персистира като `LONGTEXT` (MySQL). За queries по конкретно поле — JSON_EXTRACT functions or materialize в отделни колони (v2).
- `Correction` е append-only. Latest correction wins за download. История = audit.

## Pages (client surface: `Areas/App/`)

### `/App/` — Workspace (the main page)

**Layout:**
- Header: tenant switcher dropdown (if user has >1 tenant), user menu (logout, profile).
- Left rail: folder tree. Root = current tenant's docs. "+" buttons за нов folder. Right-click → rename / delete. ~280px wide.
- Center: thumbnails grid за selected folder's documents. Each thumbnail shows status badge.
- Above thumbnails: drag-drop zone "Drop files here, or click to browse". Multiple files accepted (PDF + image).
- Bottom of grid: "Process selected" button (gated — visible only if there are uploaded-but-not-yet-extracted documents).
- Toast notifications on errors.

**Behavior:**
- Drop files → upload via streaming multipart → on each successful upload, a new Document row + thumbnail render (status=Uploaded).
- Click "Process selected" → enqueue Hangfire jobs for each Uploaded document → status flips to Queued.
- Polling endpoint `/App/Documents/Statuses?folderId=N` every 2.5s returns status map for visible documents. UI updates badges.
- Click thumbnail → navigate to `/App/Documents/{id}`.

### `/App/Documents/{id}` — Document detail

**Layout:**
- Left pane: original viewer. For image: `<img>` with zoom. For PDF: pdf.js inline viewer (multi-page). ~50% width.
- Right pane: form-based invoice editor + actions.

**Right pane structure:**
```
[Header] Document name, status, last extracted at
[Actions row] Download JSON | Edit | (future: Re-extract)
[Section: Source]
   Document number, issue date, type (фактура/касов бон/...)
[Section: Provider (Продавач)]
   Name, EIK, IBAN, BIC, address
[Section: Recipient (Купувач)]
   Name, EIK, address
[Section: Lines (Стоки/услуги)]
   Editable table: description / qty / unit_price / vat_rate / line_total
[Section: Totals]
   Subtotal, VAT, Total
[Section: Payment]
   Method, status, due date
```

Each field shows the extracted value. `Edit` toggles inputs to editable. `Save` posts the entire model back; server creates a new `DocumentCorrection` row.

### `/App/Documents/{id}/Edit` (or modal/inline)

Same form as above but inputs editable.

### `/App/Profile`

Email / password change (uses existing `Areas/Identity` ChangePassword). Display name. Default tenant (auto-selected on login). Avatar upload (post-MVP polish).

### Tenant switcher

Lives in `_AppLayout` header next to user menu. Dropdown of tenants user belongs to. Selecting one POSTs to `/App/Tenants/SetActive` → sets active tenant in cookie/session → redirect to `/App/`.

If user has only 1 tenant, hide the switcher.

## Pages (admin surface: `Areas/Administration/`)

### `/Administration/` — Dashboard

Metric cards (top row):
- Total documents this month
- Total tokens (in + out) this month
- Estimated cost this month (USD)
- Active users (last 7 days)
- Failed extractions this month (alert if > threshold)

Charts:
- Documents processed per day (last 30 days) — line
- Vendor distribution (per-vendor count) — pie
- Average extraction latency (last 30 days) — gauge or bar

### `/Administration/Documents`

Server-side paginated table (DataTables-style). Columns: thumbnail | uploaded by | tenant | folder path | filename | status | extracted at | vendor | latency | cost. Filters: tenant, user, vendor, status, date range.

Click row → admin view of `/App/Documents/{id}` but with all extraction history visible (corrections trail).

### `/Administration/Users`

Per-user list: email | tenant membership count | first registered | last active | total docs | total tokens | total cost. Click → user detail with usage history.

### `/Administration/Tenants`

Per-tenant list: name | owner email | members count | total docs | total cost. Click → tenant detail (members list + recent documents + per-tenant stats).

### `/Administration/Settings`

App-level defaults: default vendor for extraction. Vendor on/off toggles. *(MVP min: 1 dropdown for default vendor; vendor toggles is v2.)*

## Phases

### Phase A — Domain entities + migration ✅

A1. ✅ Entities in `Accountant.DataAccess/Entities/Product/`:
   - [Tenant](../../Source/Accountant.DataAccess/Entities/Product/Tenant.cs) — Id, Name, OwnerUserId?, CreatedAtUtc
   - [TenantMembership](../../Source/Accountant.DataAccess/Entities/Product/TenantMembership.cs) — n:n User × Tenant; Role enum (Owner / Member)
   - [Folder](../../Source/Accountant.DataAccess/Entities/Product/Folder.cs) — tenant-scoped self-referencing tree
   - [Document](../../Source/Accountant.DataAccess/Entities/Product/Document.cs) — upload + Status enum (Uploaded / Queued / Processing / Extracted / Failed) + StorageKey
   - [DocumentExtraction](../../Source/Accountant.DataAccess/Entities/Product/DocumentExtraction.cs) — Vendor / ModelName / PromptVersion / latency / tokens / cost / Status / JsonResult (longtext)
   - [DocumentCorrection](../../Source/Accountant.DataAccess/Entities/Product/DocumentCorrection.cs) — append-only user edits
A2. ✅ DbSets added to [AccountantDbContext](../../Source/Accountant.DataAccess/AccountantDbContext.cs). XML doc updated to reflect three domains (research + product + identity).
A3. ✅ `IEntityTypeConfiguration<T>` for each in `Accountant.DataAccess/Configurations/Product/`. Indexes: `(TenantId, FolderId, CreatedAtUtc)` on documents for workspace browse; `Status` on documents for queue queries; `(Vendor, StartedAtUtc)` on extractions for stats; `(DocumentId, EditedAtUtc)` on corrections for "latest correction" lookup; `(UserId, TenantId)` unique on memberships; `(TenantId, ParentFolderId, Name)` unique on folders.
A4. ✅ Migration `20260511144240_AddProductSchema` generated via `dotnet ef migrations add` against `Accountant.MySql` design-time factory.
A5. ✅ Applied to `dev_accountant`. 6 new tables present: `tenants`, `tenant_memberships`, `folders`, `documents`, `document_extractions`, `document_corrections`. **Prod apply deferred** — will batch with later phases via idempotent script.

**Deliverable: ✅** schema in dev DB. Empty tables. DataAccess builds clean. Existing research / Identity / Notifications tables undisturbed.

### Phase B — `Accountant.Storage` (IFileStore + thumbnails) ✅

B1. ✅ `IFileStore` interface in [Accountant.Storage/Abstractions/IFileStore.cs](../../Source/Accountant.Storage/Abstractions/IFileStore.cs) — `SaveAsync` / `OpenReadAsync` / `DeleteAsync` / `ExistsAsync`.
B2. ✅ [LocalFileStore](../../Source/Accountant.Storage/Local/LocalFileStore.cs) — root configurable (`Storage:Root`, default `App_Data/uploads`); keys `yyyy/MM/{guid}{ext}`; key validation rejects absolute / `..` traversal paths.
B3. ✅ DI via [AddAccountantStorage](../../Source/Accountant.Storage/StorageDependencyInjection.cs) registers `IFileStore` (singleton), both renderers, and `ThumbnailDispatcher`.
B4. ✅ [ImageThumbnailRenderer](../../Source/Accountant.Storage/Thumbnails/ImageThumbnailRenderer.cs) (SixLabors.ImageSharp 3.1.12) + [PdfThumbnailRenderer](../../Source/Accountant.Storage/Thumbnails/PdfThumbnailRenderer.cs) (PDFtoImage 4.1.1 → page 0 → resize via ImageSharp) + [ThumbnailDispatcher](../../Source/Accountant.Storage/Thumbnails/ThumbnailDispatcher.cs) for content-type routing.

**Deliverable: ✅** Build clean. Unit-test in `Accountant.Tests` deferred to Phase L smoke pass.

### Phase C — Hangfire wiring ✅

C1. ✅ New project `Accountant.Jobs` with `Hangfire.AspNetCore 1.8.23` + `Hangfire.MySqlStorage 2.0.3` (Newtonsoft.Json 13.0.3 + System.Data.SqlClient 4.9.0 directly pinned to clear NU1902/NU1903 vulnerabilities from Hangfire transitives).
C2. ✅ MySQL storage uses the existing `accountant` connection string with `Hangfire_*` table prefix; `PrepareSchemaIfNecessary=true` lets Hangfire create its own tables on first run.
C3. ✅ Dashboard mounted at `/Administration/Hangfire` (configurable via `Hangfire:DashboardPath`). [AdminDashboardAuthorizationFilter](../../Source/Accountant.Jobs/AdminDashboardAuthorizationFilter.cs) rejects anonymous + non-Admin users.
C4. ✅ [ExtractDocumentJob.RunAsync(int documentId, CancellationToken)](../../Source/Accountant.Jobs/ExtractDocumentJob.cs) loads the document, copies the blob to a temp file (extractor API takes paths), picks vendor via [IExtractorFactory](../../Source/Accountant.Jobs/Extraction/IExtractorFactory.cs) (reads `Extraction:DefaultVendor`), persists `DocumentExtraction`, and flips status to Extracted/Failed.
C5. ✅ `[AutomaticRetry(Attempts=3, DelaysInSeconds={30,120,300})]` on the job class. Each attempt re-throws; failure row is written on every attempt (last write wins).

**Deliverable: ✅** Web builds with Hangfire wired. Real-job smoke deferred to Phase E when upload + enqueue paths exist.

**Follow-up (Phase J):** vendor selection currently reads `Extraction:DefaultVendor` from configuration. Replace with DB-backed `ApplicationSettings` when admin model picker lands.

### Phase D — Tenant + membership + active-tenant switcher ✅

D1. ✅ Auto-bootstrap moved from `BaseAccountController.Register` override into [ActiveTenantMiddleware](../../Source/Accountant.Web/Areas/App/Middleware/ActiveTenantMiddleware.cs): on the first authenticated request after registration, [TenantService.EnsureDefaultTenantAsync](../../Source/Accountant.Web/Areas/App/Services/TenantService.cs) creates the "<email-local-part>'s firm" tenant + Owner membership. Idempotent — no-op when a membership already exists. Avoids modifying the Braikov Identity package + self-heals if a user somehow ends up tenantless.
D2. ✅ [TenantService](../../Source/Accountant.Web/Areas/App/Services/TenantService.cs) — `ListMembershipsAsync`, `IsMemberAsync`, `CreateTenantAsync`, `EnsureDefaultTenantAsync`.
D3. ✅ Active tenant stored in `.Accountant.ActiveTenant` cookie ([ActiveTenantCookie](../../Source/Accountant.Web/Areas/App/Services/ActiveTenantCookie.cs); HttpOnly, Secure, SameSite=Lax, 30-day expiry). Middleware reads cookie, validates membership, falls back to first membership when invalid, and re-writes the cookie if the resolved id differs. Exposes the result via scoped `IActiveTenantAccessor`.
D4. ✅ [_AppLayout](../../Source/Accountant.Web/Areas/App/Views/Shared/_AppLayout.cshtml) header: `<details>`-based switcher dropdown with all memberships + "+ Нова фирма" link. Styles in [app.css](../../Source/Accountant.Web/wwwroot/app/css/app.css).
D5. ✅ [TenantsController](../../Source/Accountant.Web/Areas/App/Controllers/TenantsController.cs) — `Index` (list), `Create` (GET+POST creates + auto-activates new tenant), `Switch` (POST sets cookie and redirects to `returnUrl` when local).
D6. **Pending Phase E onwards** — convention: all workspace queries must filter by `IActiveTenantAccessor.Current.Id`. Document upload, folder tree, document listings, and the extract job will enforce this.

**Deliverable: ✅** TenantService + middleware + switcher + Tenants controller all build clean. End-to-end smoke deferred to Phase E (needs upload flow to exercise the tenant scoping).

### Phase E — Workspace UI ✅ (Rename/Delete UI follow-up)

E1. ✅ [WorkspaceController](../../Source/Accountant.Web/Areas/App/Controllers/WorkspaceController.cs) — `Index(int? folderId)` renders the main page; `Statuses(?ids=...)` returns JSON `[{id, status, hasThumbnail}]` for polling. Replaces the placeholder `HomeController`; new area-default route in Program.cs maps `/App` → `Workspace.Index`.
E2. ✅ [WorkspaceViewModel](../../Source/Accountant.Web/Areas/App/ViewModels/WorkspaceViewModel.cs) (tenant, FolderNode tree, current folder, breadcrumbs, document rows). [WorkspaceService](../../Source/Accountant.Web/Areas/App/Services/WorkspaceService.cs) loads tree + documents in one round-trip per request, scoped to active tenant.
E3. ✅ 3-column layout in [Workspace/Index.cshtml](../../Source/Accountant.Web/Areas/App/Views/Workspace/Index.cshtml) — sidebar (folder tree) / main (header + drop zone + grid).
E4. ✅ Server-rendered recursive nested `<ul>` via [_FolderChildren.cshtml](../../Source/Accountant.Web/Areas/App/Views/Workspace/_FolderChildren.cshtml). Expand/collapse is implicit (always shown; small datasets in MVP). No client lib added.
E5. ✅ "+ Нова" button + `<dialog>` modal → POST `Folders/Create`. [FoldersController](../../Source/Accountant.Web/Areas/App/Controllers/FoldersController.cs) also exposes `Rename` + `Delete` endpoints (used in follow-up). **Follow-up:** wire right-click context menu for Rename/Delete (controllers ready; UI deferred — see `docs/followups.md`).
E6. ✅ Drop zone with `dragenter/dragover/drop`, XHR multipart upload, per-file `<progress>` and status indicator. Accepts PDF + common image types; client filters by MIME; server caps file at 25 MB / form at 120 MB.
E7. ✅ CSS grid via [_DocumentCard.cshtml](../../Source/Accountant.Web/Areas/App/Views/Workspace/_DocumentCard.cshtml) — thumbnail / filename / status badge / checkbox.
E8. ✅ "Обработи избраните" button enables when ≥1 Uploaded/Failed doc selected → POST `Documents/EnqueueExtraction` enqueues a Hangfire job per id via `IBackgroundJobClient`. Status flips to Queued in same transaction.
E9. ✅ `workspace.js` polls every 2.5s while `document.visibilityState === 'visible'`. Only sends ids in `[Queued, Processing]`. Updates badge + swaps placeholder → thumbnail when the extractor produces one.

Synchronous thumbnail generation during upload (image via ImageSharp; PDF via PDFtoImage first page). Failure is non-fatal — placeholder shows instead, document still usable.

**Deliverable: ✅** Build clean; awaiting manual smoke (upload PDF/image → Hangfire dashboard → status transitions). Anthropic API key needs to be set in user-secrets (`Claude:ApiKey`) for the extractor to actually run.

### Phase F — Document detail (read-only) ✅

F1. ✅ `DocumentsController.Detail(int id)` loads Document + Extraction + latest Correction (one round-trip). Merges into [DocumentDetailViewModel](../../Source/Accountant.Web/Areas/App/ViewModels/DocumentDetailViewModel.cs) with `IsCorrected` flag when a correction overrides the extraction.
F2. ✅ [Detail.cshtml](../../Source/Accountant.Web/Areas/App/Views/Documents/Detail.cshtml) — sticky left-pane original viewer, right-pane sectioned read-only display.
F3. ✅ Original viewer: `<img>` for images, `<iframe>` to the `File` action for PDFs (browser native viewer; pdf.js skipped for MVP — works for Chrome/Edge/Firefox/Safari out of the box).
F4. ✅ `File(int id)` already existed from Phase E — streams via `IFileStore.OpenReadAsync` with original `Content-Type`.
F5. ✅ Sections: документ / доставчик / получател / стоки и услуги (table) / суми + ДДС breakdown / плащане / fiscal / обработка meta (vendor / model / latency / tokens / cost).
F6. ✅ Action buttons: "Сваляне JSON" (when data exists), "Редактирай" (Phase G placeholder — already wired to `Edit` action), "Назад към работния плот" breadcrumb.
F7. ✅ `Download(int id)` returns `application/json` with the correction-or-extraction body as `<filename>.json`.

Status-specific bodies: `Failed` shows the failure reason in a red callout; `Uploaded`/`Queued`/`Processing` shows a pending notice instead of the form.

**Deliverable: ✅** Build clean. Thumbnail click → `/App/Documents/Detail/{id}` → original + extracted data side-by-side, with download. Edit button is a stub until Phase G.

### Phase G — Edit form + Save ✅

G1. ✅ [EditDocumentViewModel](../../Source/Accountant.Web/Areas/App/ViewModels/EditDocumentViewModel.cs) mirrors the v2 schema. DataAnnotations: ISO date regex, ISO 4217 3-letter currency, EIK (9 or 13 digits), VAT (`BG\d{9,10}`), IBAN (`^[A-Z]{2}\d{2}[A-Z0-9]{1,30}$`), BIC, and a custom `[Amount]` regex (`^-?\d+(\.\d{1,4})?$`) for the schema's "number-as-string" fields.
G2. ✅ `Edit` GET loads the current authoritative `ExtractionResult` (correction-if-exists else extraction) via `LoadCurrentDataAsync` and projects it into the view model.
G3. ✅ `Edit` POST validates, reloads the original `ExtractionResult`, replaces only the `Extraction` subdoc (preserving `Source` / `Validation` / `Provider` / `ModelAssessment` / `Evidence`), serializes the result, and appends a new `DocumentCorrection` row. Empty form fields are `Nullify`-ed back to `null` so the JSON doesn't grow noise. Empty line items are dropped on save.
G4. ✅ [Edit.cshtml](../../Source/Accountant.Web/Areas/App/Views/Documents/Edit.cshtml) mirrors the detail layout — sticky viewer left, editable form right. Line items table with JS-driven add/remove ([edit-document.js](../../Source/Accountant.Web/wwwroot/app/js/edit-document.js)) re-indexes form names so the model binder still sees `Lines[0]`, `Lines[1]`, … . `_PartyForm` reused for supplier/customer with `HtmlFieldPrefix` set from `ViewData["Prefix"]`. Totals auto-recompute is deferred to a polish pass — Phase G saves whatever the user typed; server doesn't recompute either (corrections are user-authoritative).
G5. ✅ `<div asp-validation-summary="ModelOnly">` at the top + `<span asp-validation-for=...>` inline. jQuery-validate + jquery.validate.unobtrusive included so the regex hints fire client-side too.

**Deliverable: ✅** Build clean. Edit form renders, saves a new `DocumentCorrection` row, Detail page picks the correction up automatically via `IsCorrected` precedence, Download serves the corrected JSON.

### Phase H — Admin dashboard ⏳ Чакаща

H1. `Administration/Home/Index` (replaces template's placeholder) renders 5 metric cards + 3 charts.
H2. `AdminDashboardService` with SQL queries:
   - DocumentsThisMonth count
   - TotalTokens (sum of TokensIn + TokensOut) over current month
   - TotalCost (sum of EstimatedCostUsd) over current month
   - ActiveUsersLast7Days (distinct UserId in DocumentExtractions joined via Documents)
   - FailedExtractionsThisMonth count
H3. Chart data endpoints (JSON): `/Administration/Charts/DocumentsPerDay?days=30`, `/Administration/Charts/VendorDistribution`, `/Administration/Charts/AvgLatency`.
H4. View: Chart.js (CDN) reads the JSON endpoints.

**Deliverable:** admin sees real-time metrics on landing.

### Phase I — Admin Documents / Users / Tenants tables ⏳ Чакаща

I1. `Administration/Documents` — server-side paginated DataTable. Filters as listed in Pages section.
I2. `Administration/Users` — list + per-user detail.
I3. `Administration/Tenants` — list + per-tenant detail.
I4. All views use the scaffolded `_AdminLayout` (from braikov-admin template).

**Deliverable:** admin can drill into any document / user / tenant.

### Phase J — Admin Settings + model default ⏳ Чакаща

J1. `ApplicationSettings` entity OR `Identity:Vendor:Default` in `appsettings.json`. Decision: app settings DB row keyed by setting name. Allows admin to edit through UI without redeploy.
J2. `Administration/Settings/Index` view: dropdown of available vendors, `Save` button.
J3. `ExtractDocumentJob` reads the setting at run time.

**Deliverable:** admin can pick `Claude` vs `Codex` vs `Gemini` from a UI dropdown; new extractions use the chosen vendor.

### Phase K — Localization scaffolding ⏳ Чакаща

K1. Add `Resources/SharedResource.bg.resx` to the Web project — auth strings already come from Braikov.Identity.Core; this is for product-specific labels (folder tree, drop zone, invoice section headers, admin metrics).
K2. `Resources/SharedResource.cs` marker class.
K3. Wire `AddDataAnnotationsLocalization` with marker (already done for Identity — extend to cover product VMs).
K4. Every user-facing string in product views goes through `@Localizer["Workspace.DropZone.Hint"]` etc. Initial pass: BG only. Adding EN later = new `.en-GB.resx` file.

**Deliverable:** every user-visible string in the product surface is resource-keyed. Switching to EN is a matter of adding one more `.resx` file.

### Phase L — Polish + smoke ⏳ Чакаща

L1. End-to-end manual smoke: register → login → upload 3 docs (different vendors potentially) → process → review each → edit one → download.
L2. Admin smoke: login as admin → dashboard renders → tables drilldown works.
L3. Error paths: oversized file rejected, unsupported file type rejected, extraction failure shows nicely, missing model setting fallback.
L4. Performance smoke: 50 docs in one folder doesn't break the workspace.
L5. Update `docs/Project_Structure.md` with the new entities + the surface map.
L6. Update `useful_commands.md` with migrations + Hangfire dashboard URL + admin role bootstrap recipe.

**Deliverable:** v1 MVP shippable.

## Open implementation questions (resolve as phases hit them)

- **Tenant cookie vs claim:** active-tenant в cookie (light) или в JWT claim (heavy, refresh нужно). За cookie auth → cookie е простият избор. (Phase D.)
- **PDF rendering library:** PDFtoImage (uses pdfium) vs PuppeteerSharp (heavy, needs Chrome) vs Docnet (lightweight, older). Decision in Phase B.
- **Hangfire MySQL package:** `Hangfire.MySqlStorage` is community; verify EF Core 9 / Pomelo compatibility before Phase C.
- **Cost estimation source:** vendor extractors already produce token counts. Mapping tokens → USD requires a price table (per-model rates). Hardcode initial rates in `appsettings.json` (`Vendors:Claude:PricePerInputToken` etc.), refine later.

## Risks

- **Hangfire + Pomelo + EF Core 9:** community package may lag .NET 10. If incompatible, fall back to `BackgroundService` + simple in-memory queue + retry (loses dashboard + persistence; ok for MVP if Hangfire blocks).
- **Folder tree UX in pure HTML:** without jstree-style library, expand/collapse + drag-drop folder reorganization could be janky. Acceptable for MVP — no reorganization in v1; only create/rename/delete. Drag-to-move folders is v1.1.
- **Polling load:** with 2.5s interval and many active users, DB query volume grows linearly. Mitigate: cache statuses in IMemoryCache with 1s TTL keyed by tenant+folder.
- **Multi-tenant security:** every query MUST filter by ActiveTenantId. Easy to miss in admin list views. Mitigate: introduce a `TenantScopedDbContext` query filter pattern (EF Core's `HasQueryFilter`).
- **Storage abstraction "leakage":** if Hangfire job needs to read the file, it needs `IFileStore` injected. Make sure DI lifetime is sensible (Scoped for storage; transient is fine if no state). Verify in Phase C.

## Status icons per phase

When working a phase, mark steps as done inline:

- ⏳ Чакаща
- 🔄 В процес
- ✅ Завършена

---

## Suggested execution order

```
A → B → C → D → E → F → G → H → I → J → K → L
```

Parallelizable splits if time pressure: B and C can go alongside A; H and I can go after E (don't need F/G to demo dashboard).
