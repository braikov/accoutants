using Accountant.Storage.Abstractions;

namespace Accountant.Storage.Thumbnails;

/// Routes a render request to the first registered `IThumbnailRenderer`
/// that claims it can handle the content type. Returns `false` when no
/// renderer matches — caller decides whether to fall back to a placeholder
/// or skip thumbnail generation.
public sealed class ThumbnailDispatcher
{
    private readonly IReadOnlyList<IThumbnailRenderer> _renderers;

    public ThumbnailDispatcher(IEnumerable<IThumbnailRenderer> renderers)
    {
        _renderers = renderers.ToArray();
    }

    public async Task<bool> TryRenderAsync(
        Stream source,
        Stream destination,
        string contentType,
        int width,
        CancellationToken cancellationToken)
    {
        foreach (var renderer in _renderers)
        {
            if (renderer.CanRender(contentType))
            {
                await renderer.RenderAsync(source, destination, width, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
        }
        return false;
    }
}
