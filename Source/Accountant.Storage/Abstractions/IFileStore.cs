namespace Accountant.Storage.Abstractions;

/// Persists opaque binary blobs (uploaded documents, generated thumbnails).
/// Keys are returned by `SaveAsync` and are impl-specific — callers store
/// them on the entity (`Document.StorageKey`) and pass them back on read.
///
/// Default impl `LocalFileStore` writes to disk under a configured root.
/// Future S3 / Azure-Blob impls can replace it without touching callers.
public interface IFileStore
{
    /// Stores the stream and returns a new opaque key. Stream is read from
    /// current position to end. Caller owns the stream lifecycle.
    Task<string> SaveAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    /// Opens the stored blob for reading. Throws `FileNotFoundException`
    /// when the key is unknown. Caller owns the returned stream.
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// Removes the blob. Idempotent — silently succeeds when the key
    /// doesn't exist. Caller is responsible for clearing entity references.
    Task DeleteAsync(string key, CancellationToken cancellationToken);

    /// Returns whether a blob exists for the key.
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}
