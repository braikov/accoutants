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

- **R11 — `document_type` decision tree.** Title-first; payment method does not change type; fiscal section inside an A4 invoice does not change type.
- **R12 — `includes_vat` decision.** Fiscal receipts / cash register printouts → prices are VAT-inclusive (`includes_vat = true`), even when the document has a "ФАКТУРА" sub-heading. Standard A4 invoices → typically net (`false`). Verification: sum the printed line totals and compare to `totals.net` vs `totals.gross`. Trap: a receipt showing `1 бр × 7.80 = 7.80` is gross; do NOT compute `gross = 7.80 × 1.20`.
- **R12 — `vat_rate` inference per line.** Bulgarian invoices often omit a per-line VAT % column when the entire invoice is at a single rate. The absence of a column does NOT mean zero-rated. Resolution order:
  1. Per-line VAT column → use it.
  2. Otherwise infer from the totals / `vat_breakdown` ("Начислен ДДС - 20%", "Данъчна група Б = 20%"). If a single rate covers the whole base, every line uses it.
  3. Multiple rates in totals → match by group code (А/Б/В) per line.
  4. Default to `0.00` ONLY when the totals explicitly show zero VAT or a legal basis cites zero-rated treatment.
- **R13 — discount column.** When the line table prints "T.O. %" / "Отстъпка %", `unit_price` stays printed pre-discount; populate `discount_pct`; derive `net = qty × unit_price × (1 - discount_pct/100)`. Never compute `unit_price = line_total / qty`.
- **No-null-objects rule.** For nested objects (`document`, `supplier`, `customer`, `totals`, `fiscal`, `image_quality`, `model_assessment`, `model_assessment.confidence`): always return the object with sub-fields populated. NEVER return the whole object as `null`. If your model still emits null nested objects despite the prompt, call `ModelInputSanitizer.Sanitize(...)` after deserialisation as a safety net.

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
