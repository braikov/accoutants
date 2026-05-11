using Accountant.Storage.Abstractions;
using Microsoft.Extensions.Options;

namespace Accountant.Storage.Local;

/// Stores blobs under `{Root}/yyyy/MM/{guid}{ext}`. Keys are the relative
/// `yyyy/MM/{guid}{ext}` path — opaque to callers but stable across moves
/// of the root directory.
public sealed class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IOptions<StorageOptions> options)
    {
        var configured = options.Value.Root;
        _root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);
    }

    public async Task<string> SaveAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var folder = $"{now:yyyy}/{now:MM}";
        var extension = ExtensionFor(contentType);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var key = $"{folder}/{fileName}";

        var fullDir = Path.Combine(_root, folder);
        Directory.CreateDirectory(fullDir);

        var fullPath = Path.Combine(_root, folder, fileName);
        await using var output = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var fullPath = ResolvePath(key);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Blob '{key}' not found.", fullPath);
        }

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var fullPath = ResolvePath(key);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(ResolvePath(key)));

    private string ResolvePath(string key)
    {
        // Reject absolute or traversal keys — callers must pass the opaque
        // value returned by SaveAsync, nothing else.
        if (Path.IsPathRooted(key) || key.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid storage key '{key}'.", nameof(key));
        }
        return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "application/pdf" => ".pdf",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/tiff" => ".tiff",
        "image/bmp" => ".bmp",
        _ => ".bin",
    };
}
