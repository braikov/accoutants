using Accountant.Contracts;
using Accountant.DataAccess.Entities.Product;

namespace Accountant.Web.Areas.App.ViewModels;

/// View model for `/App/Documents/Detail/{id}`. Holds the source Document
/// metadata, the latest extraction (vendor / cost / tokens / timing), and the
/// merged data the user should see — which is either the latest `DocumentCorrection`
/// (if one exists) or the raw `DocumentExtraction.JsonResult`.
public sealed class DocumentDetailViewModel
{
    public required int Id { get; init; }
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public long ByteSize { get; init; }
    public DocumentStatus Status { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? ProcessedAtUtc { get; init; }
    public int? FolderId { get; init; }

    /// True when the doc is an image (browser <img>) — false means PDF or other
    /// (browser <iframe> with native viewer).
    public bool IsImage { get; init; }

    /// The latest extraction row — null when the doc was never processed
    /// (Status=Uploaded/Queued/Processing).
    public ExtractionMeta? Extraction { get; init; }

    /// The user-visible extracted data: correction-if-exists, else extraction.
    /// Null when extraction never ran or failed before producing JSON.
    public ExtractionResult? Data { get; init; }

    /// True when `Data` came from `DocumentCorrection`, false when from
    /// `DocumentExtraction.JsonResult`. Shown in the UI as a "Edited by user"
    /// badge.
    public bool IsCorrected { get; init; }

    public DateTime? LastEditedAtUtc { get; init; }

    /// Populated only when Status == Failed and an extraction row exists.
    public string? FailureReason { get; init; }
}

public sealed record ExtractionMeta(
    string Vendor,
    string ModelName,
    string PromptVersion,
    int? LatencyMs,
    int TokensIn,
    int TokensOut,
    decimal EstimatedCostUsd);
