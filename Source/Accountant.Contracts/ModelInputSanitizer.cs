namespace Accountant.Contracts;

/// Coerces null nested objects/lists in a deserialized ModelExtractionInput into
/// their schema-required default shapes (empty object / empty list).
///
/// The v2 contract requires every defined key to be present — `fiscal: null` is invalid;
/// it must be `fiscal: { fiscal_device_number: null, ... }`. Models occasionally collapse
/// "no data" sub-objects to a literal null. This sanitizer fixes that uniformly across
/// all vendor extractors so downstream code (validator, serializer, diff) sees a
/// stable shape.
public static class ModelInputSanitizer
{
    public static ModelExtractionInput Sanitize(ModelExtractionInput input) => input with
    {
        Extraction = SanitizeExtraction(input.Extraction),
        ModelAssessment = SanitizeAssessment(input.ModelAssessment),
        Evidence = input.Evidence ?? new Dictionary<string, EvidenceItem>(),
        ImageQuality = SanitizeImageQuality(input.ImageQuality),
    };

    private static Extraction SanitizeExtraction(Extraction? source)
    {
        var e = source ?? new Extraction();
        return e with
        {
            Document = NullCoalesce(e.Document, () => new Document()),
            Supplier = NullCoalesce(e.Supplier, () => new Party()),
            Customer = NullCoalesce(e.Customer, () => new Party()),
            Totals = NullCoalesce(e.Totals, () => new Totals()),
            Fiscal = NullCoalesce(e.Fiscal, () => new Fiscal()),
            VatBreakdown = NullCoalesce(e.VatBreakdown, () => Array.Empty<VatBreakdownItem>()),
            Payments = NullCoalesce(e.Payments, () => Array.Empty<Payment>()),
            LineItems = NullCoalesce(e.LineItems, () => Array.Empty<LineItem>()).Select(SanitizeLineItem).ToArray(),
        };
    }

    /// Per R13 (revised): `discount_pct` is always a decimal string with 2 decimals, never null.
    /// Default `"0.00"` when no discount applies. Covers vendor outputs that emit null,
    /// the literal string `"null"` (a known Codex bug), empty strings, or whitespace.
    private static LineItem SanitizeLineItem(LineItem li) =>
        li with { DiscountPct = NormaliseDiscountPct(li.DiscountPct) };

    private static string NormaliseDiscountPct(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "0.00";
        var trimmed = value.Trim();
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)) return "0.00";
        return trimmed;
    }

    private static ModelAssessment SanitizeAssessment(ModelAssessment? source)
    {
        var a = source ?? new ModelAssessment();
        return a with
        {
            Confidence = NullCoalesce(a.Confidence, () => new ConfidenceMap()),
            ExtractionWarnings = NullCoalesce(a.ExtractionWarnings, () => Array.Empty<string>()),
        };
    }

    private static ImageQuality SanitizeImageQuality(ImageQuality? source)
    {
        var q = source ?? new ImageQuality();
        return q with
        {
            Issues = NullCoalesce(q.Issues, () => Array.Empty<string>()),
        };
    }

    /// `??` warns "left operand never null" against non-nullable types, but JSON
    /// deserialization can still produce null at runtime. Wrap the runtime check
    /// in a helper to silence the analyser without disabling the warning globally.
    private static T NullCoalesce<T>(T? value, Func<T> fallback) where T : class =>
        value ?? fallback();
}
