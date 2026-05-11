namespace Accountant.DataAccess.Entities;

/// One source image. Rows are unique by FileHash so re-uploading the same image
/// (with a different filename) reuses the row. FileName captures the most recent
/// filename it appeared under for display.
public class SourceDocument
{
    public int Id { get; set; }

    /// SHA-256 of the file bytes — stable identity across renames.
    public required string FileHash { get; set; }

    /// Most recent filename observed for this image (e.g. "20240213_190514.jpg").
    public required string FileName { get; set; }

    public long FileSizeBytes { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    public DateTime FirstSeenAtUtc { get; set; }

    public List<Extraction> Extractions { get; set; } = new();
    public GroundTruth? GroundTruth { get; set; }
}
