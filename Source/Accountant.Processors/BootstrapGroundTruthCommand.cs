using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Accountant.Contracts;

namespace Accountant.Processors;

/// `bootstrap-ground-truth` — for each image in --dir, build a consensus `extraction` block
/// from existing vendor outputs (Claude_*, Codex_*, Gemini_*) and write it as
/// `<stem>.ground_truth.json`. Idempotent: never overwrites existing ground truth files
/// unless --force is passed. Also writes `_bootstrap_report.md` listing fields where vendors
/// disagreed (your manual review checklist).
internal static class BootstrapGroundTruthCommand
{
    private static readonly string[] Vendors = ["Claude", "Codex", "Gemini"];
    private const string GroundTruthSubdir = "ground_truth";

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        string? dir = null;
        var force = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dir": dir = args[++i]; break;
                case "--force": force = true; break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 2;
            }
        }

        if (dir is null) { Console.Error.WriteLine("--dir <path> is required."); return 2; }
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"--dir '{dir}' is not a directory."); return 2; }

        var gtDir = Path.Combine(dir, GroundTruthSubdir);
        Directory.CreateDirectory(gtDir);

        var stems = Directory.EnumerateFiles(dir, "*.*")
            .Where(f => IsImage(f))
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        if (stems.Count == 0) { Console.Error.WriteLine("No images found."); return 2; }

        Console.WriteLine($"Bootstrapping ground truth for {stems.Count} document(s) into {Path.GetFullPath(gtDir)}");
        Console.WriteLine();

        var report = new StringBuilder();
        report.AppendLine($"# Ground truth bootstrap report");
        report.AppendLine();
        report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Source folder: `{Path.GetFullPath(dir)}`");
        report.AppendLine();
        report.AppendLine("Each document below lists fields where vendors disagreed. The bootstrap chose the");
        report.AppendLine("majority value when 2+ vendors agreed (canonical comparison: whitespace, Cyrillic");
        report.AppendLine("look-alikes, decimal value), or the first non-null value otherwise. Manually verify");
        report.AppendLine("the choice against the source image and edit `<stem>.ground_truth.json` directly.");
        report.AppendLine();

        var written = 0;
        var skipped = 0;
        var totalDisagreements = 0;

        foreach (var stem in stems)
        {
            var gtPath = Path.Combine(gtDir, $"{stem}.ground_truth.json");
            if (File.Exists(gtPath) && !force)
            {
                Console.WriteLine($"  SKIP {stem} (ground truth already exists; use --force to overwrite)");
                skipped++;
                continue;
            }

            var vendorExtractions = LoadVendorExtractions(dir, stem);
            if (vendorExtractions.Count == 0)
            {
                Console.WriteLine($"  SKIP {stem} (no vendor outputs found)");
                skipped++;
                continue;
            }

            var (consensus, disagreements) = BuildConsensus(vendorExtractions);
            File.WriteAllText(gtPath, consensus.ToJsonString(JsonWriteOptions) + "\n");
            written++;
            totalDisagreements += disagreements.Count;

            Console.WriteLine($"  WRITE {stem}.ground_truth.json  ({vendorExtractions.Count} vendor input(s), {disagreements.Count} disagreement(s))");

            report.AppendLine($"## {stem}");
            report.AppendLine();
            if (disagreements.Count == 0)
            {
                report.AppendLine("All vendors agree. No manual review required.");
                report.AppendLine();
                continue;
            }

            report.AppendLine($"{disagreements.Count} field(s) needed a tie-break:");
            report.AppendLine();
            report.AppendLine("| Field | Chosen | Claude | Codex | Gemini | Source |");
            report.AppendLine("|---|---|---|---|---|---|");
            foreach (var (path, chosen, perVendor, source) in disagreements)
            {
                var claude = perVendor.GetValueOrDefault("Claude", "—");
                var codex = perVendor.GetValueOrDefault("Codex", "—");
                var gemini = perVendor.GetValueOrDefault("Gemini", "—");
                report.AppendLine($"| `{path}` | {Md(chosen)} | {Md(claude)} | {Md(codex)} | {Md(gemini)} | {source} |");
            }
            report.AppendLine();
        }

        var reportPath = Path.Combine(gtDir, "_bootstrap_report.md");
        File.WriteAllText(reportPath, report.ToString());

        Console.WriteLine();
        Console.WriteLine($"Done. {written} written, {skipped} skipped, {totalDisagreements} total disagreement(s) across all documents.");
        Console.WriteLine($"Review checklist: {reportPath}");
        return 0;
    }

    private static bool IsImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp";

    private static Dictionary<string, JsonNode> LoadVendorExtractions(string dir, string stem)
    {
        var result = new Dictionary<string, JsonNode>();
        foreach (var vendor in Vendors)
        {
            var path = Path.Combine(dir, $"{vendor}_{stem}.json");
            if (!File.Exists(path)) continue;
            try
            {
                var raw = File.ReadAllText(path);
                var root = JsonNode.Parse(raw);
                var extraction = root?["extraction"];
                if (extraction is not null) result[vendor] = extraction.DeepClone();
            }
            catch (JsonException) { /* skip malformed */ }
        }
        return result;
    }

    private record DisagreementRow(string Path, string Chosen, Dictionary<string, string> PerVendor, string Source);

    private static (JsonNode consensus, List<DisagreementRow> disagreements) BuildConsensus(
        Dictionary<string, JsonNode> vendorExtractions)
    {
        // Take Claude's structure as the template for shape (object keys, list lengths).
        // For each leaf value, look up what each vendor produced and majority-vote.
        var template = vendorExtractions.GetValueOrDefault("Claude")
            ?? vendorExtractions.GetValueOrDefault("Codex")
            ?? vendorExtractions.GetValueOrDefault("Gemini")
            ?? throw new InvalidOperationException("No vendor extraction available.");

        var result = template.DeepClone();
        var disagreements = new List<DisagreementRow>();
        ResolvePathRecursive(result, "extraction", vendorExtractions, disagreements);
        return (result, disagreements);
    }

    private static void ResolvePathRecursive(
        JsonNode node,
        string path,
        Dictionary<string, JsonNode> vendorExtractions,
        List<DisagreementRow> disagreements)
    {
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj.ToList())
            {
                if (kvp.Value is null) { obj[kvp.Key] = ResolveLeaf($"{path}.{kvp.Key}", vendorExtractions, disagreements); continue; }
                if (kvp.Value is JsonObject || kvp.Value is JsonArray)
                {
                    ResolvePathRecursive(kvp.Value, $"{path}.{kvp.Key}", vendorExtractions, disagreements);
                }
                else
                {
                    obj[kvp.Key] = ResolveLeaf($"{path}.{kvp.Key}", vendorExtractions, disagreements);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                var child = arr[i];
                if (child is null) continue;
                if (child is JsonObject || child is JsonArray)
                {
                    ResolvePathRecursive(child, $"{path}[{i}]", vendorExtractions, disagreements);
                }
                else
                {
                    arr[i] = ResolveLeaf($"{path}[{i}]", vendorExtractions, disagreements);
                }
            }
        }
    }

    private static JsonNode? ResolveLeaf(
        string fullPath,
        Dictionary<string, JsonNode> vendorExtractions,
        List<DisagreementRow> disagreements)
    {
        // fullPath starts with "extraction." — strip and walk the path in each vendor.
        var relativePath = fullPath.StartsWith("extraction.") ? fullPath["extraction.".Length..] : fullPath;

        var perVendor = new Dictionary<string, string>();
        var perVendorRaw = new Dictionary<string, JsonNode?>();
        foreach (var (vendor, ext) in vendorExtractions)
        {
            var node = WalkPath(ext, relativePath);
            perVendorRaw[vendor] = node;
            perVendor[vendor] = node is null ? "null" : node.ToJsonString();
        }

        // Group by canonical form to find majority.
        var groups = perVendor
            .GroupBy(kvp => Canonical(kvp.Value))
            .OrderByDescending(g => g.Count())
            .ToList();

        if (groups.Count == 1)
        {
            // All agree (or all null) — just return Claude's (or first) raw value.
            var pick = perVendorRaw.GetValueOrDefault("Claude") ?? perVendorRaw.Values.FirstOrDefault(v => v is not null);
            return pick?.DeepClone();
        }

        var topGroup = groups[0];
        if (topGroup.Count() >= 2)
        {
            // Majority. Pick the raw value from one of the agreeing vendors.
            var winningVendor = topGroup.First().Key;
            var chosen = perVendorRaw[winningVendor];
            disagreements.Add(new DisagreementRow(
                Path: fullPath,
                Chosen: chosen?.ToJsonString() ?? "null",
                PerVendor: perVendor,
                Source: $"majority ({string.Join("+", topGroup.Select(g => g.Key))})"));
            return chosen?.DeepClone();
        }

        // No majority — pick the first non-null vendor value (Claude, then Codex, then Gemini).
        foreach (var v in Vendors)
        {
            if (perVendorRaw.TryGetValue(v, out var value) && value is not null)
            {
                disagreements.Add(new DisagreementRow(
                    Path: fullPath,
                    Chosen: value.ToJsonString(),
                    PerVendor: perVendor,
                    Source: $"first-non-null ({v})"));
                return value.DeepClone();
            }
        }

        return null;
    }

    private static JsonNode? WalkPath(JsonNode root, string path)
    {
        JsonNode? cur = root;
        var segments = SplitPath(path);
        foreach (var seg in segments)
        {
            if (cur is null) return null;
            if (seg.StartsWith('['))
            {
                var idx = int.Parse(seg[1..^1], CultureInfo.InvariantCulture);
                if (cur is JsonArray arr && idx < arr.Count) cur = arr[idx];
                else return null;
            }
            else
            {
                if (cur is JsonObject obj && obj.ContainsKey(seg)) cur = obj[seg];
                else return null;
            }
        }
        return cur;
    }

    private static List<string> SplitPath(string path)
    {
        var segments = new List<string>();
        var current = new StringBuilder();
        foreach (var c in path)
        {
            if (c == '.') { if (current.Length > 0) { segments.Add(current.ToString()); current.Clear(); } }
            else if (c == '[') { if (current.Length > 0) { segments.Add(current.ToString()); current.Clear(); } current.Append(c); }
            else if (c == ']') { current.Append(c); segments.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        if (current.Length > 0) segments.Add(current.ToString());
        return segments;
    }

    /// Canonical form for equality checks. Aligns with ReviewSite's diff normalization:
    /// whitespace around punctuation, Cyrillic look-alikes, decimal-equivalent strings.
    private static string Canonical(string s)
    {
        if (s == "null") return s;
        var trimmed = s.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed)) return "null";

        // Whitespace normalization
        trimmed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"([.,:;])(?=\S)", "$1 ");
        trimmed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ");

        // Cyrillic look-alike normalization
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            sb.Append(CyrillicConfusables.GetValueOrDefault(c, c));
        }
        var lookalike = sb.ToString();

        // Decimal equivalence: "20" == "20.00", "1.5" == "1.500"
        if (decimal.TryParse(lookalike, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
            return d.ToString(CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');

        return lookalike.ToLowerInvariant();
    }

    private static readonly Dictionary<char, char> CyrillicConfusables = new()
    {
        ['А'] = 'A', ['В'] = 'B', ['Е'] = 'E', ['К'] = 'K', ['М'] = 'M',
        ['Н'] = 'H', ['О'] = 'O', ['Р'] = 'P', ['С'] = 'C', ['Т'] = 'T',
        ['У'] = 'Y', ['Х'] = 'X', ['І'] = 'I',
        ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c',
        ['у'] = 'y', ['х'] = 'x', ['і'] = 'i',
    };

    private static string Md(string raw) => $"`{raw.Replace("|", "\\|").Replace("\n", " ")}`";

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            bootstrap-ground-truth — seed `<stem>.ground_truth.json` files from vendor outputs.

            USAGE:
              dotnet run --project Accountant.Processors -- bootstrap-ground-truth
                  --dir <path>
                  [--force]

            EXAMPLES:
              bootstrap-ground-truth --dir ../docs/facturi
              bootstrap-ground-truth --dir ../docs/facturi --force

            DETAILS:
              - For each image stem in --dir, reads existing Claude_<stem>.json, Codex_<stem>.json,
                Gemini_<stem>.json and produces a consensus `extraction` block.
              - Per leaf field: majority vote (2+ vendors agree using canonical comparison) wins.
                If no majority, first non-null value wins (Claude > Codex > Gemini).
              - Writes `<dir>/ground_truth/<stem>.ground_truth.json` (just the extraction block).
              - Writes `<dir>/ground_truth/_bootstrap_report.md` listing per-document disagreements
                so you can manually review and edit the chosen values.
              - Idempotent: skips existing ground truth files unless --force.

            REVIEW WORKFLOW:
              1. Run bootstrap.
              2. Open the report; for each disagreement, look at the source image.
              3. Edit `<stem>.ground_truth.json` directly to set the correct value.
              4. Re-run `evaluate` to measure each vendor's accuracy against ground truth.
            """);
    }
}
