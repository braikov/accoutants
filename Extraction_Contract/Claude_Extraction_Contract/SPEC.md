# Cross-vendor extraction spec — Bulgarian payment documents

This spec defines the canonical output format for image-to-JSON extraction of Bulgarian payment documents. Claude, Codex (OpenAI), and Antigravity (Google) implementations all target this same shape so results are directly diff-able.

Three files per processed image — each has a distinct responsibility:

| File | Owner | Purpose |
|---|---|---|
| `<image>.result.json` | the model | Pure facts about the document. Vendor-neutral. **This is what you compare across tools.** |
| `<image>.run.json` | the harness | Vendor metadata: model id, tokens, latency, cost, prompt version. Used for ROI comparison. |
| `<image>.validation.json` | a shared validator script | Deterministic checks (ЕИК checksum, IBAN mod-97, arithmetic) run on `result.json`. **Same validator for all three vendors** — so validation pass-rate is a fair metric. |

## Why three files instead of one

- **Pure data is diffable.** If `result.json` mixes in `tokens_used: 5650`, every diff between Claude and Codex shows a phantom delta on `tokens_used` even when the actual extraction is identical.
- **Validation must be vendor-independent.** If each tool ships its own validator, "Claude passes 90%" and "Codex passes 85%" mean different things. A shared validator script (port to whichever language you want — algorithm spelled out below) is the only fair way.
- **Run metadata is per-vendor by definition.** `model: "claude-sonnet-4-6"` simply has no equivalent in Codex output; trying to align them in one file is a category error.

---

## 1. `result.json` — the extraction

### Universal rules

- **Snake_case** for all keys.
- **Every defined field is always present** in the output (use `null`, never omit). Lists default to `[]`, not `null`. Eliminates ambiguity in diffs ("did the model not extract this, or did it omit the field?").
- **`schema_version`** is a top-level string field. Bump on breaking changes.
- **Strings are stripped** of leading/trailing whitespace; internal whitespace is collapsed to single spaces.
- **Dates** are ISO 8601 (`YYYY-MM-DD`). `null` if unreadable or absent.
- **Numbers** are JSON numbers (not strings). Amounts use 2 decimals max (`230.0`, `66.67`); unit prices may use up to 4 (`0.958`, `66.667`).
- **EIK** — 9 or 13 digit string, **no "BG" prefix** (the prefix lives in `vat_number`).
- **VAT number** — keep "BG" prefix uppercase (`"BG206802564"`).
- **IBAN** — no spaces, uppercase (`"BG62BPBI79291053720501"`).
- **BIC** — uppercase, 8 or 11 chars.
- **Currency** — ISO 4217 (`"BGN"`, `"EUR"`, `"USD"`).
- **Enums must be from the listed set** — see below. Use `"unknown"` / `"other"` rather than free-form strings.

### Enum vocabularies

```
document_type:    invoice | receipt | proforma | protocol | credit_note | debit_note | other
payment_method:   bank_transfer | cash | card | other | unknown
currency:         BGN | EUR | USD | other
```

### Schema

See `example.result.json` for a fully-populated example. The shape:

```jsonc
{
  "schema_version": "1.0",
  "document_type": "invoice",
  "document_no": "0300051648",        // verbatim, leading zeros preserved
  "issue_date": "2023-12-11",
  "tax_event_date": "2023-12-11",
  "due_date": "2023-12-19",
  "place_of_deal": null,

  "supplier": {
    "name": "АВАНС ТРЕЙД ООД",
    "eik": "201760264",                // no "BG"
    "vat_number": "BG201760264",       // with "BG"
    "address": "ул. Свищовска 1",
    "city": "Габрово",
    "mol": "Николай Умников",          // материално отговорно лице
    "phone": "066 80 10 43"
  },

  "recipient": { /* same shape as supplier */ },

  "bank": {                            // null when no bank info on document
    "bank_name": "Юробанк България АД",
    "iban": "BG62BPBI79291053720501",
    "bic": "BPBIBGSF"
  },

  "payment_method": "bank_transfer",
  "currency": "BGN",

  "line_items": [
    {
      "position": 1,
      "code": "730",
      "description": "М.К.120 КАПАК КЕЪР СОФТх24",
      "unit": "бр.",                   // мярка as printed
      "quantity": 240.0,
      "unit_price": 0.958,
      "discount_pct": null,
      "amount": 230.0,
      "vat_rate": null,                // null = use top-level totals.vat_rate
      "includes_vat": false            // see "VAT inclusion" below
    }
  ],

  "totals": {
    "tax_base": 230.0,                 // данъчна основа (without VAT)
    "vat_rate": 20.0,                  // % standard rate
    "vat_amount": 46.0,
    "total": 276.0,                    // сума за плащане
    "amount_in_words": "Двеста седемдесет и шест лв."
  },

  "notes": "Съставил: РАДОСЛАВ НЕНОВ",  // free text from document

  "extraction_warnings": [
    "Recipient MOL field appears blank on the document."
  ]
}
```

### VAT inclusion — important normalization

In our sample corpus, ~20% of documents (касови бележки and some протоколи) print **gross prices** in the line items (with VAT included), while the totals section shows tax_base and VAT separately. Without a flag, `sum(line_items.amount) ≠ tax_base` looks like an extraction error when in fact it's correct.

Convention:

- `line_items[*].includes_vat: false` (default for invoices) → `amount` is net, sum should equal `totals.tax_base`.
- `line_items[*].includes_vat: true` (typical for receipts) → `amount` is gross, sum should equal `totals.total`.
- If the model can't determine, leave `null` and the validator skips the line-sum check.

### `extraction_warnings`

A list of one-sentence strings, in **English**, describing concrete uncertainties the model encountered:

- `"Last digit of EIK partially covered by stamp; read as 4 but could be 1."`
- `"Image contains a second receipt at top right; only main invoice extracted."`
- `"Total amount printed only in words, not digits; tax_base+vat used to derive."`

Empty list if nothing is uncertain. **Not** a place for general commentary.

---

## 2. `run.json` — the harness metadata

```jsonc
{
  "schema_version": "1.0",
  "vendor": "anthropic",                  // anthropic | openai | google
  "model": "claude-sonnet-4-6",
  "prompt_version": "claude-2026-05-08",  // free-form; bump when prompt changes
  "input_file": "20240213_190514.jpg",
  "input_bytes": 943210,
  "input_resized": false,                 // true if image was downscaled before send
  "input_resize_note": null,              // human-readable summary if resized
  "started_at": "2026-05-08T13:01:42Z",
  "elapsed_sec": 14.27,
  "tokens": {
    "input": 5650,
    "output": 696
  },
  "cost_usd": 0.0274,                     // best estimate per the vendor's published rate
  "stop_reason": "tool_use",
  "error": null                           // string with error message if extraction failed
}
```

This file is what answers "which vendor is fastest / cheapest" — never look at it for accuracy comparison.

---

## 3. `validation.json` — deterministic post-checks

Produced by a single shared validator script. The same `result.json` fed to the same validator must always produce the same `validation.json`, regardless of which vendor produced the result.

```jsonc
{
  "schema_version": "1.0",
  "validator_version": "1.0",
  "result_file": "20240213_190514.result.json",
  "passed": true,
  "checks": [
    { "name": "supplier_eik_checksum",      "status": "pass", "message": null },
    { "name": "recipient_eik_checksum",     "status": "pass", "message": null },
    { "name": "iban_mod97",                 "status": "pass", "message": null },
    { "name": "bic_format",                 "status": "pass", "message": null },
    { "name": "totals_arithmetic",          "status": "pass", "message": null },
    { "name": "vat_rate_consistency",       "status": "pass", "message": null },
    { "name": "line_items_sum",             "status": "pass", "message": null },
    { "name": "issue_date_format",          "status": "pass", "message": null },
    { "name": "due_date_format",            "status": "pass", "message": null },
    { "name": "due_date_after_issue",       "status": "pass", "message": null },
    { "name": "currency_iso",               "status": "pass", "message": null },
    { "name": "document_type_enum",         "status": "pass", "message": null }
  ]
}
```

`status` is one of `pass` / `fail` / `skip`. `skip` when the input field was `null`, so the check couldn't run (e.g. no IBAN to validate) — this is **not** a failure.

`passed` at the top is `true` iff zero checks have `fail`.

### Check definitions (port to any language)

- **`supplier_eik_checksum` / `recipient_eik_checksum`** — Bulgarian EIK algorithm. 9-digit: weights `[1,2,3,4,5,6,7,8]` × first 8 digits, mod 11. If result is 10, retry with weights `[3,4,5,6,7,8,9,10]`; if again 10, checksum is 0. 13-digit: as above for first 9, then weights `[2,7,3,5]` for digits 9-12 mod 11 (retry with `[4,9,5,7]` if 10 → 0).
- **`iban_mod97`** — Move first 4 chars to end, replace each letter with `(ord - 55)`, interpret as integer, must equal 1 mod 97.
- **`bic_format`** — Regex `^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$`.
- **`totals_arithmetic`** — `abs(tax_base + vat_amount - total) ≤ 0.02`.
- **`vat_rate_consistency`** — `abs(tax_base * vat_rate / 100 - vat_amount) ≤ 0.05`.
- **`line_items_sum`** — If any `line_items[*].includes_vat` is `true`, expected = `totals.total`; else expected = `totals.tax_base`. Skip if mixed or all null. Tolerance 0.05.
- **`issue_date_format` / `due_date_format`** — Must parse as ISO 8601 date.
- **`due_date_after_issue`** — `due_date >= issue_date`.
- **`currency_iso`** — Value ∈ enum.
- **`document_type_enum`** — Value ∈ enum.

A reference Python implementation of the validator lives in `../Claude/src/validate.py` (Anthropic implementation). It's vendor-agnostic — feed it any vendor's `result.json` and it produces the same `validation.json` shape.

```python
# from C:\Projects\Miro\Accountant\Claude
from src.validate import validate_document
from src.schema import ExtractedDocument
import json

doc = ExtractedDocument.model_validate(json.load(open("any_vendor_result.json")))
report = validate_document(doc)
```

Codex / Antigravity implementations are free to write their own validator in any language — they MUST produce the same `checks` list and the same `passed` decision for any given `result.json`.

---

## File naming convention

For a given source image `20240213_190514.jpg`, each vendor produces:

```
<vendor-folder>/
└── output/
    ├── 20240213_190514.result.json
    ├── 20240213_190514.run.json
    └── 20240213_190514.validation.json
```

Where `<vendor-folder>` is `Claude/`, `Codex/`, `Antigravity/`. Then a comparison script can read all three `result.json` files and produce a field-level agreement report.

---

## Open questions for cross-tool discussion

These are choices where a wrong call now becomes painful later — flag them with the other agents:

1. **Address granularity.** Right now `address` is one verbatim line plus extracted `city`. Should we split further (`street`, `street_number`, `postcode`, `district`)? More structure helps queries; more fields means more places models disagree.
2. **Person names — single field or split?** Keep `mol: "Николай Умников"` or split `mol_first_name` / `mol_last_name`?
3. **Multi-document images.** Should `result.json` be a single object or a list `documents: [...]`? Currently we extract the primary and warn about others. A list is cleaner but most vendors will produce a single doc.
4. **Receipt-only fields.** Fiscal receipts have UNP, fiscal device serial, QR data — currently dropped. Worth a `receipt_metadata` sub-object?
5. **Confidence scoring.** Per-field confidence (`{ "value": "...", "confidence": 0.9 }`)? Heavier shape, but helps weight diff disagreements.
