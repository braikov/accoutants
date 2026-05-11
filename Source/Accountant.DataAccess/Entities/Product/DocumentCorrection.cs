using Accountant.Identity.Models;

namespace Accountant.DataAccess.Entities.Product;

/// User-edited version of a Document's extracted JSON. Append-only —
/// each `Save` from the editor creates a new row. Latest row by
/// `EditedAtUtc` is the canonical "current state" for downloads.
public class DocumentCorrection
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int EditedByUserId { get; set; }
    public ApplicationUser EditedByUser { get; set; } = null!;

    public DateTime EditedAtUtc { get; set; }

    /// Full corrected ExtractionResult JSON (same v2 schema as the
    /// extraction JSON). LONGTEXT in MySQL.
    public required string CorrectedJson { get; set; }
}
