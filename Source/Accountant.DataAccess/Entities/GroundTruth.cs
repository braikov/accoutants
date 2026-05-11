namespace Accountant.DataAccess.Entities;

/// Human-verified canonical extraction for a SourceDocument. One per document.
/// Used as the comparison baseline by the evaluator.
public class GroundTruth
{
    public int Id { get; set; }

    public int SourceDocumentId { get; set; }
    public SourceDocument SourceDocument { get; set; } = null!;

    /// The `extraction` block of the v2 contract (no source/validation/provider — those
    /// are vendor-specific and not in scope for ground truth).
    public required string ExtractionJson { get; set; }

    public DateTime LastEditedAtUtc { get; set; }

    /// Free-form identifier (user name / system label) of who last edited.
    /// Will populate from authenticated user context once Identity is wired.
    public string? LastEditedBy { get; set; }
}
