namespace Accountant.Contracts;

public interface IAccountingDocumentExtractor
{
    Task<IReadOnlyList<ExtractionResult>> ExtractAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);
}
