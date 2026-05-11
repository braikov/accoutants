using Accountant.Storage.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Accountant.Storage.Thumbnails;

public sealed class ImageThumbnailRenderer : IThumbnailRenderer
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "image/tiff",
        "image/bmp",
    };

    public bool CanRender(string contentType) => Supported.Contains(contentType);

    public async Task RenderAsync(
        Stream source,
        Stream destination,
        int width,
        CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync(source, cancellationToken).ConfigureAwait(false);
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(width, 0),
            Mode = ResizeMode.Max,
        }));
        await image.SaveAsync(destination, new JpegEncoder { Quality = 80 }, cancellationToken)
            .ConfigureAwait(false);
    }
}
