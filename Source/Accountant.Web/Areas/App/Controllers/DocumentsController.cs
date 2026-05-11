using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Accountant.Contracts;
using Accountant.DataAccess;
using Accountant.DataAccess.Entities.Product;
using Accountant.Jobs;
using Accountant.Storage;
using Accountant.Storage.Abstractions;
using Accountant.Storage.Thumbnails;
using Accountant.Web.Areas.App.Services;
using Accountant.Web.Areas.App.ViewModels;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accountant.Web.Areas.App.Controllers;

[Area("App")]
[Authorize]
public sealed class DocumentsController : Controller
{
    private const long MaxFileSize = 25 * 1024 * 1024;
    private static readonly HashSet<string> AcceptedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/tiff",
        "image/bmp",
    };

    private readonly AccountantDbContext _db;
    private readonly IFileStore _files;
    private readonly ThumbnailDispatcher _thumbnails;
    private readonly StorageOptions _storageOptions;
    private readonly IBackgroundJobClient _jobs;
    private readonly IActiveTenantAccessor _activeTenant;

    public DocumentsController(
        AccountantDbContext db,
        IFileStore files,
        ThumbnailDispatcher thumbnails,
        IOptions<StorageOptions> storageOptions,
        IBackgroundJobClient jobs,
        IActiveTenantAccessor activeTenant)
    {
        _db = db;
        _files = files;
        _thumbnails = thumbnails;
        _storageOptions = storageOptions.Value;
        _jobs = jobs;
        _activeTenant = activeTenant;
    }

    /// Receives one or more files via multipart upload. Stores each blob,
    /// renders a thumbnail synchronously, and inserts a Document row with
    /// Status=Uploaded.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(120 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 120 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        int? folderId,
        [FromForm] IFormFileCollection files,
        CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        if (folderId is int fid)
        {
            var exists = await _db.Folders
                .AnyAsync(f => f.Id == fid && f.TenantId == tenant.Id, cancellationToken);
            if (!exists)
            {
                return BadRequest(new { error = "Невалидна папка." });
            }
        }

        if (files is null || files.Count == 0)
        {
            return BadRequest(new { error = "Не са изпратени файлове." });
        }

        var userId = CurrentUserId();
        var created = new List<object>();
        var errors = new List<object>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (file.Length <= 0)
            {
                errors.Add(new { file = file.FileName, error = "Празен файл." });
                continue;
            }
            if (file.Length > MaxFileSize)
            {
                errors.Add(new { file = file.FileName, error = $"Файлът е по-голям от {MaxFileSize / (1024 * 1024)} MB." });
                continue;
            }
            if (!AcceptedContentTypes.Contains(file.ContentType))
            {
                errors.Add(new { file = file.FileName, error = $"Неподдържан тип: {file.ContentType}." });
                continue;
            }

            string storageKey;
            await using (var source = file.OpenReadStream())
            {
                storageKey = await _files.SaveAsync(source, file.ContentType, cancellationToken).ConfigureAwait(false);
            }

            string? thumbnailKey = null;
            try
            {
                await using var blob = await _files.OpenReadAsync(storageKey, cancellationToken);
                await using var thumbBuffer = new MemoryStream();
                var rendered = await _thumbnails.TryRenderAsync(
                    blob, thumbBuffer, file.ContentType, _storageOptions.ThumbnailWidth, cancellationToken);
                if (rendered)
                {
                    thumbBuffer.Position = 0;
                    thumbnailKey = await _files.SaveAsync(thumbBuffer, "image/jpeg", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                // Thumbnail failure must not block upload — the document is
                // still usable, just shown with a placeholder.
                errors.Add(new { file = file.FileName, warning = $"Thumbnail рендирането се провали: {ex.Message}" });
            }

            var document = new Accountant.DataAccess.Entities.Product.Document
            {
                TenantId = tenant.Id,
                FolderId = folderId,
                UploadedByUserId = userId,
                OriginalFileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                ByteSize = file.Length,
                StorageKey = storageKey,
                ThumbnailKey = thumbnailKey,
                Status = DocumentStatus.Uploaded,
                CreatedAtUtc = DateTime.UtcNow,
            };
            _db.Documents.Add(document);
            await _db.SaveChangesAsync(cancellationToken);

            created.Add(new
            {
                id = document.Id,
                originalFileName = document.OriginalFileName,
                contentType = document.ContentType,
                byteSize = document.ByteSize,
                status = document.Status.ToString(),
                hasThumbnail = thumbnailKey is not null,
            });
        }

        return Json(new { created, errors });
    }

    /// Flips status to Queued and enqueues a Hangfire job per id.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnqueueExtraction(
        [FromForm] int[] documentIds,
        CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        if (documentIds is null || documentIds.Length == 0)
        {
            return BadRequest(new { error = "Няма избрани документи." });
        }

        // .NET 10 resolves `int[].Contains` to `ReadOnlySpan<int>.Contains`,
        // which EF Core cannot translate. Materialize as a List to force the
        // LINQ-to-Entities overload.
        var ids = documentIds.ToList();
        var documents = await _db.Documents
            .Where(d => d.TenantId == tenant.Id && ids.Contains(d.Id)
                && (d.Status == DocumentStatus.Uploaded || d.Status == DocumentStatus.Failed))
            .ToListAsync(cancellationToken);

        foreach (var doc in documents)
        {
            doc.Status = DocumentStatus.Queued;
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var doc in documents)
        {
            _jobs.Enqueue<ExtractDocumentJob>(job => job.RunAsync(doc.Id, CancellationToken.None));
        }
        return Json(new { queued = documents.Select(d => d.Id).ToArray() });
    }

    /// Streams the thumbnail blob (JPEG). 404 when the document has no
    /// thumbnail (e.g. unsupported content-type or render failure).
    [HttpGet]
    public async Task<IActionResult> Thumbnail(int id, CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        var doc = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id && d.TenantId == tenant.Id)
            .Select(d => new { d.ThumbnailKey })
            .FirstOrDefaultAsync(cancellationToken);
        if (doc?.ThumbnailKey is null)
        {
            return NotFound();
        }
        var stream = await _files.OpenReadAsync(doc.ThumbnailKey, cancellationToken);
        // Browsers cache aggressively — safe here because thumbnails never
        // change in place (a re-render would create a new key).
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(stream, "image/jpeg");
    }

    /// Document detail page (read-only). Loads the document + latest
    /// extraction + latest correction in one round trip and merges them
    /// into a `DocumentDetailViewModel`.
    [HttpGet]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        var doc = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id && d.TenantId == tenant.Id)
            .Select(d => new
            {
                d.Id,
                d.OriginalFileName,
                d.ContentType,
                d.ByteSize,
                d.Status,
                d.CreatedAtUtc,
                d.ProcessedAtUtc,
                d.FolderId,
                Extraction = d.Extraction,
                LatestCorrection = d.Corrections
                    .OrderByDescending(c => c.EditedAtUtc)
                    .Select(c => new { c.CorrectedJson, c.EditedAtUtc })
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (doc is null)
        {
            return NotFound();
        }

        ExtractionMeta? extractionMeta = null;
        ExtractionResult? data = null;
        bool isCorrected = false;
        string? failureReason = null;

        if (doc.Extraction is { } e)
        {
            extractionMeta = new ExtractionMeta(
                e.Vendor, e.ModelName, e.PromptVersion, e.LatencyMs,
                e.TokensIn, e.TokensOut, e.EstimatedCostUsd);
            if (e.Status == DocumentExtractionStatus.Failed)
            {
                failureReason = e.FailureReason;
            }
            if (doc.LatestCorrection is { } c)
            {
                data = DeserializeOrNull(c.CorrectedJson);
                isCorrected = data is not null;
            }
            if (data is null && !string.IsNullOrEmpty(e.JsonResult))
            {
                data = DeserializeOrNull(e.JsonResult);
            }
        }

        var model = new DocumentDetailViewModel
        {
            Id = doc.Id,
            OriginalFileName = doc.OriginalFileName,
            ContentType = doc.ContentType,
            ByteSize = doc.ByteSize,
            Status = doc.Status,
            CreatedAtUtc = doc.CreatedAtUtc,
            ProcessedAtUtc = doc.ProcessedAtUtc,
            FolderId = doc.FolderId,
            IsImage = doc.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
            Extraction = extractionMeta,
            Data = data,
            IsCorrected = isCorrected,
            LastEditedAtUtc = doc.LatestCorrection?.EditedAtUtc,
            FailureReason = failureReason,
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        var (doc, data) = await LoadCurrentDataAsync(tenant.Id, id, cancellationToken);
        if (doc is null)
        {
            return NotFound();
        }
        if (data is null)
        {
            // Cannot edit a doc that never produced JSON. Bounce to detail.
            return RedirectToAction(nameof(Detail), new { id });
        }
        var model = BuildEditViewModel(doc, data);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditDocumentViewModel model, CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        var (doc, currentData) = await LoadCurrentDataAsync(tenant.Id, id, cancellationToken);
        if (doc is null)
        {
            return NotFound();
        }
        if (currentData is null)
        {
            return RedirectToAction(nameof(Detail), new { id });
        }

        // Re-stamp non-form fields onto the model so a re-render after a
        // validation failure shows the right header info.
        model.Id = doc.Id;
        model.OriginalFileName = doc.OriginalFileName;
        model.ContentType = doc.ContentType;
        model.IsImage = doc.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var updated = ApplyEdits(currentData, model);
        var json = JsonSerializer.Serialize(updated, SerializerOptions);

        _db.DocumentCorrections.Add(new DocumentCorrection
        {
            DocumentId = doc.Id,
            EditedByUserId = CurrentUserId(),
            EditedAtUtc = DateTime.UtcNow,
            CorrectedJson = json,
        });
        await _db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Detail), new { id });
    }

    /// Returns the latest correction-or-extraction JSON as a download.
    [HttpGet]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        var doc = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id && d.TenantId == tenant.Id)
            .Select(d => new
            {
                d.OriginalFileName,
                ExtractionJson = d.Extraction != null ? d.Extraction.JsonResult : null,
                CorrectionJson = d.Corrections
                    .OrderByDescending(c => c.EditedAtUtc)
                    .Select(c => c.CorrectedJson)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (doc is null)
        {
            return NotFound();
        }
        var json = doc.CorrectionJson ?? doc.ExtractionJson;
        if (string.IsNullOrEmpty(json))
        {
            return NotFound();
        }
        var baseName = Path.GetFileNameWithoutExtension(doc.OriginalFileName);
        var bytes = Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", $"{baseName}.json");
    }

    /// Streams the original document blob for the inline viewer in Phase F.
    [HttpGet]
    public async Task<IActionResult> File(int id, CancellationToken cancellationToken)
    {
        var tenant = _activeTenant.Current ?? throw new InvalidOperationException("No active tenant.");
        var doc = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id && d.TenantId == tenant.Id)
            .Select(d => new { d.StorageKey, d.ContentType, d.OriginalFileName })
            .FirstOrDefaultAsync(cancellationToken);
        if (doc is null)
        {
            return NotFound();
        }
        var stream = await _files.OpenReadAsync(doc.StorageKey, cancellationToken);
        return File(stream, doc.ContentType, fileDownloadName: doc.OriginalFileName);
    }

    private int CurrentUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    private static readonly JsonSerializerOptions DeserializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// Loads the document + the currently authoritative `ExtractionResult`
    /// (correction-if-exists else extraction). Returns nulls when the doc
    /// doesn't exist or has no JSON yet.
    private async Task<(DocumentRow? Row, ExtractionResult? Data)> LoadCurrentDataAsync(
        int tenantId, int id, CancellationToken cancellationToken)
    {
        var row = await _db.Documents
            .AsNoTracking()
            .Where(d => d.Id == id && d.TenantId == tenantId)
            .Select(d => new DocumentRow(
                d.Id,
                d.OriginalFileName,
                d.ContentType,
                d.Extraction != null ? d.Extraction.JsonResult : null,
                d.Corrections
                    .OrderByDescending(c => c.EditedAtUtc)
                    .Select(c => c.CorrectedJson)
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return (null, null);
        var data = DeserializeOrNull(row.CorrectionJson) ?? DeserializeOrNull(row.ExtractionJson);
        return (row, data);
    }

    private sealed record DocumentRow(
        int Id,
        string OriginalFileName,
        string ContentType,
        string? ExtractionJson,
        string? CorrectionJson);

    private static EditDocumentViewModel BuildEditViewModel(DocumentRow doc, ExtractionResult data)
    {
        var e = data.Extraction;
        return new EditDocumentViewModel
        {
            Id = doc.Id,
            OriginalFileName = doc.OriginalFileName,
            ContentType = doc.ContentType,
            IsImage = doc.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
            DocumentType = e.DocumentType,
            Number = e.Document.Number,
            Date = e.Document.Date,
            TaxEventDate = e.Document.TaxEventDate,
            DueDate = e.Document.DueDate,
            Currency = e.Document.Currency,
            ExchangeRate = e.Document.ExchangeRate,
            Place = e.Document.Place,
            Notes = e.Document.Notes,
            Supplier = PartyToForm(e.Supplier),
            Customer = PartyToForm(e.Customer),
            TotalsNet = e.Totals.Net,
            TotalsVat = e.Totals.Vat,
            TotalsGross = e.Totals.Gross,
            TotalsDiscount = e.Totals.Discount,
            TotalsRounding = e.Totals.Rounding,
            TotalsAmountDue = e.Totals.AmountDue,
            Lines = e.LineItems.Select(LineToForm).ToList(),
            Payment = e.Payments.Count > 0 ? PaymentToForm(e.Payments[0]) : new PaymentForm(),
            Fiscal = new FiscalForm
            {
                FiscalReceiptNumber = e.Fiscal.FiscalReceiptNumber,
                FiscalDeviceNumber = e.Fiscal.FiscalDeviceNumber,
                Operator = e.Fiscal.Operator,
                QrCode = e.Fiscal.QrCode,
            },
        };
    }

    private static PartyForm PartyToForm(Party p) => new()
    {
        Name = p.Name, Eik = p.Eik, VatNumber = p.VatNumber, Address = p.Address,
        City = p.City, Country = p.Country, Mol = p.Mol,
    };

    private static LineItemForm LineToForm(LineItem l) => new()
    {
        Description = l.Description, Quantity = l.Quantity, Unit = l.Unit,
        UnitPrice = l.UnitPrice, DiscountPct = l.DiscountPct, VatRate = l.VatRate,
        Net = l.Net, Vat = l.Vat, Gross = l.Gross, IncludesVat = l.IncludesVat,
    };

    private static PaymentForm PaymentToForm(Payment p) => new()
    {
        Method = p.Method, Amount = p.Amount, Currency = p.Currency,
        Iban = p.Iban, Bic = p.Bic, BankName = p.BankName,
    };

    /// Overwrites the `Extraction` subdocument with values from the form
    /// while preserving `Source`, `Validation`, `Provider`, `ModelAssessment`,
    /// and `Evidence` from the original.
    private static ExtractionResult ApplyEdits(ExtractionResult original, EditDocumentViewModel m)
    {
        return original with
        {
            Extraction = new Extraction
            {
                DocumentType = m.DocumentType,
                Language = original.Extraction.Language,
                Country = original.Extraction.Country,
                Document = new Accountant.Contracts.Document
                {
                    Number = Nullify(m.Number),
                    Date = Nullify(m.Date),
                    TaxEventDate = Nullify(m.TaxEventDate),
                    DueDate = Nullify(m.DueDate),
                    Currency = Nullify(m.Currency),
                    ExchangeRate = Nullify(m.ExchangeRate),
                    Place = Nullify(m.Place),
                    Notes = Nullify(m.Notes),
                },
                Supplier = FormToParty(m.Supplier),
                Customer = FormToParty(m.Customer),
                Totals = new Totals
                {
                    Net = Nullify(m.TotalsNet),
                    Vat = Nullify(m.TotalsVat),
                    Gross = Nullify(m.TotalsGross),
                    Discount = Nullify(m.TotalsDiscount),
                    Rounding = Nullify(m.TotalsRounding),
                    AmountDue = Nullify(m.TotalsAmountDue),
                },
                VatBreakdown = original.Extraction.VatBreakdown,
                Payments = HasPayment(m.Payment)
                    ? new List<Payment> { FormToPayment(m.Payment) }
                    : new List<Payment>(),
                LineItems = m.Lines
                    .Where(l => !string.IsNullOrWhiteSpace(l.Description)
                        || !string.IsNullOrWhiteSpace(l.Net)
                        || !string.IsNullOrWhiteSpace(l.Gross))
                    .Select(FormToLine)
                    .ToList(),
                Fiscal = new Fiscal
                {
                    FiscalReceiptNumber = Nullify(m.Fiscal.FiscalReceiptNumber),
                    FiscalDeviceNumber = Nullify(m.Fiscal.FiscalDeviceNumber),
                    Operator = Nullify(m.Fiscal.Operator),
                    QrCode = Nullify(m.Fiscal.QrCode),
                },
            },
        };
    }

    private static Party FormToParty(PartyForm f) => new()
    {
        Name = Nullify(f.Name), Eik = Nullify(f.Eik), VatNumber = Nullify(f.VatNumber),
        Address = Nullify(f.Address), City = Nullify(f.City), Country = Nullify(f.Country),
        Mol = Nullify(f.Mol),
    };

    private static LineItem FormToLine(LineItemForm f) => new()
    {
        Description = Nullify(f.Description), Quantity = Nullify(f.Quantity), Unit = Nullify(f.Unit),
        UnitPrice = Nullify(f.UnitPrice), DiscountPct = Nullify(f.DiscountPct), VatRate = Nullify(f.VatRate),
        Net = Nullify(f.Net), Vat = Nullify(f.Vat), Gross = Nullify(f.Gross), IncludesVat = f.IncludesVat,
    };

    private static Payment FormToPayment(PaymentForm f) => new()
    {
        Method = f.Method, Amount = Nullify(f.Amount), Currency = Nullify(f.Currency),
        Iban = Nullify(f.Iban), Bic = Nullify(f.Bic), BankName = Nullify(f.BankName),
    };

    private static bool HasPayment(PaymentForm p) =>
        !string.IsNullOrWhiteSpace(p.Amount)
        || !string.IsNullOrWhiteSpace(p.Iban)
        || !string.IsNullOrWhiteSpace(p.Bic)
        || !string.IsNullOrWhiteSpace(p.BankName)
        || p.Method.HasValue;

    private static string? Nullify(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ExtractionResult? DeserializeOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ExtractionResult>(json, DeserializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
