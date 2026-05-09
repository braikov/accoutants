# Proposal — Line item rules (R12, R13)

**Status:** RESOLVED — accepted with consensus from Codex and Gemini, folded into the contract on 2026-05-09.
**Date:** 2026-05-09.
**Author:** Claude side (after cross-vendor diff on 23-doc corpus).
**Affects:** [NORMALIZATION_RULES.md](NORMALIZATION_RULES.md) + [accountant.document.v2.schema.json](accountant.document.v2.schema.json) + [EXTRACTION_CONTRACT.md](EXTRACTION_CONTRACT.md) + [example.result.json](example.result.json).

## Resolution (2026-05-09)

All three vendors converged. Final decisions:

- **R12** — accepted as-stated. Line item `net` / `vat` / `gross` storage is canonical (all three populated when derivable). `includes_vat` describes printed orientation only. Zero-rate edge cases and `ROUND_HALF_UP` rounding mode codified per Codex review. Folded into [NORMALIZATION_RULES.md](NORMALIZATION_RULES.md) §R12.
- **R13 — Option A** chosen: schema gains `line_items[*].discount_pct` as a required nullable decimal string. Folded into [NORMALIZATION_RULES.md](NORMALIZATION_RULES.md) §R13 and [accountant.document.v2.schema.json](accountant.document.v2.schema.json).
- **`discount_amount`** — deferred. Gemini withdrew the request after acknowledging no corpus evidence; Codex's "wait for evidence" stance prevailed. Absolute-amount discounts handled via warning-only path documented in R13 until a real example surfaces.
- **R1 unit price precision** — updated per Codex: removed the "up to 4 decimals" cap, replaced with "minimum 2; preserve printed precision (no upper bound)".

The discussion below is preserved as the audit trail for these decisions. Future contributors should treat the resolution above as authoritative.

---

## Background

Cross-vendor diff revealed two systematic disagreements in `extraction.line_items[*]` that are not extraction errors — they are **conventions left implicit** in the v2 contract. Both produce loud false-positives in the diff and need explicit rules.

---

## Problem 1 — `net` / `vat` / `gross` convention

### Observed divergence

`IMG-fb7be744…`, line 0 (vat_rate = 20%):

| Field | Codex | Claude | Gemini |
|---|---|---|---|
| `unit_price` | `1.88333` | `1.8833` | `1.88333` |
| `vat` | `null` | `27.35` | `27.35` |
| `gross` | `136.73` | `164.08` | `164.08` |

`20250402_210847.jpg`, line 2 (vat_rate = 20%):

| Field | Codex | Claude | Gemini |
|---|---|---|---|
| `gross` | `88.64` | `106.37` | `106.37` |

### Analysis

`136.73 × 1.20 = 164.08` and `27.35 = 136.73 × 0.20`. Same arithmetic for line 2: `88.64 × 1.20 = 106.37`.

- **Codex** treats `gross` as the **printed line value verbatim** (which on these documents is actually net), and leaves `vat` as `null` because it's not printed per-line.
- **Claude / Gemini** **derive** the missing components from the printed value plus `vat_rate`, filling all three.

The v2 contract example fills all three (`net=230.00`, `vat=46.00`, `gross=276.00` with 240 × 0.958 ≈ 230, gross = net × 1.20). This implies derivation, but the contract never says so explicitly. The line-items validator (`line_items_sum`) skips per-component checks when any line has `null` for that component — so Codex's behavior bypasses validation silently.

### Proposed R12 — Line item component derivation

When `vat_rate` is known and at least one of `net` / `gross` is printed, the model **derives** the missing components and fills all three. No component is left `null` unless the math is undecidable.

| What's printed on the line | How to fill `net` / `vat` / `gross` |
|---|---|
| `net` + `vat_rate` | `vat = net × rate/100`; `gross = net + vat` |
| `gross` + `vat_rate` | `net = gross / (1 + rate/100)`; `vat = gross − net` |
| `net` + `vat` (no rate) | `gross = net + vat`; `vat_rate = vat/net × 100` |
| All three printed | Verbatim, no recalculation. If the printed numbers don't reconcile within tolerance (0.02), keep them and emit a warning in `model_assessment.extraction_warnings`. |
| Only a single price column, no rate | All three `null`. Validator's `line_items_incomplete` warning will trigger. |

**`includes_vat` semantics (clarification, not change):** `includes_vat` describes the **printed orientation** — was the printed amount net or gross? It does not change the storage model. Storage is always all three components when derivable.

**Rounding for derived values:** standard half-up to 2 decimals (`Decimal.quantize(Decimal("0.01"), ROUND_HALF_UP)`). Document the rounding mode so all vendors match exactly.

**Why this matters:** today the same line shows `gross: 136.73` from one vendor and `gross: 164.08` from another, both "right" depending on interpretation. The diff has no way to tell that these are equivalent — they look like a real disagreement. R12 forces a single interpretation.

---

## Problem 2 — Trade discount column ("T.O. %" / "Отстъпка %")

### Observed divergence

`20250402_210847.jpg`, line 2:

| Field | Codex | Claude | Gemini |
|---|---|---|---|
| `unit_price` | `27.70` | `27.70` | `22.16` |

The document prints columns: `Кол=4`, `Ед. цена=27.70`, `T.O. %=20`, `Стойност=88.64`.
- `4 × 27.70 = 110.80` ≠ printed line value
- `4 × 27.70 × (1 − 0.20) = 88.64` ✓ — there's a 20% trade discount applied
- `88.64 / 4 = 22.16` — this is what Gemini reported as `unit_price`, but it's not printed anywhere

### Analysis

- **Codex / Claude** report `unit_price = 27.70` (the printed value, before discount). Correct per "verbatim".
- **Gemini** divided line total by quantity, ignoring the discount column. Wrong.

But there's no field to capture the discount rate per line. The current schema has no `line_items[*].discount_pct`. Whichever vendor is "verbatim" loses information: either the printed unit price or the relationship `qty × unit_price ≠ line_total`.

### Proposed R13 — Trade discount handling

Three implementation options, ordered by impact:

#### Option A — Schema change (recommended)

Add `line_items[*].discount_pct: decimal_string | null` (2 decimals, R1) to the schema.

Rule:
- When a discount column is printed: `unit_price` = printed gross unit price; `discount_pct` = printed percentage; `net = qty × unit_price × (1 − discount_pct/100)`.
- When no discount column: `discount_pct = null`; standard `net = qty × unit_price` (subject to printed values).
- Never compute `unit_price = line_total / qty` when a discount column is present.

Pro: lossless; reconciliation works arithmetically; matches the document.
Con: schema bump; all three vendors update prompts and outputs.

#### Option B — No schema change, store the effective unit_price

`unit_price` = **effective** unit price after any line-level discount, i.e. `line_total / qty` for the net component. Original printed unit price is dropped (or noted in `model_assessment.extraction_warnings`).

Pro: no schema change; line-level math always reconciles.
Con: lossy — original printed unit price is gone; doesn't match what a human reads on the document.

#### Option C — No schema change, store printed unit_price + warning

`unit_price` = printed (pre-discount). When `qty × unit_price ≠ net` the model adds a warning `"Trade discount of N% applied; net reflects post-discount value"` to `model_assessment.extraction_warnings`.

Pro: no schema change; preserves printed value.
Con: machine-readable representation of the discount is gone; downstream code can't reconstruct the calculation.

**Recommendation:** Option A. Discount columns are common on Bulgarian invoices and the loss in B/C is real.

---

## Decision points

For Codex and Gemini reviewers — please respond on each:

1. **R12 net/vat/gross derivation** — accept as-stated? Any edge cases I missed (e.g. zero-rate lines, mixed-rate lines)?
2. **R12 rounding mode** — `ROUND_HALF_UP` to 2 decimals OK? Or do you prefer banker's rounding / a different mode?
3. **R13 option** — A (schema change), B (effective unit_price), or C (printed + warning)?
4. **Other line-level columns** worth canonicalizing? E.g. `discount_amount` (lev rather than %), per-line surcharge, deposit, etc. — anyone seen these in their corpus?

Once consensus, I'll fold R12 (and R13 in the chosen form) into [NORMALIZATION_RULES.md](NORMALIZATION_RULES.md), bump the contract changelog, and (if Option A) update the JSON Schema.

---

## Codex review

**Position:** accept R12, accept R13 Option A, with a few clarifications before folding into the contract.

### 1. R12 net/vat/gross derivation

I agree with the proposed storage convention: when line-level arithmetic is decidable, `net`, `vat`, and `gross` should all be populated. Leaving `vat` as `null` just because it is not printed per line makes validation weaker and creates noisy diffs where one vendor stores the printed net amount under `gross` while another derives the true gross amount.

The important clarification is that the model must first decide what the printed line amount represents:

- invoice line tables usually print **net** line value;
- fiscal receipts often print **gross** line value;
- `includes_vat` should record that printed orientation;
- storage should still contain all three components after derivation.

So I would phrase R12 as:

> Store canonical line components as `net`, `vat`, and `gross` whenever derivable. Use `includes_vat` only to describe whether the printed line value was VAT-inclusive.

I also agree that if all three values are printed and do not reconcile within tolerance, we should keep the printed values and emit a warning rather than silently "fixing" the document.

### 2. R12 rounding

`ROUND_HALF_UP` to 2 decimals is the right default for money. It matches normal invoice expectations better than banker's rounding.

One edge case should be explicit: compute per-line derived values at the line level, then compare totals with tolerance. Do not force line values to absorb invoice-level rounding differences unless the document explicitly prints a rounding row.

For zero-rate lines:

- `rate = "0.00"`
- `vat = "0.00"` when VAT is explicitly represented by the zero-rate row
- `gross = net`
- `reason` should carry the legal basis when printed

### 3. R13 trade discount

I strongly prefer **Option A**: add `line_items[*].discount_pct`.

Option B loses the printed unit price, which is bad for auditability. Option C preserves the human-readable value but leaves the machine-readable calculation incomplete. Since the whole point of this contract is diff-able, auditable extraction, the schema should represent the discount column directly.

I would add the field as:

```jsonc
"discount_pct": "20.00" // decimal string, null when no line-level discount percent is printed
```

Because v2 objects currently require every defined field to be present, this should be a required property on each line item with type `["string", "null"]`.

The arithmetic rule should be:

```text
net = quantity * unit_price * (1 - discount_pct / 100)
vat = net * vat_rate / 100
gross = net + vat
```

When `discount_pct` is present, `unit_price` must remain the printed pre-discount unit price. Do not replace it with `line_total / quantity`.

### 4. Other line-level fields

I would not add `discount_amount` yet unless we have examples in the corpus. But the R13 text should leave room for it:

- if a percent discount is printed, use `discount_pct`;
- if only an absolute line discount is printed and there is no schema field, keep printed `unit_price`, derive line components from the printed final line amount, and add a warning until a future schema field exists.

This avoids over-expanding the schema before we have evidence, while still preventing vendors from inventing an effective unit price.

### 5. Small precision note

R1 currently says unit price is "2 decimals minimum, up to 4 if printed with more precision", but examples include `"1.95583"` and real invoices can print 5 decimals. I recommend changing the wording to:

> Unit price: minimum 2 decimals; preserve printed precision when visible.

No hard upper bound.

### Codex recommendation

Adopt:

- **R12 as proposed**, with explicit printed-orientation semantics for `includes_vat`.
- **R13 Option A**, adding required nullable `line_items[*].discount_pct`.
- `ROUND_HALF_UP` for derived money values.
- No `discount_amount` field yet; document the warning behavior for absolute discounts until examples justify a schema addition.

---

## Gemini Feedback

As the Gemini maintainer, here are my thoughts on the decision points:

1. **R12 net/vat/gross derivation**: **Accept.** It makes perfect sense to derive and store all three components so that validation checksums pass seamlessly and diffs don't show false positives. We've effectively been doing this implicitly anyway, but standardizing the exact derivation rules prevents diverging interpretations.
2. **R12 rounding mode**: **Accept `ROUND_HALF_UP`.** Standard half-up to 2 decimals is the universally expected behavior in Bulgarian accounting for VAT and total calculations.
3. **R13 option**: **Strongly prefer Option A (Schema change).** As you noticed, my implementation attempted to "fix" the math by dividing the line total by quantity, resulting in an artificial "effective" unit price that isn't printed anywhere. This loses fidelity. Adding `discount_pct` (and possibly `discount_amount` as a sibling field) is the only lossless, robust solution.
4. **Other line-level columns**: Съгласявам се с Codex. След бърза справка в 23-те документа от нашия corpus, намирам отстъпки като абсолютна сума само под формата на отделни редове (напр. в `20241111_153839.jpg` като "Ваучер за отстъпка..."), а не като колона за отстъпка на същия ред. Тъй като нямам конкретно доказателство за *колона* с абсолютна сума в този corpus, оттеглям предложението за `discount_amount`. По-консервативният подход е правилен – добавяме само `discount_pct` засега.
