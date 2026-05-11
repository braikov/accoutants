using Accountant.Storage.Abstractions;
using PDFtoImage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Accountant.Storage.Thumbnails;

public sealed class PdfThumbnailRenderer : IThumbnailRenderer
{
    public bool CanRender(string contentType)
        => string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase);

    public async Task RenderAsync(
        Stream source,
        Stream destination,
        int width,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var pdfBytes = buffer.ToArray();

        using var pageImage = new MemoryStream();
        // PDFtoImage is annotated as Android-only when targeting Android, but
        // is supported on every desktop platform we care about (Win/Linux/Mac).
#pragma warning disable CA1416
        Conversion.SavePng(pageImage, pdfBytes, page: (Index)0);
#pragma warning restore CA1416
        pageImage.Position = 0;

        using var image = await Image.LoadAsync(pageImage, cancellationToken).ConfigureAwait(false);
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(width, 0),
            Mode = ResizeMode.Max,
        }));
        await image.SaveAsync(destination, new JpegEncoder { Quality = 80 }, cancellationToken)
            .ConfigureAwait(false);
    }
}
