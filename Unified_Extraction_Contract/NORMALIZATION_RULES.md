# Normalization Rules — to add to v2 contract

Derived from cross-vendor diff analysis on 23 documents (1648 field comparisons). 240 disagreements (15%) are pure format/normalization issues that produce noise in side-by-side comparison without reflecting real extraction quality differences. This document lists the rules each vendor must follow so those false positives disappear.

The rules below are additive to [EXTRACTION_CONTRACT.md](EXTRACTION_CONTRACT.md). They formalize conventions that were left implicit in v2.

---

## Diff statistics (run 2026-05-09 against 23-doc corpus)

| Class | Count | Share | Source |
|---|---:|---:|---|
| `identical` | 1057 | 64.1% | All 3 vendors agree |
| `real_disagreement` | 351 | 21.3% | Genuinely different values (OCR errors, parsing differences) — not addressed by these rules |
| `decimal_format` | 72 | 4.4% | `"20"` vs `"20.00"`, `"1"` vs `"1.00"` — **R1** |
| `null_zero` | 47 | 2.9% | `null` vs `"0.00"` — **R2** |
| **Other format-only** (whitespace, trailing punct, case, country code, etc.) | ~120 | ~7% | Listed below as R3–R10 |
| `document_type` misclassification | small | <1% | **R11** |
| Line item net/vat/gross convention | observed across 23-doc corpus | — | **R12** |
| Trade discount column handling | observed across 23-doc corpus | — | **R13** |

After applying rules R1–R13, the remaining diffs should be **only real extraction disagreements** — the only ones that reflect actual model accuracy.

---

## Rules

### R1. Decimal string precision

All decimal strings have a **fixed precision** by field type, with trailing zeros preserved:

| Field type | Precision | Example |
|---|---|---|
| Money (`net`, `vat`, `gross`, `discount`, `rounding`, `amount_due`, line item amounts, `payments[*].amount`) | **2 decimals** | `"230.00"`, `"0.00"`, `"-41.67"` |
| Percentages (`vat_rate`, `discount_pct`) | **2 decimals** | `"20.00"`, `"9.00"`, `"0.00"` |
| Quantity | **3 decimals** | `"240.000"`, `"1.000"`, `"19.000"` |
| Unit price | **Minimum 2 decimals; preserve printed precision when visible (no upper bound)** | `"66.67"`, `"0.958"`, `"1.95583"`, `"0.123456"` |
| Exchange rate | **As printed**; min 2 decimals | `"1.95583"`, `"2.00"` |

**Wrong:** `"20"`, `"1"`, `"230"`, `"0"`.
**Right:** `"20.00"`, `"1.000"` (quantity) or `"1.00"` (money), `"230.00"`, `"0.00"`.

Currently violated by: Codex (writes `"20"` for VAT rate, `"1"` for quantity).

---

### R2. `null` vs `"0.00"` semantics

- **`null`** — the field is **not printed** on the document.
- **`"0.00"`** — the field is **explicitly printed as zero** on the document.

Field-by-field guidance:

- `totals.discount`, `totals.rounding`: usually `null`. Only `"0.00"` if the document literally shows a discount/rounding row with value 0.
- `vat_breakdown[*].vat` when the row's `rate` is `"0.00"`: use `"0.00"` (explicit zero is the point of a 0% line).
- `line_items[*].vat` for VAT-exempt lines: `"0.00"` if the document shows a VAT column with 0; `null` if the document has no VAT column at all.

Currently violated by: Gemini (fills `"0.00"` for non-printed discount/rounding).

---

### R3. Country codes

`country` fields use **ISO 3166-1 alpha-2** uppercase only:

- Right: `"BG"`, `"DE"`, `"GB"`, `"US"`.
- Wrong: `"Bulgaria"`, `"България"`, `"BGR"`.

Default `"BG"` for Bulgarian invoices unless the address is clearly foreign.

Currently violated by: Gemini (`"Bulgaria"`).

---

### R4. Address vs city split

The `address` and `city` fields have specific responsibilities:

- **`address`**: street, number, neighborhood, building/entrance/floor/apartment — **no city, no postcode**.
- **`city`**: just the city name in **Bulgarian title case**.
- Postcode goes nowhere right now (drop it; future schema may add `postcode`).

Examples:

| Printed on document | `city` | `address` |
|---|---|---|
| `"гр. Варна, 9010, ул. Рощок 9"` | `"Варна"` | `"ул. Рощок 9"` |
| `"София 1309, ул. Кукуш 1"` | `"София"` | `"ул. Кукуш 1"` |
| `"гр. Бургас, жк \"Славейков\"-СПЗ, Хипермаркет"` | `"Бургас"` | `"жк \"Славейков\"-СПЗ, Хипермаркет"` |
| `"Габрово, ПК 5300"` | `"Габрово"` | (whatever the street line says) |

Currently violated by: Claude (puts postcode in `city`: `"Габрово, ПК 5300"`).

---

### R5. String casing

| Field type | Rule | Example |
|---|---|---|
| Company names | **Verbatim** as printed | `"АВАНС ТРЕЙД ООД"`, `"Инко трейд ЕООД"` |
| Person names (МОЛ, operator) | **Verbatim** as printed | `"Николай Умников"`, `"ИЛИАН ЦВЕТКОВ"` |
| **City names** | **Bulgarian title case** | `"Варна"` (not `"ВАРНА"`), `"София"` (not `"СОФИЯ"`) |
| Currency, country code, BIC, IBAN | **Uppercase** | `"BGN"`, `"BG"`, `"UNCRBGSF"` |

The city-name normalization is the most common source of disagreement — vendors mirror what the document says (`"гр. ВАРНА"` → `"ВАРНА"`). Title case fixes this.

---

### R6. Whitespace normalization

For all string fields:
- Strip leading and trailing whitespace.
- Collapse internal whitespace runs to a single space.
- Strip non-breaking spaces (` ` → ` `).

`"ул.  Рощок 9 "` → `"ул. Рощок 9"`.
`"ПИБ АД"` → `"ПИБ АД"`.

---

### R7. Trailing punctuation in `unit`

`line_items[*].unit` is **stripped of trailing punctuation** for normalization:

- `"бр."` → `"бр"`
- `"кг."` → `"кг"`
- `"час."` → `"час"`
- `"бр"` stays `"бр"`.

This is one rare case where strict "verbatim" loses to consistency. Pick stripped form so `"бр."` and `"бр"` from different vendors compare equal.

Currently violated by: Claude/Gemini (keep dot), Codex (strips). Lock everyone on the stripped form.

---

### R8. Exchange rate

- `null` when `currency == "BGN"` (regardless of whether `"1.00"` is printed).
- `null` when no exchange rate is printed (most invoices).
- Decimal string ONLY when the document explicitly prints a non-trivial rate (typically EUR/USD invoices showing a BGN conversion).

Currently violated by: Gemini (writes `"1.00"` for BGN invoices).

---

### R9. Document number leading zeros

`document.number` is **verbatim including all leading zeros** as printed:

- `"0000006137"` (10 chars), not `"6137"`.
- `"0300051648"`, not `"300051648"`.

The field is a string; treat it as one. Don't trim leading zeros.

Currently mostly OK across vendors.

---

### R10. ЕИК prefix and VAT number prefix

**Two distinct fields, two distinct rules:**

- `eik`: **digits only**, no `BG` prefix. 9 or 13 digits.
  - Right: `"206802564"`. Wrong: `"BG206802564"`.
- `vat_number`: **uppercase 2-letter country prefix + digits**.
  - Right: `"BG206802564"`. Wrong: `"206802564"`, `"bg206802564"`.

Both fields are typically derived from the same printed value ("ИН по ЗДДС: BG206802564" or "ЕИК: 206802564"). Models must populate them consistently — `vat_number == "BG" + eik` for Bulgarian companies.

---

### R11. `document_type` classification

The v2 contract defines the enum but left the *decision rule* implicit. Vendors disagree most often when a document is paid by card or has a fiscal receipt attached — some treat that as a signal for `receipt`, which is wrong.

Apply in strict priority order:

**Step 1 — printed title is authoritative.** What's at the top of the document wins:

| Printed title | `document_type` |
|---|---|
| `ФАКТУРА` / `FACTURA` / `INVOICE` | `invoice` |
| `ПРОФОРМА ФАКТУРА` / `PROFORMA` / `Pro forma` | `proforma` |
| `КРЕДИТНО ИЗВЕСТИЕ` | `credit_note` |
| `ДЕБИТНО ИЗВЕСТИЕ` | `debit_note` |
| `ПРИЕМО-ПРЕДАВАТЕЛЕН ПРОТОКОЛ` | `protocol` |
| `КАСОВ БОН` / `ФИСКАЛЕН БОН` / `ФИСКАЛЕН ВАУЧЕР` | `receipt` |
| `СТОРНО` (standalone) | `credit_note` |

`proforma` matching is case-insensitive — many issuers print `Pro forma` or `Проформа` mixed-case rather than full uppercase.

**Step 2 — no title? infer from structure:**

- Narrow thermal-printer strip, fiscal device number printed, **no recipient details** → `receipt`
- Supplier AND customer (both with EIK), VAT breakdown table → `invoice`
- Both (an invoice with an attached/printed fiscal receipt) → `invoice` (fiscal data goes into the `fiscal` block per the v2 contract; it does **not** change the type)

**Step 3 — undecidable:**

- POS card slip without fiscal data → `unknown`
- Anything ambiguous → `unknown` AND add a note to `model_assessment.extraction_warnings`

**Anti-rules — common mistakes to avoid:**

- Payment method (card / bank / cash) does **NOT** influence document type. A card-paid фактура is still `invoice`.
- A `Касова бележка №…` line printed at the bottom of a фактура does **NOT** make it a `receipt`. It stays `invoice` with the `fiscal` block populated.
- Document size or orientation (narrow strip) is not decisive on its own — if full invoice fields are present, it's `invoice`.

Currently violated by: Codex (classified `20250402_210847.jpg` as `receipt` despite the title being "ФАКТУРА" — likely triggered by `Плащане с карта`).

---

### R12. Line item `net` / `vat` / `gross` derivation

`line_items[*]` storage is **canonical**, not verbatim. When line-level arithmetic is decidable, the model populates all three of `net`, `vat`, `gross` — even if only one is printed on the document. Leaving fields `null` because they aren't printed per-line bypasses the `line_items_sum` validator silently and produces noisy cross-vendor diffs.

**Decide first what the printed line value represents:**

- Invoice line tables usually print **net** line value.
- Fiscal receipts often print **gross** line value.
- `includes_vat` records that printed orientation. It does **not** change storage — storage is always all three components when derivable.

**Derivation table:**

| Printed on the line | How to fill `net` / `vat` / `gross` |
|---|---|
| `net` + `vat_rate` | `vat = net × rate/100`; `gross = net + vat` |
| `gross` + `vat_rate` | `net = gross / (1 + rate/100)`; `vat = gross − net` |
| `net` + `vat` (no rate) | `gross = net + vat`; `vat_rate = vat/net × 100` |
| All three printed | Verbatim — no recalculation. If they don't reconcile within tolerance (0.02), keep printed values and add a warning to `model_assessment.extraction_warnings`. Never silently "fix" the document. |
| Single price column, no `vat_rate` | All three `null`. Validator's `line_items_incomplete` warning will fire. |

**Zero-rate lines:**

- `vat_rate = "0.00"`
- `vat = "0.00"` (zero is explicit on a 0% line)
- `gross = net`
- `vat_breakdown[*].reason` carries the legal basis when printed

**Rounding for derived values:** `ROUND_HALF_UP` to 2 decimals (`Decimal.quantize(Decimal("0.01"), ROUND_HALF_UP)`). Compute per-line first, then compare against `totals` with tolerance — do not push invoice-level rounding differences into individual line values unless the document explicitly prints a "Закръгляване" row.

Currently violated by: Codex (leaves `vat = null` per line, stores printed net under `gross`).

---

### R13. Trade discount column ("T.O. %" / "Отстъпка %")

When a per-line discount **percentage** column is printed:

- `unit_price` = the **printed pre-discount** unit price. Never compute `unit_price = line_total / quantity`.
- `discount_pct` = the printed discount percentage (decimal string, 2 decimals per R1).
- `net = quantity × unit_price × (1 − discount_pct/100)` (rounded per R12).
- `vat = net × vat_rate / 100`.
- `gross = net + vat`.

When no discount column is printed, `discount_pct = null`.

**Schema impact:** `line_items[*].discount_pct` is added in v2 as a required nullable property (every defined field always present, per the v2 conventions).

**Absolute-amount discounts (corner case, no schema field yet):** if the document prints an absolute lev discount per line rather than a percentage, keep the printed `unit_price`, derive `net` from the printed final line amount, and add a warning to `model_assessment.extraction_warnings`. A future schema may add `line_items[*].discount_amount` if corpus evidence justifies it — currently no such examples exist in the 23-doc corpus.

Currently violated by: Gemini (computed `unit_price = line_total / quantity`, ignoring the discount column).

---

## How to apply

1. **All three implementations** update their prompts to enforce R1–R13 and re-run extraction on the 23-doc corpus.
2. **Diff tool** (ReviewSite) can also normalize before comparison so historical outputs don't show false positives. Suggested order:
   - Strip whitespace + collapse runs (R6).
   - Apply decimal precision normalization (R1) — parse as Decimal, format with fixed precision.
   - Strip trailing dot from `unit` (R7).
   - Compare `null` and `"0.00"` as different (R2 makes them semantically different).
3. **After re-run**: real disagreements should drop to ~351 — these are the actual accuracy differences worth investigating.

---

## What this does NOT cover

The remaining 21% of comparisons (351 real disagreements) include:

- **OCR digit confusion**: `"0000300823"` vs `"0000308823"` — model error, not format.
- **Supplier/customer confusion**: Codex sometimes substitutes recipient's EIK (`206802564` — the user's own company) as supplier EIK on documents where the supplier's printing is faded.
- **VAT-inclusive ambiguity**: `includes_vat: false` vs `true` — model interpretation differences for receipts with VAT-inclusive prices.
- **Long alphanumeric strings (QR codes, fiscal device serials)**: usually contain at least one OCR-confused character per vendor.
- **Notes / place / operator fields**: vendors disagree on what to capture as "place of deal" vs leaving null.

These are real model accuracy issues. They surface in `model_assessment.confidence` and in the validator's `*_checksum` checks, and that's the right place to handle them — not via normalization.
