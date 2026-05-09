using System.Text.Json;
using Accountant.Contracts;

namespace Accountant.Processors;

/// `normalize` — coerce null nested objects in stored extraction JSONs to the
/// schema-required default shape. No vendor API calls. Use this to fix historical
/// outputs after the contract / sanitizer evolves, without paying for re-extraction.
internal static class NormalizeCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        string? dir = null;
        var vendors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dryRun = false;
        var noBackup = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dir":
                    dir = args[++i];
                    break;
                case "--vendor":
                    foreach (var v in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        vendors.Add(v);
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--no-backup":
                    noBackup = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 2;
            }
        }

        if (dir is null)
        {
            Console.Error.WriteLine("--dir <path> is required.");
            return 2;
        }
        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine($"--dir '{dir}' is not a directory.");
            return 2;
        }

        var files = Directory.EnumerateFiles(dir, "*.json")
            .Where(f => MatchesVendor(f, vendors))
            .Where(f => !Path.GetFileName(f).StartsWith("_run_summary", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("No matching JSON files found.");
            return 0;
        }

        Console.WriteLine($"Scanning {files.Count} file(s) in {Path.GetFullPath(dir)}");
        if (dryRun) Console.WriteLine("(DRY RUN — no files will be modified)");
        Console.WriteLine();

        var changed = 0;
        var unchanged = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            try
            {
                var raw = File.ReadAllText(file);
                ExtractionResult? doc;
                try
                {
                    doc = JsonSerializer.Deserialize<ExtractionResult>(raw, ExtractionJson.Compact);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"  SKIP {name}: not an ExtractionResult ({ex.Message})");
                    skipped++;
                    continue;
                }

                if (doc is null)
                {
                    Console.WriteLine($"  SKIP {name}: deserialized to null");
                    skipped++;
                    continue;
                }

                var sanitized = ResultSanitizer.Sanitize(doc);
                var newJson = JsonSerializer.Serialize(sanitized, ExtractionJson.Default) + "\n";

                if (NormalizeForCompare(newJson) == NormalizeForCompare(raw))
                {
                    unchanged++;
                    continue;
                }

                if (!dryRun)
                {
                    if (!noBackup)
                    {
                        var bak = file + ".bak";
                        if (!File.Exists(bak)) File.Copy(file, bak);
                    }
                    File.WriteAllText(file, newJson);
                }
                Console.WriteLine($"  FIX  {name}");
                changed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERR  {name}: {ex.Message}");
                skipped++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Done. {changed} fixed, {unchanged} already clean, {skipped} skipped.");
        if (changed > 0 && !dryRun && !noBackup)
        {
            Console.WriteLine("Backups stored as <file>.bak (only first time per file).");
        }
        return 0;
    }

    private static bool MatchesVendor(string filePath, HashSet<string> vendors)
    {
        if (vendors.Count == 0) return true;
        var stem = Path.GetFileNameWithoutExtension(filePath);
        var underscore = stem.IndexOf('_');
        if (underscore <= 0) return false;
        var prefix = stem[..underscore];
        return vendors.Contains(prefix);
    }

    /// Whitespace-insensitive comparison so we don't flag files as "changed" when only
    /// trailing whitespace or line-ending normalisation would differ.
    private static string NormalizeForCompare(string s) =>
        s.Replace("\r\n", "\n").TrimEnd();

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            normalize — coerce null nested objects in stored extraction JSONs to schema defaults.

            USAGE:
              dotnet run --project Accountant.Processors -- normalize
                  --dir <path>
                  [--vendor <claude|codex|gemini|comma-list>]
                  [--dry-run] [--no-backup]

            EXAMPLES:
              normalize --dir ../docs/facturi
              normalize --dir ../docs/facturi --vendor claude
              normalize --dir ../docs/facturi --vendor claude,gemini --dry-run

            DETAILS:
              - Reads every <Vendor>_<stem>.json (filtered by --vendor if given).
              - Skips _run_summary*.json and files that don't deserialise to ExtractionResult.
              - Replaces `fiscal: null`, `document: null`, `supplier: null`, `customer: null`,
                `totals: null`, `image_quality: null`, `confidence: null` and similar with
                the schema-required object shape (sub-fields populated as null).
              - Replaces null lists (`vat_breakdown`, `payments`, `line_items`, `errors`,
                `warnings`, `checks`, `extraction_warnings`, `issues`) with `[]`.
              - Writes a `<file>.bak` backup the first time a file is modified
                (suppress with --no-backup).
              - Use --dry-run to see what would change without writing.

            NO API CALLS. Free, deterministic, repeatable.
            """);
    }
}
