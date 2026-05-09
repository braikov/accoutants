using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Accountant.Gemini;

internal static class ImageLoader
{
    private const long AnthropicImageLimitBytes = 5 * 1024 * 1024;
    private const double SafetyMargin = 0.95;
    private static readonly long TargetRawBytes = (long)(AnthropicImageLimitBytes * SafetyMargin * 0.75);

    private static readonly int[] MaxSides = [2800, 2400, 2000, 1600, 1200];
    private static readonly int[] Qualities = [88, 80, 70];

    public sealed record LoadResult(byte[] Bytes, string MediaType, string? ResizeNote);

    public static LoadResult Load(string path, CancellationToken cancellationToken = default)
    {
        var raw = File.ReadAllBytes(path);
        var media = MediaType(path);

        if (raw.LongLength <= TargetRawBytes)
            return new LoadResult(raw, media, null);

        using var image = Image.Load(raw);

        foreach (var maxSide in MaxSides)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (w, h) = (image.Width, image.Height);
            var scale = (double)maxSide / Math.Max(w, h);

            using var working = scale >= 1.0
                ? image.Clone(_ => { })
                : image.Clone(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size((int)(w * scale), (int)(h * scale)),
                    Sampler = KnownResamplers.Lanczos3,
                }));

            foreach (var quality in Qualities)
            {
                using var ms = new MemoryStream();
                working.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });
                var data = ms.ToArray();
                if (data.LongLength <= TargetRawBytes)
                {
                    var note = $"Original {raw.Length:N0} bytes resized to {working.Width}x{working.Height} " +
                               $"JPEG q={quality} ({data.Length:N0} bytes) to fit Anthropic's 5MB limit.";
                    return new LoadResult(data, "image/jpeg", note);
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not shrink {Path.GetFileName(path)} below {TargetRawBytes:N0} bytes even at 1200px / q=70.");
    }

    private static string MediaType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };
}
