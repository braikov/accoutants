using System.Globalization;
using System.Text.RegularExpressions;
using Accountant.Contracts;

namespace Accountant.Contracts.Validators;

/// Deterministic v2 validator. Same input -> same output for any vendor.
/// Mirrors Python Claude/src/validate.py — both implementations must stay in sync.
public static class ExtractionValidator
{
    private const decimal ToleranceTotals = 0.02m;
    private const decimal ToleranceLineItems = 0.05m;
    private const decimal ToleranceVatRate = 0.05m;
    private const double ConfidenceReviewThreshold = 0.7;

    private static readonly HashSet<string> ReviewWarningCodes =
    [
        "multiple_documents_detected",
        "document_partially_cropped",
        "line_items_incomplete",
        "ocr_text_conflict",
        "image_quality_issue",
    ];

    private static readonly Regex VatRe = new(@"^[A-Z]{2}\d{8,12}$", RegexOptions.Compiled);
    private static readonly Regex BicRe = new(@"^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.Compiled);
    private static readonly Regex CurrencyRe = new(@"^[A-Z]{3}$", RegexOptions.Compiled);
    private static readonly Regex IsoDateRe = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public static Validation Validate(Extraction extraction, Source source, ConfidenceMap? confidence = null)
    {
        var checks = new List<Check>();

        // Required fields
        AddIfPresent(checks, MissingField(extraction.Document.Number, "missing_document_number"));
        AddIfPresent(checks, MissingField(extraction.Document.Date, "missing_document_date"));
        AddIfPresent(checks, MissingField(extraction.Supplier.Name, "missing_supplier_name"));
        AddIfPresent(checks, MissingField(extraction.Supplier.Eik, "missing_supplier_eik"));
        AddIfPresent(checks, MissingField(extraction.Customer.Name, "missing_customer_name"));

        // Format & checksum
        checks.Add(CheckEik(extraction.Supplier.Eik, "supplier_eik_checksum"));
        checks.Add(CheckEik(extraction.Customer.Eik, "customer_eik_checksum"));
        checks.Add(CheckVatFormat(extraction.Supplier.VatNumber, "supplier_vat_format"));
        checks.Add(CheckVatFormat(extraction.Customer.VatNumber, "customer_vat_format"));

        // IBAN/BIC: first non-null in payments
        var firstIban = extraction.Payments.Select(p => p.Iban).FirstOrDefault(i => !string.IsNullOrWhiteSpace(i));
        var firstBic = extraction.Payments.Select(p => p.Bic).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b));
        checks.Add(CheckIban(firstIban));
        checks.Add(CheckBic(firstBic));

        // Document-level
        checks.Add(CheckCurrency(extraction.Document.Currency));
        checks.Add(CheckDocType(extraction.DocumentType));
        checks.Add(CheckDateFormat(extraction));
        checks.Add(CheckDueAfterIssue(extraction));

        // Arithmetic
        checks.Add(CheckTotalsMatch(extraction));
        checks.Add(CheckVatBreakdown(extraction));
        var (lineSum, supplemental) = CheckLineItemsSum(extraction);
        checks.Add(lineSum);
        checks.AddRange(supplemental);

        // Source-derived
        AddIfPresent(checks, CheckMultipleDocuments(source));
        AddIfPresent(checks, CheckImageQuality(source));

        // Review trigger
        var needsReview = false;
        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var c in checks)
        {
            switch (c.Status)
            {
                case CheckStatus.Fail:
                    errors.Add(c.Code);
                    needsReview = true;
                    break;
                case CheckStatus.Warning:
                    warnings.Add(c.Code);
                    if (ReviewWarningCodes.Contains(c.Code)) needsReview = true;
                    break;
            }
        }

        if (confidence is not null)
        {
            double?[] values = [confidence.Overall, confidence.Document, confidence.Supplier,
                                confidence.Customer, confidence.Totals, confidence.LineItems];
            if (values.Any(v => v.HasValue && v.Value < ConfidenceReviewThreshold))
                needsReview = true;
        }

        return new Validation
        {
            NeedsReview = needsReview,
            Checks = checks,
            Errors = errors,
            Warnings = warnings,
        };
    }

    private static void AddIfPresent(List<Check> checks, Check? c)
    {
        if (c is not null) checks.Add(c);
    }

    private static Check Make(string code, CheckStatus status, string message = "") =>
        new() { Code = code, Status = status, Message = message };

    // ===== Field presence =====

    private static Check? MissingField(object? value, string code)
    {
        if (value is null) return Make(code, CheckStatus.Fail, $"{code["missing_".Length..]} is missing");
        if (value is string s && string.IsNullOrWhiteSpace(s)) return Make(code, CheckStatus.Fail, $"{code["missing_".Length..]} is missing");
        return null;
    }

    // ===== EIK (Bulgarian unified ID) =====

    private static bool? EikChecksumValid(string eik)
    {
        var s = eik.Trim().ToUpperInvariant();
        if (s.StartsWith("BG", StringComparison.Ordinal)) s = s[2..].Trim();
        if (s.Length is not (9 or 13) || !s.All(char.IsDigit)) return null;

        var digits = s.Select(c => c - '0').ToArray();

        int[] w1 = [1, 2, 3, 4, 5, 6, 7, 8];
        var total = w1.Zip(digits.Take(8), (w, x) => w * x).Sum();
        var r = total % 11;
        if (r == 10)
        {
            int[] w2 = [3, 4, 5, 6, 7, 8, 9, 10];
            var total2 = w2.Zip(digits.Take(8), (w, x) => w * x).Sum();
            var r2 = total2 % 11;
            r = r2 == 10 ? 0 : r2;
        }
        if (r != digits[8]) return false;

        if (digits.Length == 13)
        {
            int[] u1 = [2, 7, 3, 5];
            var t = u1.Zip(digits.Skip(8).Take(4), (a, b) => a * b).Sum();
            var rr = t % 11;
            if (rr >= 10)
            {
                int[] u2 = [4, 9, 5, 7];
                var t2 = u2.Zip(digits.Skip(8).Take(4), (a, b) => a * b).Sum();
                var rr2 = t2 % 11;
                rr = rr2 == 10 ? 0 : rr2;
            }
            if (rr != digits[12]) return false;
        }
        return true;
    }

    private static Check CheckEik(string? eik, string code)
    {
        if (string.IsNullOrWhiteSpace(eik)) return Make(code, CheckStatus.Skipped, "EIK is null");
        var v = EikChecksumValid(eik);
        if (v is null) return Make(code, CheckStatus.Fail, $"EIK '{eik}' is malformed (not 9 or 13 digits).");
        if (v == false) return Make(code, CheckStatus.Fail, $"EIK '{eik}' fails Bulgarian checksum.");
        return Make(code, CheckStatus.Pass);
    }

    // ===== VAT format =====

    private static Check CheckVatFormat(string? vat, string code)
    {
        if (string.IsNullOrWhiteSpace(vat)) return Make(code, CheckStatus.Skipped, "VAT number is null");
        return VatRe.IsMatch(vat)
            ? Make(code, CheckStatus.Pass)
            : Make(code, CheckStatus.Fail, $"VAT '{vat}' doesn't match country-prefix format.");
    }

    // ===== IBAN mod-97 =====

    private static Check CheckIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return Make("iban_mod97", CheckStatus.Skipped, "IBAN is null");
        var s = iban.Replace(" ", "").ToUpperInvariant();
        if (s.Length is < 15 or > 34
            || !char.IsLetter(s[0]) || !char.IsLetter(s[1])
            || !char.IsDigit(s[2]) || !char.IsDigit(s[3]))
            return Make("iban_mod97", CheckStatus.Fail, $"IBAN '{iban}' has malformed structure.");

        var rearranged = s[4..] + s[..4];
        var sb = new System.Text.StringBuilder(rearranged.Length * 2);
        foreach (var c in rearranged)
        {
            if (char.IsDigit(c)) sb.Append(c);
            else if (char.IsLetter(c)) sb.Append((c - 55).ToString(CultureInfo.InvariantCulture));
            else return Make("iban_mod97", CheckStatus.Fail, $"IBAN '{iban}' contains invalid character '{c}'.");
        }

        // Mod-97 over arbitrary-length integer via streaming reduction.
        var mod = 0;
        foreach (var ch in sb.ToString())
        {
            mod = (mod * 10 + (ch - '0')) % 97;
        }
        return mod == 1
            ? Make("iban_mod97", CheckStatus.Pass)
            : Make("iban_mod97", CheckStatus.Fail, $"IBAN '{iban}' fails mod-97 checksum.");
    }

    // ===== BIC =====

    private static Check CheckBic(string? bic)
    {
        if (string.IsNullOrWhiteSpace(bic)) return Make("bic_format", CheckStatus.Skipped, "BIC is null");
        return BicRe.IsMatch(bic)
            ? Make("bic_format", CheckStatus.Pass)
            : Make("bic_format", CheckStatus.Fail, $"BIC '{bic}' doesn't match expected format.");
    }

    // ===== Currency =====

    private static Check CheckCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return Make("currency_iso", CheckStatus.Skipped, "currency is null");
        return CurrencyRe.IsMatch(currency)
            ? Make("currency_iso", CheckStatus.Pass)
            : Make("currency_iso", CheckStatus.Fail, $"currency '{currency}' is not ISO 4217.");
    }

    // ===== Document type enum =====

    private static Check CheckDocType(DocumentType dt) =>
        Enum.IsDefined(dt) ? Make("document_type_enum", CheckStatus.Pass)
                           : Make("document_type_enum", CheckStatus.Fail, $"'{dt}' is not a valid document_type.");

    // ===== Dates =====

    private static DateOnly? ParseIso(string? s) =>
        s is not null && IsoDateRe.IsMatch(s) && DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : null;

    private static Check CheckDateFormat(Extraction e)
    {
        var fields = new (string name, string? val)[]
        {
            ("date", e.Document.Date),
            ("tax_event_date", e.Document.TaxEventDate),
            ("due_date", e.Document.DueDate),
        };
        var anyPresent = false;
        var bad = new List<string>();
        foreach (var (name, val) in fields)
        {
            if (val is null) continue;
            anyPresent = true;
            if (!IsoDateRe.IsMatch(val)) bad.Add($"{name}='{val}'");
        }
        if (!anyPresent) return Make("date_format", CheckStatus.Skipped, "no dates present");
        return bad.Count == 0
            ? Make("date_format", CheckStatus.Pass)
            : Make("date_format", CheckStatus.Fail, $"non-ISO dates: {string.Join(", ", bad)}");
    }

    private static Check CheckDueAfterIssue(Extraction e)
    {
        var issue = ParseIso(e.Document.Date);
        var due = ParseIso(e.Document.DueDate);
        if (issue is null || due is null)
            return Make("due_date_after_issue", CheckStatus.Skipped, "issue or due date missing/malformed");
        return due >= issue
            ? Make("due_date_after_issue", CheckStatus.Pass)
            : Make("due_date_after_issue", CheckStatus.Fail, $"due_date {due} is before issue date {issue}.");
    }

    // ===== Totals =====

    private static decimal? ToDecimal(string? s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static Check CheckTotalsMatch(Extraction e)
    {
        var net = ToDecimal(e.Totals.Net);
        var vat = ToDecimal(e.Totals.Vat);
        var gross = ToDecimal(e.Totals.Gross);
        if (net is null || vat is null || gross is null)
            return Make("totals_match", CheckStatus.Skipped, "net/vat/gross not all populated");
        var expected = net.Value + vat.Value;
        return Math.Abs(expected - gross.Value) <= ToleranceTotals
            ? Make("totals_match", CheckStatus.Pass, $"{net} + {vat} = {gross}")
            : Make("totals_match", CheckStatus.Fail, $"net ({net}) + vat ({vat}) = {expected}, but gross = {gross}.");
    }

    private static Check CheckVatBreakdown(Extraction e)
    {
        if (e.VatBreakdown.Count == 0)
            return Make("vat_breakdown_match", CheckStatus.Skipped, "vat_breakdown is empty");

        var bad = new List<string>();
        for (var i = 0; i < e.VatBreakdown.Count; i++)
        {
            var item = e.VatBreakdown[i];
            var rate = ToDecimal(item.Rate);
            var net = ToDecimal(item.Net);
            var vat = ToDecimal(item.Vat);
            var gross = ToDecimal(item.Gross);

            if (net is not null && vat is not null && gross is not null
                && Math.Abs(net.Value + vat.Value - gross.Value) > ToleranceTotals)
                bad.Add($"row {i}: {net}+{vat}≠{gross}");

            if (rate is not null && net is not null && vat is not null)
            {
                var expectedVat = Math.Round(net.Value * rate.Value / 100m, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(expectedVat - vat.Value) > ToleranceVatRate)
                    bad.Add($"row {i}: {net}×{rate}%={expectedVat}, vat={vat}");
            }
        }
        return bad.Count == 0
            ? Make("vat_breakdown_match", CheckStatus.Pass)
            : Make("vat_breakdown_match", CheckStatus.Fail, string.Join("; ", bad));
    }

    private static (Check sumCheck, List<Check> supplemental) CheckLineItemsSum(Extraction e)
    {
        if (e.LineItems.Count == 0)
            return (Make("line_items_sum", CheckStatus.Skipped, "no line items"), []);

        var components = new[] { "net", "vat", "gross" };
        var sums = components.ToDictionary(c => c, _ => (decimal?)0m);
        var incomplete = components.ToDictionary(c => c, _ => false);

        foreach (var li in e.LineItems)
        {
            foreach (var c in components)
            {
                var v = c switch
                {
                    "net" => ToDecimal(li.Net),
                    "vat" => ToDecimal(li.Vat),
                    "gross" => ToDecimal(li.Gross),
                    _ => null,
                };
                if (v is null)
                {
                    incomplete[c] = true;
                    sums[c] = null;
                }
                else if (sums[c] is not null)
                {
                    sums[c] = sums[c]!.Value + v.Value;
                }
            }
        }

        var mismatches = new List<string>();
        foreach (var c in components)
        {
            var totalVal = c switch
            {
                "net" => ToDecimal(e.Totals.Net),
                "vat" => ToDecimal(e.Totals.Vat),
                "gross" => ToDecimal(e.Totals.Gross),
                _ => null,
            };
            if (sums[c] is null || totalVal is null) continue;
            if (Math.Abs(sums[c]!.Value - totalVal.Value) > ToleranceLineItems)
                mismatches.Add($"sum({c})={sums[c]}, totals.{c}={totalVal}");
        }

        var supplemental = new List<Check>();
        if (incomplete.Values.Any(v => v))
        {
            var missing = incomplete.Where(kv => kv.Value).Select(kv => kv.Key);
            supplemental.Add(Make("line_items_incomplete", CheckStatus.Warning,
                $"line items missing component(s): {string.Join(", ", missing)}"));
        }

        return mismatches.Count == 0
            ? (Make("line_items_sum", CheckStatus.Pass, "all populated component sums match totals"), supplemental)
            : (Make("line_items_sum", CheckStatus.Fail, string.Join("; ", mismatches)), supplemental);
    }

    // ===== Source-derived =====

    private static Check? CheckMultipleDocuments(Source source)
    {
        var count = source.DetectedDocumentCount ?? 1;
        if (count <= 1) return null;
        return Make("multiple_documents_detected", CheckStatus.Warning,
            $"detected {count} documents; only index {source.ExtractedDocumentIndex ?? 0} extracted.");
    }

    private static Check? CheckImageQuality(Source source)
    {
        var rb = source.ImageQuality.Readability;
        if (rb is Readability.Poor or Readability.Unreadable)
            return Make("image_quality_issue", CheckStatus.Warning, $"readability={rb.ToString()!.ToLowerInvariant()}");
        if (source.ImageQuality.Issues.Count > 0)
            return Make("image_quality_issue", CheckStatus.Warning,
                $"issues: {string.Join(", ", source.ImageQuality.Issues)}");
        return null;
    }
}
