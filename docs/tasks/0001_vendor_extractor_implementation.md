# 0001 — Vendor extractor implementation (Codex, Gemini)

**Status:** ✅ Завършена (Codex), ⏳ Чакаща (Gemini)
**Owner:** Codex agent (for `Accountant.Codex`), Gemini agent (for `Accountant.Gemini`)
**Reference implementation:** `Source/Accountant.Claude/` — already done by the Claude agent. Mirror its shape; do not invent a different one.

## Goal

Implement [`IAccountingDocumentExtractor`](../../Source/Accountant.Contracts/Extraction/IAccountingDocumentExtractor.cs) in `Accountant.Codex` (using OpenAI vision) and `Accountant.Gemini` (using Google Gemini vision) so they produce v2 `ExtractionResult` for the same images Claude can already extract.

When done, this CLI must work with all three vendors:

```powershell
dotnet run --project Accountant.Processors -- extract --vendor claude --dir ../docs/facturi --limit 1
dotnet run --project Accountant.Processors -- extract --vendor codex  --dir ../docs/facturi --limit 1
dotnet run --project Accountant.Processors -- extract --vendor gemini --dir ../docs/facturi --limit 1
```

Each vendor writes the same shape JSON to `Source/Accountant.Processors/runs/<timestamp>/<Vendor>_<stem>.json` and to `<image_dir>/<Vendor>_<stem>.json`. Diff scripts compare them.

## What is shared (do NOT reimplement)

Everything in [`Accountant.Contracts`](../../Source/Accountant.Contracts/) is shared and authoritative:

- **DTOs** (`ExtractionResult`, `ModelExtractionInput`, `Source`, `Extraction`, `Document`, `Party`, `Totals`, `VatBreakdownItem`, `Payment`, `LineItem`, `Fiscal`, `ModelAssessment`, `ConfidenceMap`, `EvidenceItem`, `Validation`, `Check`, `Provider`, `ImageQuality`) — mirror the v2 schema. Use as-is.
- **Enums** (`DocumentType`, `PaymentMethod`, `Readability`, `Engine`, `Pipeline`, `CheckStatus`) — already serialize to snake_case strings.
- **`ExtractionValidator.Validate(extraction, source, confidence)`** — deterministic R1-R13 + EIK/IBAN/BIC checksums + totals/VAT/line-item reconciliation. Call it once per extraction; do not duplicate the logic.
- **`ExtractionJson.Default` / `Compact`** — single source of truth for `JsonSerializerOptions`. Use `Default` for tool schema generation and pretty output, `Compact` for in-memory parsing.
- **`ModelInputSanitizer.Sanitize(modelInput)`** — call this immediately after deserializing the model's tool/function output, before passing to the validator. Models occasionally collapse "no data" sub-objects to literal `null` (e.g., `"fiscal": null` instead of the object with all-null fields), which violates the v2 contract's "every defined key always present" rule. The sanitizer coerces null nested objects (`fiscal`, `document`, `supplier`, `customer`, `totals`, `image_quality`, `confidence`) and null lists into the schema-required default shapes, uniformly across all vendors.
- **`IAccountingDocumentExtractor`** — the interface to implement.

If you find yourself wanting to add validation logic or schema changes, **stop**. Update the [Unified_Extraction_Contract](../../Unified_Extraction_Contract/) docs first, propose to the user, and only then mirror in `Accountant.Contracts`. The shared layer is shared territory — don't unilaterally edit it.

## What is vendor-specific (your work)

Per the Claude reference, each vendor project owns:

| File | Purpose |
|---|---|
| `<Vendor>Prompt.cs` | System prompt + `PromptVersion` constant. Domain rules (Bulgarian docs, R1-R13, etc.) are the same; vendor tweaks are allowed only to work around model quirks (e.g. response format coaxing). |
| `<Vendor>Extractor.cs` | `class <Vendor>Extractor : IAccountingDocumentExtractor`. Owns the SDK call, vision payload, structured-output / function-call wiring, response parsing into `ModelExtractionInput`, and assembly into the full `ExtractionResult`. |
| `<Vendor>ExtractorOptions.cs` (or inline) | Holds `ApiKey`, `Model`, cost rates per million input/output tokens. |
| `ImageLoader.cs` (optional) | If your vendor has an image size limit similar to Anthropic's 5MB, port the resize logic from `Source/Accountant.Claude/ImageLoader.cs`. OpenAI's image limit is 20MB; Gemini's is up to 20MB inline / larger via Files API. Adjust thresholds per vendor docs. |

Mirror the Claude implementation's file split. Do not introduce a different layout.

## Reference implementation walkthrough

[`Source/Accountant.Claude/ClaudeExtractor.cs`](../../Source/Accountant.Claude/ClaudeExtractor.cs) is the template. The flow:

1. **Constructor** — receives `ClaudeExtractorOptions`, validates `ApiKey`, builds the SDK client, generates the tool schema once.
2. **`ExtractAsync(filePaths, ct)`** — sequential loop over paths. Caller controls parallelism.
3. **`ExtractOneAsync(path, ct)`**:
   1. Load + (if needed) downsize the image.
   2. Build a `MessageParameters` with: model, system prompt, user message containing image + text, tool definition forcing the model to call `extract_document`.
   3. Send to API, measure duration.
   4. Find the `tool_use` block in the response, deserialize `Input` (a `JsonNode`) into `ModelExtractionInput` using `ExtractionJson.Compact`.
   5. Build `Source` (filename, path, page_count=1, page_index=0, detected/extracted document indices from model output, image quality from model output).
   6. Run `ExtractionValidator.Validate(modelInput.Extraction, source, modelInput.ModelAssessment.Confidence)`.
   7. Build `Provider` with `Engine`, `Model`, `Pipeline.VisionDirect`, `OcrUsed=false`, `PromptVersion`, `CreatedAt` (ISO 8601 UTC), token counts, `CostEstimateUsd`.
   8. Compose and return `ExtractionResult`.
4. **`BuildToolFunction()`** — generates the JSON Schema from `typeof(ModelExtractionInput)` via `JsonSchemaExporter.GetJsonSchemaAsNode(ExtractionJson.Default, ...)`. Wrap as the SDK's tool/function spec.

## Vendor-specific implementation notes

### `Accountant.Codex` (OpenAI)

- **SDK:** install `OpenAI` (official) or `Azure.AI.OpenAI` if you need Azure-specific routing. Most likely just `OpenAI` from `nuget.org`.
- **Model:** for vision + structured output, use a recent vision-capable model (e.g. `gpt-5-mini`, `gpt-5`, or a `gpt-4o`-family fallback if needed). Set the default in `CodexExtractorOptions.Model`.
- **Structured output:** OpenAI supports JSON Schema via the `response_format` / `tools` mechanism. Two acceptable approaches:
  - **Function-calling style:** define one tool `extract_document` with `parameters` = the schema from `ModelExtractionInput`, then `tool_choice = { type: "function", function: { name: "extract_document" } }`. Mirror Claude's pattern.
  - **`response_format: json_schema`** with strict mode — the model returns the JSON directly without a tool call. Slightly cleaner but check that all schema constructs OpenAI's strict mode requires (no `oneOf` etc.) are satisfied. If `JsonSchemaExporter` output is too rich, use the function-calling path.
- **Image:** pass as a content part with `image_url` carrying a `data:` URL with base64. Or use the URL form for hosted images. For our local files, base64 is right.
- **Cost rates:** read OpenAI's published per-million-token rates for the chosen model and put them in `CodexExtractorOptions`.
- **API key source:** read from `Codex:ApiKey` in user-secrets, fall back to `OPENAI_API_KEY` env var. Mirror Claude's pattern.
- **Engine:** use `Engine.OpenAi` in `Provider`.

### `Accountant.Gemini` (Google)

- **SDK:** install `Mscc.GenerativeAI` (community NuGet, mature) **or** `Google.Cloud.AIPlatform.V1` (official Vertex AI client). The community one is easier for quick wiring against the public Gemini API; pick what fits your auth model. If you have a Google AI Studio API key, `Mscc.GenerativeAI` is simpler. If you go through Vertex / GCP service account, use the official client.
- **Model:** a vision-capable Gemini (e.g. `gemini-2.5-pro`, `gemini-2.5-flash`). Set in `GeminiExtractorOptions.Model`.
- **Structured output:** Gemini supports `responseSchema` for structured JSON. Pass the schema generated from `ModelExtractionInput`. **Caveat:** Gemini's JSON Schema dialect doesn't accept the full Draft 2020-12 surface — strip / inline `$defs` and remove unsupported keywords (`$id`, `$schema`, `additionalProperties` if enforced strictly). Add a small post-processor next to `BuildToolFunction()` if needed.
- **Image:** pass as `inlineData` with `mimeType` + base64 `data` part, or upload via Files API and reference (recommended for >5MB).
- **Cost rates:** Gemini per-million-token rates from Google docs; default model has free-tier limits, but production use is metered.
- **API key source:** `Gemini:ApiKey` in user-secrets, fall back to `GOOGLE_API_KEY` or `GEMINI_API_KEY`.
- **Engine:** use `Engine.Google` in `Provider`.
- **Known issue from research phase:** previous Python Gemini outputs sometimes wrapped the result in a JSON array `[{...}]` instead of returning the object. Make sure your C# implementation returns a single object — if your SDK gives you the response as an array, unwrap it. Don't write the wrapping bug back in.

## Wiring into Processors

Once your extractor compiles, wire it into [`ExtractCommand.cs`](../../Source/Accountant.Processors/ExtractCommand.cs):

1. Add a `using Accountant.Codex;` (or `Accountant.Gemini;`) at the top.
2. In `BuildExtractor(...)`, replace the `NotImplementedException` for your vendor:

```csharp
"codex" => BuildCodex(config),
```

3. Add a `BuildCodex(IConfiguration config)` method following the `BuildClaude` pattern: read API key, instantiate options, instantiate extractor, return `(extractor, "Codex")`. The string is the filename prefix used in output.

The Processors project already references `Accountant.Codex` and `Accountant.Gemini`, so no `.csproj` changes are needed.

## Prompt updates the Claude implementation has shipped

Claude's `ClaudePrompt.SystemPrompt` includes guidance that other vendors may want to mirror to get consistent extractions. Each block below was added to fix a specific cross-vendor disagreement seen in the corpus. Apply the equivalent text in your prompt or skip it — but if you skip it, expect the corresponding diff noise.

- **PRIORITY 0 — Retail fiscal receipt detection.** Bulgarian retail chains (BAUHAUS, Kaufland, Lidl, Billa, Метро, Технополис, dm, OMV, Shell, Lukoil, etc.) issue fiscal receipts that ALSO contain a "ФАКТУРА" sub-block. Models routinely misclassify these as invoices and end up with `includes_vat=false`, then compute `gross = printed × 1.20` (wrong by ~20%). Apply this rule BEFORE R11: if a known retail brand logo is present, OR there is a fiscal device number, OR a "ФИСКАЛЕН БОН" footer, OR a narrow thermal-strip format → `document_type = "receipt"`, every `line_items[*].includes_vat = true`, derive `vat_rate` from "Данъчна група" mapping or totals breakdown, derive `net = printed / (1 + rate/100)`. The "ФАКТУРА" text inside such documents is decorative — do not let it flip the classification.
- **R11 — `document_type` decision tree.** Title-first; payment method does not change type; fiscal section inside an A4 invoice does not change type.
- **R12 — `includes_vat` decision.** Fiscal receipts / cash register printouts → prices are VAT-inclusive (`includes_vat = true`), even when the document has a "ФАКТУРА" sub-heading. Standard A4 invoices → typically net (`false`). Verification: sum the printed line totals and compare to `totals.net` vs `totals.gross`. Trap: a receipt showing `1 бр × 7.80 = 7.80` is gross; do NOT compute `gross = 7.80 × 1.20`.
- **R12 — `vat_rate` inference per line.** Bulgarian invoices often omit a per-line VAT % column when the entire invoice is at a single rate. The absence of a column does NOT mean zero-rated. Resolution order:
  1. Per-line VAT column → use it. Only count a column as "VAT" if the header literally says "ДДС" or "VAT".
  2. Otherwise infer from the totals / `vat_breakdown` ("Начислен ДДС - 20%", "Данъчна група Б = 20%"). If a single rate covers the whole base, every line uses it.
  3. Multiple rates in totals → match by group code (А/Б/В) per line.
  4. Default to `0.00` ONLY when the totals explicitly show zero VAT or a legal basis cites zero-rated treatment.
  - **Anti-rule:** "T.O. %" / "ТО %" / "Отстъпка %" is the trade discount column → goes to `discount_pct`, never to `vat_rate`. Models routinely confuse this; the column being numeric and percent-shaped fools them.
  - **Self-check:** if `vat_rate = "0.00"` for a line whose `net` is in `totals.net` AND `totals.vat` is non-zero, that's a contradiction — almost certainly the whole invoice is at the rate shown in totals.
- **R13 — discount column (REVISED, default zero).** `discount_pct` is **always a decimal string with 2 decimals**, never `null`. Default `"0.00"` (no discount applied). Set `"20.00"` etc. only when a non-zero discount applies. When the line table prints "T.O. %" / "Отстъпка %" with a non-zero value: `unit_price` stays printed pre-discount; populate `discount_pct` with the percentage; derive `net = qty × unit_price × (1 - discount_pct/100)`. Never compute `unit_price = line_total / qty`. Older outputs may have `null` for non-discounted lines — `ResultSanitizer` coerces them to `"0.00"` so a `normalize` pass cleans up legacy data without re-extraction.
- **R10 — strict EIK length.** `eik` MUST be exactly 9 or 13 digits, never anything in between. If the digit count cannot be read clearly (stamp, fold, smudge, low contrast), return `null` and add a note to `model_assessment.extraction_warnings`. Do NOT return a partial EIK (8, 10, 11, or 12 digits) — the validator runs a Bulgarian checksum that requires the full length, so a truncated value silently breaks the record.
- **R10 — EIK ↔ VAT cross-derivation.** Bulgarian companies have `vat_number == "BG" + eik`. When one of the two fields is partially obscured but the other is clearly readable, derive the obscured one from the clear one rather than emitting a partial OCR read. Add a self-disclosure note to `model_assessment.extraction_warnings`. Applies ONLY for Bulgarian (`BG`) prefix — never cross-derive across non-BG country VATs.
- **No-null-objects rule.** For nested objects (`document`, `supplier`, `customer`, `totals`, `fiscal`, `image_quality`, `model_assessment`, `model_assessment.confidence`): always return the object with sub-fields populated. NEVER return the whole object as `null`. If your model still emits null nested objects despite the prompt, call `ModelInputSanitizer.Sanitize(...)` after deserialisation as a safety net.

## Targeted prompt fixes (2026-05-10, after first ground-truth evaluation pass)

After 23-document ground-truth evaluation revealed systematic per-vendor failures, Claude/Codex/Gemini prompts each got targeted anti-rules. If you re-derive a prompt or replace a vendor's model, mirror these or expect the same regressions.

- **`document.notes` verbatim-only (all 3 vendors).** Anti-rule: notes is the verbatim text of a labelled "Забележка" / "Основание за сделката" / "Основание" block. NEVER your own observations, OCR descriptions, meta-commentary, or restatements of payment method. Without such a labelled block → `null`. Anything else goes in `model_assessment.extraction_warnings`.
- **`address` completeness (all 3 vendors).** R4's split between `address` and `city` only applies when the document has a separately labelled city field. If city / postcode / region appear inline within the printed address block, KEEP them in `address`. Don't strip "София 1309, ул. Кукуш 1" down to "ул. Кукуш 1".
- **Codex — `document.tax_event_date` enforcement.** Always look explicitly for "Дата на данъчно събитие". When only one date is printed, set `tax_event_date = document.date` (they're the same when not separately stated). Same logic for `due_date`.
- **Claude — `payments` mandatory extraction.** When the document shows ANY bank info (IBAN, BIC, "Банка:", supplier IBAN below "по сметка"), MUST create a payment entry. Don't leave `payments = []` when bank details are visible. Also: don't duplicate single payments as `payments[1]`.
- **Claude — unit lowercase (R7 strengthened).** `"БР"` → `"бр"`. Bulgarian unit abbreviations are conventionally lowercase.
- **Gemini — `exchange_rate` BGN strict anti-rule.** When `currency == "BGN"`, `exchange_rate = null` ALWAYS, no exceptions. Even if "1.95583" or "1.00" appears anywhere on the document.
- **Gemini — `qr_code` raw value.** Extract the raw decoded value (hex hash). If QR encodes an НАП URL like `https://nraapp.nra.bg/fisc/qr?id=...`, extract ONLY the id (hex hash), not the URL. Never write `"Present"` or meta-descriptions.
- **Gemini — single-document safeguard.** Don't hallucinate a different document's fields into the output. If you see two different document numbers / "Сума за плащане" lines, set `detected_document_count = 2` and pick ONE.

## Output convention (already handled)

You don't need to write file output — `ExtractCommand` does it. As long as you return well-formed `ExtractionResult` objects, the runner writes:

- `runs/<timestamp>/<Vendor>_<stem>.json` (immutable run record)
- `<image_dir>/<Vendor>_<stem>.json` (latest, may be overwritten)
- `runs/<timestamp>/_run_summary.json` (totals)

The `<Vendor>` prefix comes from the second tuple element returned by `BuildExtractor`.

## Definition of done

- [ ] `Accountant.<Vendor>` builds with no warnings and no errors.
- [ ] `dotnet run --project Accountant.Processors -- extract --vendor <vendor> --dir ../docs/facturi --limit 1` runs to completion against a real API key without error.
- [ ] The single output JSON validates against [`Unified_Extraction_Contract/accountant.document.v2.schema.json`](../../Unified_Extraction_Contract/accountant.document.v2.schema.json) (manual check, e.g. paste into an online JSON Schema validator or wire a small test in `Accountant.Tests`).
- [ ] Field-by-field, the output should be diff-comparable to Claude's output for the same image. Disagreements are acceptable on actual values; structural / shape disagreements are not.
- [ ] No edits to `Accountant.Contracts` (DTOs, validators, JSON config) without a separate user-approved task.
- [ ] No production-grade hardening required — this is research mode. Keep it simple.

## Coordination

- The `Accountant.Contracts` project, `Accountant.Processors/ExtractCommand.cs`, and `Accountant.Web/*` are shared territory. Editing `ExtractCommand.cs` to wire your vendor is fine and expected; deeper changes to the runner or contract should go through the user.
- If you find a bug in the Claude implementation while reading it, flag it — don't silently "fix" code another agent owns.
- Per [CLAUDE.md / AGENTS.md](../../AGENTS.md): never commit without explicit user approval. Ask before staging.
