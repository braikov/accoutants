# Accountant Extraction Contract v1

This contract defines the canonical output format for extracting structured accounting data from payment document images. All providers and agents should export this shape so results can be compared fairly across OpenAI, Claude, Gemini, OCR, and hybrid pipelines.

The canonical output is one JSON file per source document.

```text
runs/
  openai/
    20240213_190514.json
  claude/
    20240213_190514.json
  gemini/
    20240213_190514.json
```

## Top-Level Shape

```json
{
  "schema_version": "accountant.document.v1",
  "source": {},
  "extraction": {},
  "validation": {},
  "evidence": {},
  "provider": {}
}
```

## Canonical JSON Example

```json
{
  "schema_version": "accountant.document.v1",
  "source": {
    "file_name": "20240213_190514.jpg",
    "file_path": "docs/facturi/20240213_190514.jpg",
    "page_count": 1,
    "page_index": 0,
    "image_quality": {
      "readability": "good",
      "issues": ["slightly_blurry"]
    }
  },
  "extraction": {
    "document_type": "invoice",
    "language": "bg",
    "country": "BG",
    "document": {
      "number": "0300051648",
      "date": "2023-12-11",
      "due_date": null,
      "currency": "BGN",
      "place": null
    },
    "supplier": {
      "name": "АВАНС ТРЕЙД ООД",
      "eik": "123456789",
      "vat_number": "BG123456789",
      "address": null,
      "city": null,
      "country": "BG"
    },
    "customer": {
      "name": null,
      "eik": null,
      "vat_number": null,
      "address": null,
      "city": null,
      "country": "BG"
    },
    "totals": {
      "net": "100.00",
      "vat": "20.00",
      "gross": "120.00",
      "discount": null,
      "rounding": null,
      "amount_due": "120.00"
    },
    "vat_breakdown": [
      {
        "rate": "20.00",
        "net": "100.00",
        "vat": "20.00",
        "gross": "120.00"
      }
    ],
    "payments": [
      {
        "method": "bank_transfer",
        "amount": "120.00",
        "currency": "BGN",
        "iban": "BG00AAAA00000000000000",
        "bic": null,
        "bank_name": null
      }
    ],
    "line_items": [
      {
        "description": "Стока / услуга",
        "quantity": "1.000",
        "unit": "бр.",
        "unit_price": "100.00",
        "vat_rate": "20.00",
        "net": "100.00",
        "vat": "20.00",
        "gross": "120.00"
      }
    ],
    "fiscal": {
      "fiscal_receipt_number": null,
      "fiscal_device_number": null,
      "operator": null,
      "qr_code": null
    }
  },
  "validation": {
    "is_valid_json": true,
    "needs_review": true,
    "confidence": {
      "overall": 0.82,
      "document": 0.95,
      "supplier": 0.88,
      "customer": 0.55,
      "totals": 0.96,
      "line_items": 0.70
    },
    "checks": [
      {
        "code": "totals_match",
        "status": "pass",
        "message": "net + vat equals gross"
      },
      {
        "code": "missing_customer_vat",
        "status": "warning",
        "message": "Customer VAT number is not visible"
      }
    ],
    "errors": [],
    "warnings": ["missing_customer_vat"]
  },
  "evidence": {
    "document.number": {
      "text": "Фактура № 0300051648",
      "confidence": 0.97
    },
    "document.date": {
      "text": "11.12.2023",
      "confidence": 0.94
    },
    "supplier.name": {
      "text": "АВАНС ТРЕЙД ООД",
      "confidence": 0.91
    },
    "totals.gross": {
      "text": "Общо: 120.00 лв.",
      "confidence": 0.98
    }
  },
  "provider": {
    "engine": "openai",
    "model": "gpt-5.4-mini",
    "pipeline": "vision_direct",
    "ocr_used": false,
    "created_at": "2026-05-08T12:55:48+03:00",
    "duration_ms": 6200,
    "cost_estimate_usd": null
  }
}
```

## Required Benchmark Fields

These fields are the minimum comparison surface for provider benchmarking:

- `extraction.document_type`
- `extraction.document.number`
- `extraction.document.date`
- `extraction.document.currency`
- `extraction.supplier.name`
- `extraction.supplier.eik`
- `extraction.supplier.vat_number`
- `extraction.customer.name`
- `extraction.customer.eik`
- `extraction.customer.vat_number`
- `extraction.totals.net`
- `extraction.totals.vat`
- `extraction.totals.gross`
- `validation.needs_review`
- `validation.confidence.overall`

## Conventions

- Use `null` for missing, unreadable, cropped, or uncertain values. Do not invent values.
- Monetary values must be strings, not JSON numbers, for example `"120.00"`.
- Dates should be normalized to ISO `YYYY-MM-DD` when possible.
- Currency should use ISO 4217 codes such as `BGN` or `EUR`.
- Bulgarian VAT numbers should be normalized as `BG` followed by digits when visible.
- `needs_review` must be `true` if important fields are missing, the image quality is weak, totals do not reconcile, or the model relied on weak evidence.
- `evidence` keys should use dot paths matching extracted fields, for example `document.number` or `totals.gross`.

## Recommended Validation Check Codes

Use stable check codes so reports from different providers can be compared:

- `totals_match`
- `vat_breakdown_match`
- `missing_document_number`
- `missing_document_date`
- `missing_supplier_name`
- `missing_supplier_eik`
- `missing_supplier_vat_number`
- `missing_customer_name`
- `invalid_supplier_eik`
- `invalid_supplier_vat_number`
- `invalid_iban`
- `low_confidence_field`
- `image_quality_issue`
- `multiple_documents_detected`
- `document_partially_cropped`
- `line_items_incomplete`
- `ocr_text_conflict`

Each validation check should use:

```json
{
  "code": "totals_match",
  "status": "pass",
  "message": "net + vat equals gross"
}
```

Allowed statuses:

- `pass`
- `warning`
- `fail`
- `skipped`

