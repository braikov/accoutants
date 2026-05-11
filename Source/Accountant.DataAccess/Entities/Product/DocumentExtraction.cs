namespace Accountant.DataAccess.Entities.Product;

public enum DocumentExtractionStatus
{
    Success = 1,
    Failed = 2
}

/// The AI run for a Document. One per Document in MVP (re-extraction is
/// v1.1). Stores the vendor / model / prompt snapshot, timing, token
/// counts, cost estimate, and the JSON result (or failure reason).
public class DocumentExtraction
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    /// Vendor identifier — wire string ("claude" / "codex" / "gemini").
    public required string Vendor { get; set; }

    /// Concrete model name as reported by the vendor SDK
    /// (e.g. "claude-sonnet-4.6", "gpt-5.4-mini", "gemini-3-flash").
    public required string ModelName { get; set; }

    /// Snapshot of our prompt content version. Wire to git commit / file
    /// hash so a future re-run with a different prompt is identifiable.
    public required string PromptVersion { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    /// Milliseconds from StartedAtUtc to CompletedAtUtc. Computed on save
    /// for query speed.
    public int? LatencyMs { get; set; }

    public int TokensIn { get; set; }
    public int TokensOut { get; set; }

    /// Cost estimate in USD as of the run time. Uses the price table
    /// from app config (per-model rate * tokens). Stored so reports
    /// remain stable even when the price table changes.
    public decimal EstimatedCostUsd { get; set; }

    public DocumentExtractionStatus Status { get; set; }

    /// Populated only on Status=Failed. Free-form text (exception message,
    /// vendor error code, etc.). Max 2000 chars.
    public string? FailureReason { get; set; }

    /// Serialized ExtractionResult by Unified_Extraction_Contract v2 schema.
    /// Stored as LONGTEXT (MySQL). Null when Status=Failed.
    public string? JsonResult { get; set; }
}
