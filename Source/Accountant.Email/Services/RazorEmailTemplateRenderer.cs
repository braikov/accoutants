using Braikov.Identity.Core.Abstractions;
using Microsoft.Extensions.Options;
using RazorLight;

namespace Accountant.Email.Services;

/// RazorLight-backed renderer. Templates live under `Templates/` of the host's
/// output directory and are named `<templateName>.<culture>.cshtml`. When the
/// requested culture file is missing, falls back to the default culture file.
public class RazorEmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly IRazorLightEngine _engine;
    private readonly string _templatesPath;
    private readonly string _defaultCulture;

    public RazorEmailTemplateRenderer(IOptions<EmailOptions> options)
    {
        _defaultCulture = string.IsNullOrWhiteSpace(options.Value.DefaultCulture)
            ? "bg-BG"
            : options.Value.DefaultCulture;

        _templatesPath = Path.Combine(AppContext.BaseDirectory, "Templates");
        if (!Directory.Exists(_templatesPath))
        {
            throw new DirectoryNotFoundException(
                $"Email templates folder not found at '{_templatesPath}'. " +
                "Ensure the host project references Accountant.Email AND that " +
                "Templates/*.cshtml are copied to the application's output directory.");
        }

        _engine = new RazorLightEngineBuilder()
            .UseFileSystemProject(_templatesPath)
            .UseMemoryCachingProvider()
            .Build();
    }

    public async Task<string> RenderAsync<TModel>(
        string templateName,
        TModel model,
        string? culture,
        CancellationToken cancellationToken)
    {
        var requested = string.IsNullOrWhiteSpace(culture) ? _defaultCulture : culture!;

        var primary = $"{templateName}.{requested}.cshtml";
        if (File.Exists(Path.Combine(_templatesPath, primary)))
        {
            return await _engine.CompileRenderAsync(primary, model);
        }

        if (!string.Equals(requested, _defaultCulture, StringComparison.OrdinalIgnoreCase))
        {
            var fallback = $"{templateName}.{_defaultCulture}.cshtml";
            if (File.Exists(Path.Combine(_templatesPath, fallback)))
            {
                return await _engine.CompileRenderAsync(fallback, model);
            }
        }

        throw new InvalidOperationException(
            $"Email template '{templateName}' not found for culture '{requested}' " +
            $"or default culture '{_defaultCulture}' in '{_templatesPath}'.");
    }
}
