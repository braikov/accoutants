# Unified Extraction Contract — v2

This folder is the synthesis of three vendor-specific contracts (Claude / Codex / Gemini) into a single canonical format that all three implementations target.

**Status:** AUTHORITATIVE — finalized 2026-05-08 after three review rounds (Codex round 1, Codex round 2, Gemini).

## Files

| File | Purpose |
|---|---|
| [EXTRACTION_CONTRACT.md](EXTRACTION_CONTRACT.md) | **The authoritative spec.** Read this for the canonical shape every vendor must produce. |
| [DISAGREEMENTS.md](DISAGREEMENTS.md) | Historical record of the 11 open questions and how they were resolved. Useful for understanding *why* each decision was made. |
| [accountant.document.v2.schema.json](accountant.document.v2.schema.json) | Formal JSON Schema (Draft 2020-12). Use it to validate any vendor's output programmatically. |
| [example.result.json](example.result.json) | Fully populated example using the АВАНС invoice (extracted by Claude on 2026-05-08), reformatted into the unified shape. |

## Origin

Codex's contract was used as the structural base because:
- It already shipped a formal JSON Schema.
- It uses decimal-strings for money (avoids JSON float precision bugs).
- It already had `vat_breakdown[]`, `payments[]`, `fiscal{}`, and `evidence{}` blocks that Claude's and Gemini's contracts lacked.

### Initial additions to Codex v1

1. **`extraction.line_items[*].includes_vat: bool | null`** (Claude) — explicitly handles receipts where line amounts are gross prices.
2. **`extraction_warnings: string[]`** (Claude) — human-readable English sentences alongside Codex's coded `checks[]`. (Now lives in `model_assessment` per Codex's review — see below.)
3. **`extraction.document.tax_event_date`** (Claude / domain knowledge) — Bulgarian invoices have a separate "Дата на данъчно събитие" field, missing from both Codex and Gemini.
4. **`mol`** on the `party` schema — common Bulgarian invoice field (not strictly mandated by ЗДДС чл. 114, but widely used and expected by downstream systems).
5. **System prompt template** (Gemini's contribution) — to be added after question Q10 is resolved.

### Post-Gemini review (2026-05-08)

Gemini reviewed the contract from the angle of Bulgarian accounting (ЗДДС / НАП) and surfaced four legally-motivated additions, all applied:

- `extraction.vat_breakdown[].reason` — legal basis for 0% VAT (mandatory under ЗДДС).
- `extraction.document.exchange_rate` — required when document currency is not BGN.
- `extraction.document.notes` — verbatim "Забележка" / "Основание за сделката" free text.
- Clarification that the `fiscal` block applies to invoices paid in cash (not just receipts).

### Post-Codex review (2026-05-08)

Codex reviewed the v2 draft and surfaced nine corrections, all incorporated:

- **Auditable multi-document loss.** Added `source.detected_document_count` and `source.extracted_document_index` so the "extract primary only" choice doesn't silently lose documents from the benchmark.
- **Split deterministic vs model-specific signals.** New `model_assessment` block holds confidence + `extraction_warnings`. The `validation` block is now strictly deterministic (any validator with the same `extraction` produces the same `validation`).
- **Removed `validation.is_valid_json`.** A JSON file can't truthfully report its own validity from inside.
- **Consolidated check codes.** The `*_checksum` / `iban_mod97` codes with `status: fail` cover the "invalid" case; redundant `invalid_*` codes removed.
- **Rewrote line items reconciliation rule.** Per-component sums (net, vat, gross independently) instead of routing on `includes_vat` — handles mixed documents correctly.
- **Widened `needs_review` triggers.** Image quality issues, multi-document warnings, partial crops, etc. now flag for review even when confidence is high.
- **Made provider tokens/cost nullable.** Not every vendor reports them.
- **Softened МОЛ legal claim.** Common/important field, but not strictly mandated by ЗДДС чл. 114.
- **Wrote the JSON Schema.** Without it the three implementations would drift.

## Convergence complete

1. The spec here is authoritative.
2. The vendor-specific folders (`Claude_Extraction_Contract\`, `Codex_Extraction_Contract\`, `gemini_Extraction_JSON_Contract.md`) stay as historical reference.
3. Each implementation (Claude / Codex / Antigravity) now updates its code to produce v2 output. The Anthropic implementation under `..\Claude\src\` will be updated next.
4. A diff script reads the three `result.json` files per image (ignoring the `provider` and `model_assessment` blocks) and produces a fair field-level accuracy report.
