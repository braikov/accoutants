namespace Accountant.Contracts;

/// One canonical v2 extraction document — top-level shape per Unified_Extraction_Contract.
public sealed record ExtractionResult
{
    public string SchemaVersion { get; init; } = "accountant.document.v2";
    public required Source Source { get; init; }
    public required Extraction Extraction { get; init; }
    public Validation Validation { get; init; } = new();
    public ModelAssessment ModelAssessment { get; init; } = new();
    public IReadOnlyDictionary<string, EvidenceItem> Evidence { get; init; } =
        new Dictionary<string, EvidenceItem>();
    public required Provider Provider { get; init; }
}

/// Subset of ExtractionResult that the model produces via tool_use.
/// The harness adds Source.File*, Validation, and Provider after the call.
public sealed record ModelExtractionInput
{
    public required Extraction Extraction { get; init; }
    public ModelAssessment ModelAssessment { get; init; } = new();
    public IReadOnlyDictionary<string, EvidenceItem> Evidence { get; init; } =
        new Dictionary<string, EvidenceItem>();
    public ImageQuality ImageQuality { get; init; } = new();
    public int? DetectedDocumentCount { get; init; } = 1;
    public int? ExtractedDocumentIndex { get; init; } = 0;
}
