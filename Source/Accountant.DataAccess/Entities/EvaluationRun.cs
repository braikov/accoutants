namespace Accountant.DataAccess.Entities;

/// One evaluation snapshot — typically the result of running `evaluate`.
/// Holds per-document, per-vendor scores so we can compare prompt versions over time.
public class EvaluationRun
{
    public int Id { get; set; }

    public DateTime RunAtUtc { get; set; }

    /// Free-form note from the operator: "after R12 anti-rule fix", "before discount_pct migration", etc.
    public string? Notes { get; set; }

    public List<EvaluationDocument> Documents { get; set; } = new();
}

public class EvaluationDocument
{
    public long Id { get; set; }

    public int EvaluationRunId { get; set; }
    public EvaluationRun EvaluationRun { get; set; } = null!;

    public int SourceDocumentId { get; set; }
    public SourceDocument SourceDocument { get; set; } = null!;

    /// Which Extraction was scored (null if no extraction existed at evaluate time).
    public long? ExtractionId { get; set; }
    public Extraction? Extraction { get; set; }

    public required string Vendor { get; set; }
    public string? PromptVersion { get; set; }
    public string? Model { get; set; }

    public int MatchCount { get; set; }
    public int MismatchCount { get; set; }

    public int CriticalMatchCount { get; set; }
    public int CriticalMismatchCount { get; set; }

    public int MoneyMatchCount { get; set; }
    public int MoneyMismatchCount { get; set; }

    /// Per-field mismatch detail as JSON (path, expected, actual, note tuples).
    public required string MismatchesJson { get; set; }
}
