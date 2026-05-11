using Accountant.Contracts;

namespace Accountant.Jobs.Extraction;

public interface IExtractorFactory
{
    /// Returns an extractor for the given vendor name (`VendorName.*`).
    /// Throws `InvalidOperationException` when the vendor is unknown or its
    /// API key is not configured.
    IAccountingDocumentExtractor Create(string vendor);

    /// Vendor that should be used when no per-document / per-tenant override
    /// is set. Reads from `Extraction:DefaultVendor`; falls back to Claude.
    string DefaultVendor { get; }

    /// Model name actually used by the vendor (returned for audit / cost
    /// attribution on `DocumentExtraction.ModelName`).
    string ModelFor(string vendor);
}
