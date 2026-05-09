using System.Globalization;
using System.Text.Json;
using Accountant.Claude;
using Accountant.Codex;
using Accountant.Contracts;
using Microsoft.Extensions.Configuration;

namespace Accountant.Processors;

internal static class ExtractCommand
{
    private static readonly HashSet<string> ImageExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".gif",
    ];

    private static readonly string[] AllVendors = ["claude", "codex", "gemini"];

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        var parsed = ParseArgs(args);
        if (parsed is null) return 2;
        var (vendors, dir, limit, files) = parsed.Value;

        var paths = ExpandInputs(dir, files, limit);
        if (paths.Count == 0)
        {
            Console.Error.WriteLine("No image files matched.");
            return 2;
        }

        var config = BuildConfig();

        var runId = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        var runDir = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "runs", runId));
        runDir.Create();
        var runDirAbs = runDir.FullName;

        Console.WriteLine($"Run id:  {runId}");
        Console.WriteLine($"Run dir: {runDirAbs}");
        Console.WriteLine($"Vendors: {string.Join(", ", vendors)}");
        Console.WriteLine($"Images:  {paths.Count}");
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); Console.WriteLine("\nCancelling..."); };

        var perVendor = new List<VendorRunResult>();
        foreach (var vendor in vendors)
        {
            if (cts.IsCancellationRequested) break;

            IAccountingDocumentExtractor extractor;
            string vendorPrefix;
            try
            {
                (extractor, vendorPrefix) = BuildExtractor(vendor, config);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{vendor}] ERROR: {ex.Message}");
                perVendor.Add(new VendorRunResult(vendor, [], 0, 0, 0, 0m, ex.Message));
                continue;
            }

            Console.WriteLine($"--- {vendorPrefix} ---");
            var result = await RunVendorAsync(extractor, vendorPrefix, paths, runDirAbs, cts.Token);
            WriteVendorSummary(runDirAbs, runId, result);
            perVendor.Add(result);
            Console.WriteLine();
        }

        WriteCombinedSummary(runDirAbs, runId, perVendor);

        Console.WriteLine($"Run summary: {Path.Combine(runDirAbs, "_run_summary.json")}");
        return perVendor.All(v => v.SetupError is null && v.Entries.All(e => e.Ok)) ? 0 : 1;
    }

    private static async Task<VendorRunResult> RunVendorAsync(
        IAccountingDocumentExtractor extractor,
        string vendorPrefix,
        List<string> paths,
        string runDir,
        CancellationToken ct)
    {
        var entries = new List<RunEntry>(paths.Count);
        var totalCost = 0m;
        var totalIn = 0;
        var totalOut = 0;
        var totalDuration = 0;

        for (var i = 0; i < paths.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            var path = paths[i];
            Console.Write($"[{i + 1}/{paths.Count}] {Path.GetFileName(path)} ... ");
            try
            {
                var results = await extractor.ExtractAsync([path], ct);
                var result = results[0];

                WriteResult(result, vendorPrefix, runDir, path);

                var entry = new RunEntry(
                    File: Path.GetFileName(path),
                    Ok: true,
                    DocType: result.Extraction.DocumentType.ToString(),
                    NeedsReview: result.Validation.NeedsReview,
                    FailCount: result.Validation.Checks.Count(c => c.Status == CheckStatus.Fail),
                    WarnCount: result.Validation.Checks.Count(c => c.Status == CheckStatus.Warning),
                    DurationMs: result.Provider.DurationMs ?? 0,
                    InputTokens: result.Provider.InputTokens ?? 0,
                    OutputTokens: result.Provider.OutputTokens ?? 0,
                    CostUsd: result.Provider.CostEstimateUsd ?? "0.0000",
                    Error: null);

                entries.Add(entry);
                totalIn += entry.InputTokens;
                totalOut += entry.OutputTokens;
                totalDuration += entry.DurationMs;
                if (decimal.TryParse(entry.CostUsd, NumberStyles.Number, CultureInfo.InvariantCulture, out var c))
                    totalCost += c;

                var reviewTag = entry.NeedsReview ? "REVIEW" : "ok";
                var extras = string.Join(", ",
                    new[] { entry.FailCount > 0 ? $"{entry.FailCount} fail" : null,
                            entry.WarnCount > 0 ? $"{entry.WarnCount} warn" : null }
                    .Where(s => s is not null));
                var extraStr = extras.Length > 0 ? $" [{extras}]" : "";
                Console.WriteLine($"{entry.DocType,-12} {reviewTag}{extraStr}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("CANCELLED");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAILED: {ex.Message}");
                entries.Add(new RunEntry(
                    File: Path.GetFileName(path),
                    Ok: false,
                    DocType: null, NeedsReview: false, FailCount: 0, WarnCount: 0,
                    DurationMs: 0, InputTokens: 0, OutputTokens: 0, CostUsd: "0.0000",
                    Error: ex.Message));
            }
        }

        var ok = entries.Count(e => e.Ok);
        var review = entries.Count(e => e.NeedsReview);
        Console.WriteLine($"{vendorPrefix}: {ok}/{entries.Count} extracted; {ok - review} clean, {review} for review. " +
                          $"in={totalIn:N0} out={totalOut:N0} cost=${totalCost:F4} duration={totalDuration / 1000.0:F1}s");

        return new VendorRunResult(vendorPrefix, entries, totalIn, totalOut, totalDuration, totalCost, null);
    }

    private static (string[] vendors, string? dir, int? limit, List<string> files)? ParseArgs(string[] args)
    {
        string? vendorArg = null;
        string? dir = null;
        int? limit = null;
        var files = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--vendor":
                    vendorArg = args[++i];
                    break;
                case "--dir":
                    dir = args[++i];
                    break;
                case "--limit":
                    if (!int.TryParse(args[++i], out var l) || l <= 0)
                    {
                        Console.Error.WriteLine($"Invalid --limit: {args[i]}");
                        return null;
                    }
                    limit = l;
                    break;
                default:
                    files.Add(args[i]);
                    break;
            }
        }

        if (vendorArg is null)
        {
            Console.Error.WriteLine("--vendor is required (claude | codex | gemini | all | comma-separated list).");
            return null;
        }
        if (dir is null && files.Count == 0)
        {
            Console.Error.WriteLine("Provide --dir <path> or one or more file paths.");
            return null;
        }

        var vendors = vendorArg.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? AllVendors
            : vendorArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Select(v => v.ToLowerInvariant())
                       .ToArray();

        var unknown = vendors.Where(v => !AllVendors.Contains(v)).ToArray();
        if (unknown.Length > 0)
        {
            Console.Error.WriteLine($"Unknown vendor(s): {string.Join(", ", unknown)}. Expected: claude | codex | gemini | all.");
            return null;
        }

        return (vendors, dir, limit, files);
    }

    private static List<string> ExpandInputs(string? dir, List<string> files, int? limit)
    {
        var paths = new List<string>();
        if (dir is not null)
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"--dir '{dir}' is not a directory.");
                return [];
            }
            foreach (var f in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    paths.Add(f);
            }
        }
        foreach (var f in files)
        {
            if (File.Exists(f) && ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                paths.Add(Path.GetFullPath(f));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = paths.Where(p => seen.Add(p)).ToList();
        return limit is { } l && unique.Count > l ? unique.Take(l).ToList() : unique;
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<ExtractCommandMarker>(optional: true)
            .AddEnvironmentVariables()
            .Build();

    private static (IAccountingDocumentExtractor extractor, string vendorPrefix) BuildExtractor(
        string vendor, IConfiguration config)
    {
        return vendor.ToLowerInvariant() switch
        {
            "claude" => BuildClaude(config),
            "codex" => BuildCodex(config),
            "gemini" => BuildGemini(config),
            _ => throw new ArgumentException($"Unknown vendor '{vendor}'. Expected: claude | codex | gemini."),
        };
    }

    private static (IAccountingDocumentExtractor, string) BuildClaude(IConfiguration config)
    {
        var apiKey = config["Claude:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "Claude API key missing. Set Claude:ApiKey in user-secrets or ANTHROPIC_API_KEY env var.");
        var options = new ClaudeExtractorOptions
        {
            ApiKey = apiKey,
            Model = config["Claude:Model"] ?? "claude-sonnet-4-6",
        };
        return (new ClaudeExtractor(options), "Claude");
    }

    private static (IAccountingDocumentExtractor, string) BuildCodex(IConfiguration config)
    {
        var apiKey = config["Codex:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "OpenAI API key missing. Set Codex:ApiKey in user-secrets or OPENAI_API_KEY env var.");
        var options = new CodexExtractorOptions
        {
            ApiKey = apiKey,
            Model = config["Codex:Model"] ?? "gpt-5.4-mini",
        };
        return (new CodexExtractor(options), "Codex");
    }

    private static (IAccountingDocumentExtractor, string) BuildGemini(IConfiguration config)
    {
        var apiKey = config["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY") ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException(
                "Gemini API key missing. Set Gemini:ApiKey in user-secrets or GOOGLE_API_KEY env var.");
        var options = new Accountant.Gemini.GeminiExtractorOptions
        {
            ApiKey = apiKey,
            Model = config["Gemini:Model"] ?? "gemini-flash-latest",
        };
        return (new Accountant.Gemini.GeminiExtractor(options), "Gemini");
    }

    private static void WriteResult(ExtractionResult result, string vendorPrefix, string runDir, string sourcePath)
    {
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var json = JsonSerializer.Serialize(result, ExtractionJson.Default) + "\n";

        var runFile = Path.Combine(runDir, $"{vendorPrefix}_{stem}.json");
        File.WriteAllText(runFile, json);

        var docsFile = Path.Combine(Path.GetDirectoryName(sourcePath)!, $"{vendorPrefix}_{stem}.json");
        try
        {
            File.WriteAllText(docsFile, json);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"  warning: could not write {docsFile}: {ex.Message}");
        }
    }

    private static void WriteVendorSummary(string runDir, string runId, VendorRunResult v)
    {
        var summary = new
        {
            run_id = runId,
            vendor = v.VendorPrefix,
            schema_version = "accountant.document.v2",
            setup_error = v.SetupError,
            totals = new
            {
                input_tokens = v.TotalIn,
                output_tokens = v.TotalOut,
                duration_ms = v.TotalDuration,
                cost_usd = Math.Round(v.TotalCost, 4),
            },
            runs = v.Entries,
        };
        File.WriteAllText(
            Path.Combine(runDir, $"_run_summary_{v.VendorPrefix}.json"),
            JsonSerializer.Serialize(summary, SummaryOptions));
    }

    private static void WriteCombinedSummary(string runDir, string runId, List<VendorRunResult> perVendor)
    {
        var combined = new
        {
            run_id = runId,
            schema_version = "accountant.document.v2",
            vendors = perVendor.Select(v => new
            {
                vendor = v.VendorPrefix,
                setup_error = v.SetupError,
                input_tokens = v.TotalIn,
                output_tokens = v.TotalOut,
                duration_ms = v.TotalDuration,
                cost_usd = Math.Round(v.TotalCost, 4),
                ok_count = v.Entries.Count(e => e.Ok),
                fail_count = v.Entries.Count(e => !e.Ok),
                review_count = v.Entries.Count(e => e.NeedsReview),
            }),
            grand_totals = new
            {
                input_tokens = perVendor.Sum(v => v.TotalIn),
                output_tokens = perVendor.Sum(v => v.TotalOut),
                duration_ms = perVendor.Sum(v => v.TotalDuration),
                cost_usd = Math.Round(perVendor.Sum(v => v.TotalCost), 4),
            },
        };
        File.WriteAllText(
            Path.Combine(runDir, "_run_summary.json"),
            JsonSerializer.Serialize(combined, SummaryOptions));
    }

    private static readonly JsonSerializerOptions SummaryOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            extract — run one or more vendor extractors over an image folder.

            USAGE:
              dotnet run --project Accountant.Processors -- extract
                  --vendor <claude|codex|gemini|all|comma-list>
                  (--dir <path> | <files...>)
                  [--limit N]

            EXAMPLES:
              extract --vendor claude --dir ../docs/facturi
              extract --vendor all image.jpg
              extract --vendor claude,gemini ../docs/facturi/20240213_190514.jpg
              extract --vendor all --dir ../docs/facturi --limit 3

            CONFIGURATION:
              Vendor API keys are read from user-secrets first, then environment variables:
                Claude:ApiKey  or  ANTHROPIC_API_KEY
                Codex:ApiKey   or  OPENAI_API_KEY
                Gemini:ApiKey  or  GOOGLE_API_KEY / GEMINI_API_KEY

              Set with:
                dotnet user-secrets set "Claude:ApiKey" "sk-..." --project Accountant.Processors
            """);
    }

    private sealed record RunEntry(
        string File, bool Ok, string? DocType, bool NeedsReview,
        int FailCount, int WarnCount, int DurationMs, int InputTokens, int OutputTokens,
        string CostUsd, string? Error);

    private sealed record VendorRunResult(
        string VendorPrefix,
        List<RunEntry> Entries,
        int TotalIn,
        int TotalOut,
        int TotalDuration,
        decimal TotalCost,
        string? SetupError);
}

internal sealed class ExtractCommandMarker { }
