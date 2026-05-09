namespace Accountant.Contracts;

/// Coerces null nested objects/lists in an ExtractionResult into their schema-required
/// default shapes. Same intent as ModelInputSanitizer but operates on the full v2 document
/// (used to fix already-stored JSON files post-hoc, without re-calling any vendor).
public static class ResultSanitizer
{
    public static ExtractionResult Sanitize(ExtractionResult input) => input with
    {
        Source = SanitizeSource(input.Source),
        Extraction = SanitizeExtraction(input.Extraction),
        Validation = SanitizeValidation(input.Validation),
        ModelAssessment = SanitizeAssessment(input.ModelAssessment),
        Evidence = input.Evidence ?? new Dictionary<string, EvidenceItem>(),
    };

    private static Source SanitizeSource(Source? source)
    {
        var s = source ?? new Source { FileName = "", FilePath = "" };
        return s with
        {
            ImageQuality = NullCoalesce(s.ImageQuality, () => new ImageQuality()) with
            {
                Issues = NullCoalesce(s.ImageQuality?.Issues, () => Array.Empty<string>()),
            },
        };
    }

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
            LineItems = NullCoalesce(e.LineItems, () => Array.Empty<LineItem>()),
        };
    }

    private static Validation SanitizeValidation(Validation? source)
    {
        var v = source ?? new Validation();
        return v with
        {
            Checks = NullCoalesce(v.Checks, () => Array.Empty<Check>()),
            Errors = NullCoalesce(v.Errors, () => Array.Empty<string>()),
            Warnings = NullCoalesce(v.Warnings, () => Array.Empty<string>()),
        };
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

    private static T NullCoalesce<T>(T? value, Func<T> fallback) where T : class =>
        value ?? fallback();
}
