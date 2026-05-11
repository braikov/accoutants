using Accountant.Identity.Models;

namespace Accountant.DataAccess.Entities.Product;

public enum DocumentStatus
{
    /// File uploaded; not yet queued for extraction.
    Uploaded = 1,

    /// In Hangfire queue, awaiting a worker.
    Queued = 2,

    /// Worker has picked it up.
    Processing = 3,

    /// Extraction succeeded; `DocumentExtraction` row exists.
    Extracted = 4,

    /// Extraction failed after retries; see `DocumentExtraction.FailureReason`.
    Failed = 5
}

/// One uploaded invoice / receipt / etc. File bytes live behind
/// `IFileStore` keyed by `StorageKey`. Lifecycle:
/// `Uploaded → Queued → Processing → Extracted | Failed`.
public class Document
{
    public int Id { get; set; }

    public int TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    /// Null = at the tenant root.
    public int? FolderId { get; set; }
    public Folder? Folder { get; set; }

    public int UploadedByUserId { get; set; }
    public ApplicationUser UploadedByUser { get; set; } = null!;

    /// Filename at upload time (display only — not used as identity).
    public required string OriginalFileName { get; set; }

    public required string ContentType { get; set; }

    public long ByteSize { get; set; }

    /// Opaque key consumed by `IFileStore`. Format is impl-specific.
    public required string StorageKey { get; set; }

    /// Thumbnail key (separate IFileStore object) — null until generated
    /// by the post-upload thumbnail job.
    public string? ThumbnailKey { get; set; }

    public DocumentStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }

    public DocumentExtraction? Extraction { get; set; }
    public List<DocumentCorrection> Corrections { get; set; } = new();
}
