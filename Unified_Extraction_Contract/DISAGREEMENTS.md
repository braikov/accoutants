# Open Questions for Unified Contract v2 — RESOLVED

**Status: All 11 questions resolved 2026-05-08.** All answers: **(rec)**.

The contract at [EXTRACTION_CONTRACT.md](EXTRACTION_CONTRACT.md) is now authoritative. This document is kept as historical reference for *why* each decision was made.

## Answer summary

| # | Decision | Resolution |
|---|---|---|
| Q1 | Party labelling | `supplier` / `customer` |
| Q2 | Output structure | One JSON file with six top-level blocks |
| Q3 | Number representation | Decimal strings (`"230.00"`, never JSON floats) |
| Q4 | `protocol` document type | Added to enum |
| Q5 | `mol` field on parties | Added to `party` schema (note: not legally mandated by ЗДДС чл. 114) |
| Q6 | `tax_event_date` | Added to `document` block |
| Q7 | Multi-document images | Extract primary; metadata flags loss; future v3 may add `documents: [...]` |
| Q8 | Address granularity | Verbatim line + extracted `city` |
| Q9 | Bank info | `payments[]` array |
| Q10 | System prompt language | English with Bulgarian terms quoted |
| Q11 | `includes_vat` line item flag | Added |

Three subsequent review rounds (Codex round 1, Codex round 2, Gemini) produced 17 additional refinements — see [EXTRACTION_CONTRACT.md → Changelog](EXTRACTION_CONTRACT.md#changelog) for the full list.

---

## Original questions and rationale

Notation: each option ends with the vendor(s) that proposed it in parentheses, so you can trace back if you want.

---

## Q1. Party labelling

What do we call the two parties on the document?

- **A. `supplier` / `customer`** *(Codex)* **(rec)**
  - Matches EU PEPPOL/UBL e-invoicing standards. Internationally recognised.
- B. `supplier` / `recipient` *(Claude)*
  - Closer to Bulgarian "Получател" but mixed metaphor (supplier+recipient).
- C. `issuer` / `recipient` *(Gemini)*
  - Matches Bulgarian "Издател/Получател" verbatim. Less aligned with international standards.

Why it matters: any downstream code that consumes the JSON references these keys. Renaming later is painful across all three implementations + any DB schema.

---

## Q2. Output structure

One JSON file per document, or three?

- **A. One file with five top-level blocks** (`source`, `extraction`, `validation`, `evidence`, `provider`) *(Codex / Gemini)* **(rec)**
  - Simpler artifact management. Diff script just ignores `provider` block.
- B. Three files: `<image>.result.json`, `<image>.run.json`, `<image>.validation.json` *(Claude)*
  - Cleaner diffs by default but 3× the files to ship around.

I originally pushed for B; on reflection A is fine and matches what Codex/Gemini already produce.

---

## Q3. Number representation

How are amounts and quantities encoded?

- **A. Decimal strings** — `"230.00"`, `"0.958"` *(Codex)* **(rec)**
  - Eliminates JSON float precision bugs (`0.1 + 0.2 ≠ 0.3`). Banks use this.
  - Cost: parse to `Decimal` / `BigDecimal` before arithmetic. Trivial.
- B. JSON floats — `230.0`, `0.958` *(Claude / Gemini)*
  - Native JSON, looks more natural.
  - Cost: rounding errors on sums of many line items.
- C. Hybrid — strings for money, floats for quantities and percentages.
  - Inconsistent; not worth the cognitive overhead.

---

## Q4. `protocol` document type

Bulgarian "Приемо-предавателен протокол" is common (we have one in our 23 samples — the Masterhaus document).

- **A. Add `protocol` to the `document_type` enum** *(Claude)* **(rec)**
- B. Map all protocols to `other`, then add `other` to enum (Codex's enum currently has only `unknown`)
- C. Map to `unknown` (loses category)

Real value: it lets downstream code route protocols differently from invoices.

---

## Q5. `mol` field on parties

МОЛ ("Материално отговорно лице") is a common and important field on Bulgarian invoices but is **not** strictly mandated by ЗДДС чл. 114 (corrected per Codex review — НАП's required invoice elements list does not explicitly include МОЛ). It's still worth capturing because most invoices include it and downstream accounting systems often expect it.

- **A. Add `mol: string | null` to the `party` schema** *(Claude / domain knowledge)* **(rec)**
- B. Skip — leave it in `notes` or `evidence`
- C. Add as a separate top-level field outside party

Recommendation A captures it without overstating its legal status.

---

## Q6. `tax_event_date`

Bulgarian invoices have "Дата на данъчно събитие" — the date the taxable event occurred — which is legally distinct from the issue date and may differ. Codex's spec doesn't have this field.

- **A. Add `extraction.document.tax_event_date: string | null`** *(Claude / domain knowledge)* **(rec)**
- B. Use `document.date` for both — accept some loss of fidelity
- C. Skip; let the model put it in `notes`

A is the only correct answer for proper Bulgarian accounting compliance.

---

## Q7. Multi-document images

Some images contain multiple documents (we have one with a protocol + a fiscal receipt side by side, another with 4 receipts on one page).

- **A. Extract the primary/largest document; warn about others in `extraction_warnings`** *(Claude default)* **(rec)**
  - Simple contract. Most images are single-doc anyway.
  - Loss: 4-receipts-on-one-page image gets 1 result.
- B. Top-level shape becomes `{ documents: [...] }` — always a list
  - Heavier shape. Even single-doc cases pay the array overhead.
- C. Reject multi-doc images upstream and require splitting before processing

A is the cheapest; B is the most thorough. C requires a preprocessing pipeline.

---

## Q8. Address granularity

How structured is `party.address`?

- **A. Single verbatim line + extracted `city`** (current Claude/Codex) **(rec)**
  - One field where the model just transcribes what's printed.
- B. Split into `street`, `street_number`, `postcode`, `city`, `district`
  - More queryable but multiplies disagreement points across vendors.
- C. Both — verbatim `address` plus structured fields when parseable

Address parsing can happen post-extraction once we have the verbatim string. Not worth doing at extraction time.

---

## Q9. Bank info: single object vs `payments[]` array

- **A. `payments[]` array** *(Codex)* **(rec)**
  - Each payment has its own method/IBAN/BIC/amount/currency.
  - Handles split payments (deposit + balance), card-only (no IBAN), and bank-transfer cleanly with one shape.
- B. Single `bank` object with `iban`, `bic`, `bank_name` *(Claude)*
  - Simpler but doesn't handle split payments or cards naturally.

A's flexibility is worth the slight overhead.

---

## Q10. System prompt language

The prompt sent to each vision model — Bulgarian or English?

- **A. English with Bulgarian terms quoted** *(Claude / Codex)* **(rec)**
  - Models follow English instructions more reliably. Bulgarian terms ("ЕИК", "Дата на данъчно събитие") quoted inline give context.
- B. Bulgarian *(Gemini)*
  - Reads natural to a Bulgarian human reviewer but has produced slightly less consistent results in side-by-side tests with Claude/Codex.
- C. Both versions in the spec, each vendor picks
  - Defeats the point of unification.

A is what I tested with Claude and got 22/23 docs extracted with no warnings.

---

## Q11. `includes_vat` flag on line items

Some receipts print line amounts with VAT included; some invoices print them net. Without a flag, `sum(line_items.amount)` doesn't match `totals.tax_base` on receipts even when extraction is correct.

- **A. Add `extraction.line_items[*].includes_vat: bool | null`** *(Claude)* **(rec)**
  - Validator checks `sum == totals.gross` if true, `sum == totals.net` if false, skips if null.
- B. Always require all three of `net`/`vat`/`gross` per line *(Codex's current approach)*
  - Forces the model to compute the missing two from the printed one. Different vendors will disagree on the derived values.
- C. Add `includes_vat` at document level rather than line level
  - Simpler but doesn't handle mixed-VAT-status lines.

A handled the receipt false-positives I saw in the 23-sample run cleanly.

---

## How to answer

Reply with `Q1: A, Q2: A, Q3: A, ...` etc., or just say "all recs" to accept every recommended option. Then I'll finalise [EXTRACTION_CONTRACT.md](EXTRACTION_CONTRACT.md) and write the JSON Schema.
