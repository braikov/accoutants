using Accountant.Claude;
using Accountant.Codex;
using Accountant.Contracts;
using Accountant.Gemini;
using Microsoft.Extensions.Configuration;

namespace Accountant.Jobs.Extraction;

/// Reads vendor options from configuration (`Claude:*`, `Codex:*`, `Gemini:*`)
/// and constructs the matching extractor on demand. Lifetimes are kept simple:
/// the factory itself is singleton, and each `Create` call instantiates a
/// fresh extractor — these are cheap (HTTP client per call). Optimize later
/// if profiling shows allocation pressure on the job worker hot path.
public sealed class ExtractorFactory : IExtractorFactory
{
    private readonly IConfiguration _configuration;

    public ExtractorFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string DefaultVendor => _configuration["Extraction:DefaultVendor"] ?? VendorName.Claude;

    public IAccountingDocumentExtractor Create(string vendor)
        => vendor switch
        {
            VendorName.Claude => new ClaudeExtractor(BindOptions<ClaudeExtractorOptions>("Claude")),
            VendorName.Codex => new CodexExtractor(BindOptions<CodexExtractorOptions>("Codex")),
            VendorName.Gemini => new GeminiExtractor(BindOptions<GeminiExtractorOptions>("Gemini")),
            _ => throw new InvalidOperationException(
                $"Unknown extraction vendor '{vendor}'. " +
                $"Expected one of: {VendorName.Claude}, {VendorName.Codex}, {VendorName.Gemini}."),
        };

    public string ModelFor(string vendor)
        => vendor switch
        {
            VendorName.Claude => BindOptions<ClaudeExtractorOptions>("Claude").Model,
            VendorName.Codex => BindOptions<CodexExtractorOptions>("Codex").Model,
            VendorName.Gemini => BindOptions<GeminiExtractorOptions>("Gemini").Model,
            _ => "unknown",
        };

    private T BindOptions<T>(string sectionName) where T : new()
    {
        var options = new T();
        _configuration.GetSection(sectionName).Bind(options);
        return options;
    }
}
