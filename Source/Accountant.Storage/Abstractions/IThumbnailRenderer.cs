namespace Accountant.Storage.Abstractions;

/// Renders a small preview image for a document. The thumbnail is itself
/// persisted via `IFileStore` and addressed by its own storage key
/// (`Document.ThumbnailStorageKey`).
public interface IThumbnailRenderer
{
    /// Whether this renderer can handle the given content type.
    bool CanRender(string contentType);

    /// Reads from `source` (positioned at the start) and writes a JPEG
    /// thumbnail to `destination`. Caller owns both streams.
    Task RenderAsync(
        Stream source,
        Stream destination,
        int width,
        CancellationToken cancellationToken);
}
