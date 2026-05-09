# Accountant.ReviewSite

Side-by-side review tool for vendor extraction outputs. Razor Pages web app.

Status: ✅ Active. Used during research phase to compare what each vendor extracted from the same image and to spot real disagreements vs format-only noise.

## Why is this a separate project?

Two reasons:

1. **Different concern.** ReviewSite is a research / debugging tool for the developer. `Accountant.Web` is the product UI for end users. Mixing them blurs that boundary and would force the product UI to depend on review-only logic.
2. **Disposable.** When the research phase ends and we move to production features, ReviewSite is expected to be retired (or kept only as an internal admin surface). Keeping it physically separate makes that easy — delete the project, drop it from `Accountant.slnx`, done.

If, later on, the diff/comparison logic in `Services/DocumentStore.cs` proves useful in `Accountant.Web` (e.g. an admin "compare vendor outputs" page), the right move at that point is to extract it into a shared library — not to merge whole projects. YAGNI until there is a real second consumer.

## What it does

- Lists images from `Review:ImageFolder` (configured in `appsettings.*.json`).
- For each image, looks for `<Vendor>_<stem>.json` files next to it (Codex, Claude, Gemini).
- Renders a side-by-side view of the extracted fields plus the source image.
- Wraps everything behind `BasicAuthMiddleware` (credentials in `appsettings.*.json`).

## Local run

```powershell
cd Source\Accountant.ReviewSite
dotnet run
```

Default ports: `https://localhost:7094` and `http://localhost:5141` (see `Properties/launchSettings.json`).

The `Review:ImageFolder` path defaults to the repo's `docs/facturi/`. Override via `appsettings.Development.json` if you keep test images elsewhere.

## Deployment

Publishes to `accountant.ima.bg` on the `vic.bg` IIS server via Web Deploy:

```powershell
dotnet publish -c Release -p:PublishProfile=Properties/PublishProfiles/Test.pubxml
```

The deploy account password lives in `Properties/PublishProfiles/Test.pubxml.user` (encrypted by Visual Studio, not in git for other users).

## Dependencies on the rest of the solution

- **`Accountant.Contracts`** — referenced for the v2 schema DTOs (`ExtractionResult`, etc.). Use these instead of raw `JsonNode` when adding new typed views.

That's it. ReviewSite does not depend on vendor extractor projects (`Accountant.Claude`, `Accountant.Codex`, `Accountant.Gemini`) — it just reads the JSON they produce.

## Do not touch (from inside this project)

- `Accountant.Contracts/` — shared territory. If a v2 schema field is missing for a view you want, propose a contract change to the user; do not edit the DTOs from here.
- `Unified_Extraction_Contract/` — schema and rules go through the user.
