using System.Globalization;
using System.Text.Json;
using Accountant.DataAccess;
using Accountant.DataAccess.Entities.Product;
using Accountant.DataAccess.Services;
using Accountant.Jobs.Extraction;
using Accountant.Storage.Abstractions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Accountant.Jobs;

/// Hangfire job: run the configured AI extractor over a single Document.
///
/// Flow:
/// 1. Load Document (must be Uploaded or Queued).
/// 2. Flip status to Processing.
/// 3. Open the blob via IFileStore → copy to a temp file (extractor API takes file paths).
/// 4. Run vendor extractor.
/// 5. Persist `DocumentExtraction` row + flip status to Extracted/Failed.
///
/// Retry: 3 attempts with exponential backoff (Hangfire's `AutomaticRetry` filter
/// is set on the job class). After exhaust → status=Failed.
[AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 30, 120, 300 })]
public sealed class ExtractDocumentJob
{
    private readonly AccountantDbContext _db;
    private readonly IFileStore _fileStore;
    private readonly IExtractorFactory _factory;
    private readonly IAppSettingsService _settings;
    private readonly ILogger<ExtractDocumentJob> _logger;

    public ExtractDocumentJob(
        AccountantDbContext db,
        IFileStore fileStore,
        IExtractorFactory factory,
        IAppSettingsService settings,
        ILogger<ExtractDocumentJob> logger)
    {
        _db = db;
        _fileStore = fileStore;
        _factory = factory;
        _settings = settings;
        _logger = logger;
    }

    public async Task RunAsync(int documentId, CancellationToken cancellationToken)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            _logger.LogWarning("ExtractDocumentJob: document {DocumentId} not found.", documentId);
            return;
        }

        if (document.Status == DocumentStatus.Extracted)
        {
            _logger.LogInformation(
                "ExtractDocumentJob: document {DocumentId} is already Extracted — skipping.", documentId);
            return;
        }

        document.Status = DocumentStatus.Processing;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Vendor selection: DB-backed setting (admin UI) overrides the
        // factory's config-based default. Falls back when admin hasn't
        // picked one yet.
        var configuredVendor = await _settings
            .GetAsync(AppSettingKeys.ExtractionDefaultVendor, cancellationToken)
            .ConfigureAwait(false);
        var vendor = !string.IsNullOrWhiteSpace(configuredVendor)
            ? configuredVendor
            : _factory.DefaultVendor;
        var extractor = _factory.Create(vendor);
        var startedAt = DateTime.UtcNow;

        var tempFile = await CopyToTempAsync(document, cancellationToken).ConfigureAwait(false);
        try
        {
            var results = await extractor.ExtractAsync(new[] { tempFile }, cancellationToken)
                .ConfigureAwait(false);
            var result = results.SingleOrDefault()
                ?? throw new InvalidOperationException(
                    $"Extractor '{vendor}' returned no results for document {documentId}.");

            var extraction = new DocumentExtraction
            {
                DocumentId = document.Id,
                Vendor = vendor,
                ModelName = result.Provider.Model,
                PromptVersion = result.Provider.PromptVersion ?? "unknown",
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTime.UtcNow,
                LatencyMs = result.Provider.DurationMs,
                TokensIn = result.Provider.InputTokens ?? 0,
                TokensOut = result.Provider.OutputTokens ?? 0,
                EstimatedCostUsd = ParseCost(result.Provider.CostEstimateUsd),
                Status = DocumentExtractionStatus.Success,
                JsonResult = JsonSerializer.Serialize(result, SerializerOptions),
            };
            _db.DocumentExtractions.Add(extraction);

            document.Status = DocumentStatus.Extracted;
            document.ProcessedAtUtc = extraction.CompletedAtUtc;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "ExtractDocumentJob: document {DocumentId} extracted via {Vendor} in {LatencyMs} ms.",
                documentId, vendor, extraction.LatencyMs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = new DocumentExtraction
            {
                DocumentId = document.Id,
                Vendor = vendor,
                ModelName = _factory.ModelFor(vendor),
                PromptVersion = "unknown",
                StartedAtUtc = startedAt,
                CompletedAtUtc = DateTime.UtcNow,
                Status = DocumentExtractionStatus.Failed,
                FailureReason = Truncate(ex.Message, 2000),
            };
            _db.DocumentExtractions.Add(failed);
            document.Status = DocumentStatus.Failed;
            document.ProcessedAtUtc = failed.CompletedAtUtc;
            await _db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);

            _logger.LogError(
                ex,
                "ExtractDocumentJob: document {DocumentId} failed via {Vendor}.",
                documentId, vendor);

            // Surface the exception so Hangfire retries per the AutomaticRetry policy.
            throw;
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private async Task<string> CopyToTempAsync(Document document, CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(document.OriginalFileName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = document.ContentType switch
            {
                "application/pdf" => ".pdf",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                _ => ".bin",
            };
        }
        var tempFile = Path.Combine(Path.GetTempPath(), $"accountant-{Guid.NewGuid():N}{ext}");
        await using var source = await _fileStore.OpenReadAsync(document.StorageKey, cancellationToken)
            .ConfigureAwait(false);
        await using var destination = new FileStream(
            tempFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        return tempFile;
    }

    private static decimal ParseCost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : 0m;
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup. */ }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
