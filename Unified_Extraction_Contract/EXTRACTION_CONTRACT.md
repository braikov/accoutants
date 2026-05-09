# Unified Extraction Contract — v2

**Status: AUTHORITATIVE.** Finalized 2026-05-08. All eleven open questions in [DISAGREEMENTS.md](DISAGREEMENTS.md) resolved with the recommended defaults. Three review rounds applied (Codex round 1, Codex round 2, Gemini).

This contract defines the canonical output format for image-to-JSON extraction of Bulgarian payment documents. All vendor implementations (Claude / Codex / Antigravity) target this exact shape so results are diff-able.

`schema_version`: **`accountant.document.v2`**

The formal JSON Schema is at [accountant.document.v2.schema.json](accountant.document.v2.schema.json).

## Output

One JSON file per source image. Naming: `<image_basename>.json`, written into `<vendor>/output/`.

## Top-Level Shape

```jsonc
{
  "schema_version": "accountant.document.v2",
  "source": { /* Where the image came from. */ },
  "extraction": { /* Vendor-neutral facts read from the document. */ },
  "validation": { /* Deterministic self-checks — same for all vendors given same extraction. */ },
  "model_assessment": { /* Model-specific opinion: confidence, uncertainty notes. */ },
  "evidence": { /* Per-field text snippets + confidence. */ },
  "provider": { /* Which vendor/model/run produced this. Diff scripts ignore this block. */ }
}
```

The six blocks have distinct concerns:

- **`source`** — image metadata (filename, page index, document count, image quality assessment).
- **`extraction`** — pure facts read from the document. **The diff-able surface across vendors.**
- **`validation`** — deterministic self-checks. Reproducible by any validator given the same `extraction`.
- **`model_assessment`** — model-specific opinions: per-section confidence and human-readable uncertainty notes. Two vendors with the same `extraction` may legitimately produce different `model_assessment`. **Diff scripts comparing accuracy across vendors should NOT use this block as a difference source.**
- **`evidence`** — per-field text snippets the model relied on. Used to arbitrate disagreements.
- **`provider`** — model id, prompt version, tokens, latency, cost. **Diff scripts ignore this block.**

The split between `validation` (deterministic) and `model_assessment` (model opinion) was added in v2 per Codex review. Mixing them in a single block contaminated benchmarking — confidence scores are inherently model-specific and shouldn't influence the comparison of validation pass/fail.

---

## 1. `source`

```jsonc
{
  "file_name": "20240213_190514.jpg",
  "file_path": "docs/facturi/20240213_190514.jpg",
  "page_count": 1,                          // null if unknown
  "page_index": 0,
  "detected_document_count": 1,             // total distinct documents the model saw on this image
  "extracted_document_index": 0,            // zero-based index of which one is in `extraction` (0 = primary)
  "image_quality": {
    "readability": "good",                  // excellent | good | fair | poor | unreadable
    "issues": []                            // free-form short tags, e.g. ["slightly_blurry", "shadow"]
  }
}
```

### Multi-document images

If the image contains multiple distinct documents (e.g. an invoice + a fiscal receipt side by side, or 4 receipts on one page):

1. Set `detected_document_count` to the actual count.
2. Extract the **primary** (largest / most prominent) document into `extraction`. Set `extracted_document_index = 0`.
3. The validator MUST add a `multiple_documents_detected` check with status `warning`, and `validation.needs_review` MUST be `true`.
4. The model SHOULD describe the unextracted documents in `model_assessment.extraction_warnings`.

This makes the loss auditable instead of silent. (Future: if multi-doc extraction becomes a priority, add a `documents: [...]` shape variant — see Q7.)

---

## 2. `extraction`

The vendor-neutral data block. Every defined field is always present; use `null` for unknown; lists default to `[]`.

```jsonc
{
  "document_type": "invoice",           // see enum below
  "language": "bg",
  "country": "BG",

  "document": {
    "number": "0300051648",             // verbatim, leading zeros preserved
    "date": "2023-12-11",               // ISO 8601 (issue date)
    "tax_event_date": "2023-12-11",     // Bulgarian "Дата на данъчно събитие"
    "due_date": "2023-12-19",
    "currency": "BGN",                  // ISO 4217
    "exchange_rate": null,              // decimal string, e.g. "1.95583". Required by ЗДДС when currency ≠ BGN; null otherwise.
    "place": null,                      // "Място на сделката" if printed
    "notes": null                       // verbatim "Забележка" / "Основание за сделката" free text from the document
  },

  "supplier": {
    "name": "АВАНС ТРЕЙД ООД",
    "eik": "201760264",                 // 9 or 13 digits, NO "BG" prefix
    "vat_number": "BG201760264",        // WITH "BG" prefix
    "address": "ул. Свищовска 1",       // verbatim line as printed
    "city": "Габрово",
    "country": "BG",
    "mol": "Николай Умников"            // common BG invoice field (not legally mandated by ЗДДС чл. 114, but widely included)
  },

  "customer": {
    "name": "ИНКО ТРЕЙД ЕООД",
    "eik": "206802564",
    "vat_number": "BG206802564",
    "address": "ул. Рощок 9",
    "city": "Варна",
    "country": "BG",
    "mol": null
  },

  "totals": {
    "net": "230.00",                    // decimal strings, never floats
    "vat": "46.00",
    "gross": "276.00",
    "discount": null,
    "rounding": null,
    "amount_due": "276.00"
  },

  "vat_breakdown": [                    // one entry per VAT rate present
    {
      "rate": "20.00",
      "net": "230.00",
      "vat": "46.00",
      "gross": "276.00",
      "reason": null                    // legal basis text, required by ЗДДС for 0% rates (e.g. "чл. 113, ал. 9 от ЗДДС", "Вътрешнообщностна доставка")
    }
  ],

  "payments": [                         // array — handles split payments and card-only cases
    {
      "method": "bank_transfer",        // cash | card | bank_transfer | mixed | unknown
      "amount": "276.00",
      "currency": "BGN",
      "iban": "BG62BPBI79291053720501", // no spaces, uppercase
      "bic": "BPBIBGSF",
      "bank_name": "Юробанк България АД"
    }
  ],

  "line_items": [
    {
      "description": "М.К.120 КАПАК КЕЪР СОФТх24",
      "quantity": "240.000",
      "unit": "бр.",
      "unit_price": "0.958",
      "discount_pct": null,             // printed per-line trade discount %, null when no discount column. See NORMALIZATION_RULES R13.
      "vat_rate": "20.00",
      "net": "230.00",
      "vat": "46.00",
      "gross": "276.00",
      "includes_vat": false             // true if printed line amount is gross (common on receipts). Storage of net/vat/gross is canonical (all three populated when derivable) — see NORMALIZATION_RULES R12.
    }
  ],

  "fiscal": {                           // populated whenever fiscal device data is visible — including invoices paid in cash with an attached/printed fiscal receipt. NOT restricted to document_type: receipt.
    "fiscal_receipt_number": null,
    "fiscal_device_number": null,
    "operator": null,
    "qr_code": null
  }
}
```

### Enum vocabularies

```
document_type:    invoice | receipt | proforma | protocol | credit_note | debit_note | unknown
                                         ^^^^^^^^ приемо-предавателен протокол
payment_method:   cash | card | bank_transfer | mixed | unknown
currency:         ISO 4217 code (BGN, EUR, USD, …)
readability:      excellent | good | fair | poor | unreadable
```

---

## 3. `validation` (deterministic)

```jsonc
{
  "needs_review": false,
  "checks": [
    { "code": "totals_match",          "status": "pass", "message": "230.00 + 46.00 = 276.00" },
    { "code": "supplier_eik_checksum", "status": "pass", "message": "" },
    { "code": "iban_mod97",            "status": "pass", "message": "BG62BPBI… mod-97 = 1" }
  ],
  "errors": [],                         // string check codes that hit `fail`
  "warnings": []                        // string check codes that hit `warning`
}
```

### `needs_review` triggers

`true` if ANY of these hold:

1. Any entry in `checks` has status `fail`.
2. Any of these warning codes appears in `checks`:
   - `multiple_documents_detected`
   - `document_partially_cropped`
   - `line_items_incomplete`
   - `ocr_text_conflict`
   - `image_quality_issue`
3. `model_assessment.confidence.overall < 0.7` (when the model emitted confidence).
4. Any per-section confidence in `model_assessment.confidence` is below 0.7.

(Per Codex review: previous draft's narrow rule "fail or low confidence only" was insufficient — warnings about multiple documents, crops, etc. genuinely warrant human eyes even when confidence is high.)

### Standard check codes

```
totals_match
vat_breakdown_match
supplier_eik_checksum
customer_eik_checksum
supplier_vat_format
customer_vat_format
iban_mod97
bic_format
date_format
due_date_after_issue
currency_iso
document_type_enum
missing_document_number
missing_document_date
missing_supplier_name
missing_supplier_eik
missing_customer_name
low_confidence_field
image_quality_issue
multiple_documents_detected
document_partially_cropped
line_items_incomplete
ocr_text_conflict
```

(Per Codex review: removed `invalid_supplier_eik`, `invalid_customer_eik`, `invalid_iban`. The `*_checksum` / `iban_mod97` codes with `status: fail` already cover the "invalid" case without duplicating the namespace.)

Allowed `status`: `pass | warning | fail | skipped`. Use `skipped` when the input field was `null` so the check couldn't run — this is **not** a failure.

### Reference validator algorithms (portable)

- **EIK checksum** — Bulgarian algorithm. 9-digit: weights `[1,2,3,4,5,6,7,8]` × first 8 digits, mod 11. If result is 10, retry with weights `[3,4,5,6,7,8,9,10]`; if again 10, checksum is 0. 13-digit: as above for first 9, then weights `[2,7,3,5]` for digits 9-12 mod 11 (retry with `[4,9,5,7]` if 10 → 0).
- **IBAN mod-97** — Move first 4 chars to end, replace each letter with `(ord - 55)`, parse as integer, must equal 1 mod 97.
- **BIC format** — Regex `^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$`.
- **`totals_match`** — `abs(net + vat - gross) ≤ 0.02`. `skipped` if any of the three is null.
- **`vat_breakdown_match`** — for each entry in `vat_breakdown`, `abs(net * rate / 100 - vat) ≤ 0.05` AND `abs(net + vat - gross) ≤ 0.02`. `skipped` if breakdown empty.
- **`line_items_sum`** (per-component, per Codex review) — for each component independently (`net`, `vat`, `gross`):
  - Sum the matching field across all `line_items` where it is non-null.
  - If any line is missing that field, emit `line_items_incomplete` warning and skip the component sum check.
  - Otherwise compare against `totals.<component>` with tolerance 0.05.
  - **Do NOT** route on `includes_vat` to "switch" between net/gross sums — `includes_vat` is an extraction hint to the model, not a validator routing trick. Per-component reconciliation handles mixed documents correctly because the model fills in net/vat/gross per line regardless of how the source printed them.

The Python reference implementation lives at `..\Claude\src\validate.py` (will be updated to v2 after disagreements are resolved). It's vendor-agnostic — feed it any vendor's `extraction` block and it produces the same `validation` shape.

---

## 4. `model_assessment` (NEW in v2)

Per Codex review: separated from `validation` so deterministic checks remain comparable across vendors regardless of model confidence.

```jsonc
{
  "confidence": {                       // 0.0–1.0, null if not estimated
    "overall": 0.96,
    "document": 0.98,
    "supplier": 0.95,
    "customer": 0.96,
    "totals": 0.99,
    "line_items": 0.94
  },

  "extraction_warnings": [              // human-readable English sentences
    "Recipient MOL field appears blank on the document.",
    "Staple/paperclip partially obscures top-right corner; no critical data affected."
  ]
}
```

This block is **inherently model-specific**. Two vendors processing the same image may legitimately produce different confidence values and different warnings — that's expected, not a bug. Compare it across vendors only as a *vendor characteristic* (which model is more cautious?), not as a *correctness signal*.

---

## 5. `evidence`

Per-field text snippets + confidence. Keys use **dot paths** matching extracted fields.

```jsonc
{
  "document.number":  { "text": "Фактура № 0300051648", "confidence": 0.97 },
  "document.date":    { "text": "11.12.2023",            "confidence": 0.94 },
  "supplier.name":    { "text": "АВАНС ТРЕЙД ООД",       "confidence": 0.91 },
  "supplier.eik":     { "text": "ЕИК 201760264",         "confidence": 0.99 },
  "totals.gross":     { "text": "Сума за плащане: 276.00 лв.", "confidence": 0.98 }
}
```

When two vendors disagree on `supplier.eik`, the `evidence.supplier.eik.text` from each lets you arbitrate without re-opening the image.

**Recommended minimum evidence keys** (not enforced by the JSON Schema — for fiscal receipts or low-quality images some keys may not be meaningful):

- `document.number`, `document.date`
- `supplier.name`, `supplier.eik`, `supplier.vat_number`
- `customer.name`, `customer.eik`
- `totals.net`, `totals.vat`, `totals.gross`

Additional dot-path keys are encouraged. Models should provide evidence for any field they extracted with non-trivial uncertainty.

---

## 6. `provider`

Per-vendor metadata. **Diff scripts ignore this entire block.**

```jsonc
{
  "engine": "anthropic",                // anthropic | openai | google
  "model": "claude-sonnet-4-6",
  "pipeline": "vision_direct",          // vision_direct | ocr_then_llm | hybrid
  "ocr_used": false,
  "prompt_version": "unified-v2-2026-05-08",
  "created_at": "2026-05-08T13:01:42Z", // ISO 8601 with timezone
  "duration_ms": 14270,
  "input_tokens": 5650,                 // null OK — not all vendors report tokens uniformly
  "output_tokens": 696,                 // null OK
  "cost_estimate_usd": "0.0274"         // null OK; if known, decimal string at vendor's published rate
}
```

---

## Conventions Summary

- **Snake_case** for all keys.
- **Every defined field always present** — use `null` for unknown, `[]` for empty lists. Never omit a defined key. (Exception: `provider.input_tokens` / `output_tokens` / `cost_estimate_usd` are nullable when the vendor doesn't expose them.)
- **Strings stripped** of leading/trailing whitespace.
- **Dates** ISO 8601 (`YYYY-MM-DD`). `null` if unreadable.
- **Money / quantities / rates** — decimal strings (`"230.00"`, `"0.958"`, `"20.00"`). Never JSON numbers.
- **EIK** — 9 or 13 digits, no `BG` prefix.
- **VAT number** — uppercase country prefix when visible, e.g. `"BG206802564"` for Bulgarian, `"DE123456789"` for German. The schema accepts any 2-letter prefix + 8–12 digits to support EU invoices.
- **IBAN** — no spaces, uppercase.
- **BIC** — uppercase, 8 or 11 chars.
- **Currency** — ISO 4217 (`"BGN"`, `"EUR"`, `"USD"`).
- **Enums** — exact values from the listed sets. Use `unknown` rather than free-form strings.

---

## Required Benchmark Fields

Minimum **accuracy** comparison surface across vendors. Diff scripts compare these fields field-by-field to score correctness.

- `extraction.document_type`
- `extraction.document.number`
- `extraction.document.date`
- `extraction.document.tax_event_date`
- `extraction.document.currency`
- `extraction.supplier.name`
- `extraction.supplier.eik`
- `extraction.supplier.vat_number`
- `extraction.supplier.mol`
- `extraction.customer.name`
- `extraction.customer.eik`
- `extraction.customer.vat_number`
- `extraction.totals.net`
- `extraction.totals.vat`
- `extraction.totals.gross`
- `validation.needs_review`

## Provider Diagnostics (NOT accuracy)

These fields measure *vendor characteristics* (cost, speed, self-reported confidence). They are useful for ROI comparison but **must not** influence accuracy scoring — a model that's confidently wrong fails the same way as a model that's reluctantly wrong.

- `model_assessment.confidence.overall`
- `model_assessment.confidence.*` (per-section)
- `provider.duration_ms`
- `provider.input_tokens`
- `provider.output_tokens`
- `provider.cost_estimate_usd`

---

## Changelog

### From Codex v1 (initial unification)

- Added `extraction.document.tax_event_date` (Q6).
- Added `extraction.supplier.mol` and `extraction.customer.mol` (Q5).
- Added `extraction.line_items[*].includes_vat` (Q11).
- Added `protocol` to the `document_type` enum (Q4).

### Post-Codex review, round 1 (2026-05-08)

- **Added** `source.detected_document_count` and `source.extracted_document_index` so multi-document loss is auditable instead of silent.
- **Split** `model_assessment` block out of `validation` so deterministic checks remain vendor-comparable.
- **Removed** `validation.is_valid_json` — a JSON file can't truthfully report its own validity from inside.
- **Consolidated** check codes — single `*_checksum` / `iban_mod97` code per validator (with `status: fail` for invalid), no duplicate `invalid_*` namespace.
- **Rewrote** line items validation rule to per-component sums; `includes_vat` is no longer used to "switch" between net/gross reconciliation.
- **Widened** `needs_review` triggers — image quality issues, multi-document warnings, partial crops, etc., all now flag for review even when confidence is high.
- **Made** `provider.input_tokens`, `output_tokens`, `cost_estimate_usd` explicitly nullable.
- **Softened** МОЛ legal-status language in [DISAGREEMENTS.md](DISAGREEMENTS.md) — common/important field, not strictly mandated by ЗДДС чл. 114.
- **Added** the formal JSON Schema file at [accountant.document.v2.schema.json](accountant.document.v2.schema.json) to prevent vendor drift.

### Post-Gemini review (2026-05-08)

Gemini reviewed the v2 contract from the lens of Bulgarian accounting (ЗДДС / НАП). Four additions, all driven by real legal/accounting requirements rather than schema aesthetics — applied because they're nullable and don't break existing logic:

- **Added** `extraction.vat_breakdown[].reason: string | null` — legal basis text for 0% VAT rates (e.g. "чл. 113, ал. 9 от ЗДДС"). Mandatory under ЗДДС for zero-rate invoices.
- **Added** `extraction.document.exchange_rate: decimal_string | null` — required by ЗДДС when the document currency is not BGN; the printed rate is used to convert tax base and VAT to BGN.
- **Added** `extraction.document.notes: string | null` — verbatim "Забележка" / "Основание за сделката" free text. Often contains references to proforma invoices, contract numbers, or retention-of-title clauses.
- **Clarified** that the `fiscal` block applies whenever fiscal device data is visible — including invoices paid in cash with an attached fiscal receipt. Not restricted to `document_type: receipt`.

### Post-Codex review, round 2 (2026-05-08)

- **Moved** `model_assessment.confidence.overall` out of "Required Benchmark Fields" into a new "Provider Diagnostics" section. Confidence is a vendor characteristic, not a correctness signal — comparing it as accuracy was self-contradicting the spec's own rule that `model_assessment` must not influence accuracy diffs.
- **Reframed** evidence required keys as "recommended minimum" — the JSON Schema doesn't enforce them, and for fiscal receipts or low-quality images some keys may not be meaningful.
- **Locked** schema enums for `provider.engine` (`anthropic | openai | google | other`) and `provider.pipeline` (`vision_direct | ocr_then_llm | hybrid | manual | other`) so diff/report scripts can rely on stable values.
- **Added** ISO 4217 pattern (`^[A-Z]{3}$`) to `extraction.document.currency` and `extraction.payments[].currency` in the schema.
- **Generalised** VAT-number convention text to support non-BG EU prefixes (matching what the schema's regex already accepted).

### Post-cross-vendor diff line-item review (2026-05-09)

Driven by [PROPOSAL_2026-05-09_line_items.md](PROPOSAL_2026-05-09_line_items.md), with consensus from Claude, Codex, and Gemini.

- **Added** `extraction.line_items[*].discount_pct: decimal_string | null` (R13) — required nullable property capturing per-line trade discount percentage. Lossless representation of "T.O. %" / "Отстъпка %" columns common on Bulgarian invoices.
- **Added** R12 to [NORMALIZATION_RULES.md](NORMALIZATION_RULES.md) — line item `net` / `vat` / `gross` storage is canonical (all three populated when derivable from any one + `vat_rate`), not verbatim. Resolves the systematic Codex-vs-others diff where `gross` was either the printed line value or the computed VAT-inclusive total.
- **Added** R13 to [NORMALIZATION_RULES.md](NORMALIZATION_RULES.md) — trade discount handling: `unit_price` stays printed pre-discount, `discount_pct` records the percentage, `net` is derived. Forbids `unit_price = line_total / quantity`.
- **Updated** R1 unit price precision — removed the "up to 4 decimals" cap; now "minimum 2, preserve printed precision (no upper bound)" to accommodate 5-decimal unit prices found in real invoices.
- **Documented** rounding mode for derived line values: `ROUND_HALF_UP` to 2 decimals, computed per-line, with invoice-level rounding pushed to a `totals.rounding` row only when the document explicitly prints one.
