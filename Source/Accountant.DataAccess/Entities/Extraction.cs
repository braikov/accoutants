namespace Accountant.DataAccess.Entities;

/// One extraction run for one (SourceDocument, vendor, model, prompt-version, timestamp) tuple.
/// Multiple Extractions per document are normal — that's the whole point of A/B comparison.
public class Extraction
{
    public long Id { get; set; }

    public int SourceDocumentId { get; set; }
    public SourceDocument SourceDocument { get; set; } = null!;

    /// "Claude" / "Codex" / "Gemini" / future vendors. Filename prefix in disk era.
    public required string Vendor { get; set; }

    /// Actual model name returned by the API (may differ from what we requested
    /// when an alias auto-routes — e.g. `gpt-5.4-mini` resolving to a snapshot,
    /// or `gemini-flash-latest` resolving to `gemini-3-flash-preview`).
    public required string Model { get; set; }

    /// Prompt version constant from the vendor's `<Vendor>Prompt.PromptVersion`.
    /// Drives prompt-version A/B analytics.
    public string? PromptVersion { get; set; }

    /// `vision_direct` / `ocr_then_llm` / `hybrid` — see contract.
    public required string Pipeline { get; set; }

    public bool OcrUsed { get; set; }

    /// e.g. `accountant.document.v2`. Lets us evolve the schema without dropping history.
    public required string SchemaVersion { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public int? DurationMs { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }

    /// Cost as decimal string (matches contract). Stored at write time using
    /// the rate that was current then; older rows reflect older rates.
    public string? CostEstimateUsd { get; set; }

    public string? StopReason { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    // Denormalised flags pulled from the validation/extraction blocks for fast querying.
    public bool ValidationNeedsReview { get; set; }
    public int ValidationFailCount { get; set; }
    public int ValidationWarnCount { get; set; }
    public string? DocumentType { get; set; }

    /// Full ExtractionResult serialised as JSON (snake_case per ExtractionJson.Default).
    /// Stored as a JSON column on MySQL/MariaDB; pretty-printable on retrieval.
    public required string ResultJson { get; set; }
}
