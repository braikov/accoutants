namespace Accountant.Contracts;

public sealed record ImageQuality
{
    public Readability? Readability { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = [];
}

public sealed record Source
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public int? PageCount { get; init; } = 1;
    public int? PageIndex { get; init; } = 0;
    public int? DetectedDocumentCount { get; init; } = 1;
    public int? ExtractedDocumentIndex { get; init; } = 0;
    public ImageQuality ImageQuality { get; init; } = new();
}

public sealed record ConfidenceMap
{
    public double? Overall { get; init; }
    public double? Document { get; init; }
    public double? Supplier { get; init; }
    public double? Customer { get; init; }
    public double? Totals { get; init; }
    public double? LineItems { get; init; }
}

public sealed record ModelAssessment
{
    public ConfidenceMap Confidence { get; init; } = new();
    public IReadOnlyList<string> ExtractionWarnings { get; init; } = [];
}

public sealed record EvidenceItem
{
    public string? Text { get; init; }
    public double? Confidence { get; init; }
}

public sealed record Check
{
    public required string Code { get; init; }
    public required CheckStatus Status { get; init; }
    public string Message { get; init; } = "";
}

public sealed record Validation
{
    public bool NeedsReview { get; init; }
    public IReadOnlyList<Check> Checks { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record Provider
{
    public required Engine Engine { get; init; }
    public required string Model { get; init; }
    public Pipeline Pipeline { get; init; } = Pipeline.VisionDirect;
    public bool OcrUsed { get; init; }
    public string? PromptVersion { get; init; }
    public required string CreatedAt { get; init; }
    public int? DurationMs { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public string? CostEstimateUsd { get; init; }
}
