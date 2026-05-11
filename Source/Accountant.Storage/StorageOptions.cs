namespace Accountant.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// Absolute or app-relative directory under which uploaded blobs and
    /// rendered thumbnails are stored. Resolved against `AppContext.BaseDirectory`
    /// when relative.
    public string Root { get; set; } = "App_Data/uploads";

    /// Width (px) of generated thumbnails. Height is computed to preserve aspect.
    public int ThumbnailWidth { get; set; } = 240;
}
