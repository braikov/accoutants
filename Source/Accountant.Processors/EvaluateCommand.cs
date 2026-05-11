using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Accountant.Processors;

/// `evaluate` — compare each vendor's `extraction` against ground truth per document,
/// emit a Markdown scorecard to docs/evaluation/<timestamp>.md.
internal static class EvaluateCommand
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
        string? outDir = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dir": dir = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                default:
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 2;
            }
        }

        if (dir is null) { Console.Error.WriteLine("--dir <path> is required."); return 2; }
        if (!Directory.Exists(dir)) { Console.Error.WriteLine($"--dir '{dir}' is not a directory."); return 2; }

        var gtDir = Path.Combine(dir, GroundTruthSubdir);
        if (!Directory.Exists(gtDir))
        {
            Console.Error.WriteLine($"No ground truth folder at {gtDir}. Run bootstrap-ground-truth first.");
            return 2;
        }

        var gtFiles = Directory.EnumerateFiles(gtDir, "*.ground_truth.json").OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (gtFiles.Count == 0) { Console.Error.WriteLine("No *.ground_truth.json files found."); return 2; }

        Console.WriteLine($"Evaluating {gtFiles.Count} document(s) against ground truth.");
        Console.WriteLine();

        var docResults = new List<DocResult>();
        foreach (var gtFile in gtFiles)
        {
            var stem = Path.GetFileName(gtFile).Replace(".ground_truth.json", "");
            var gtRoot = JsonNode.Parse(File.ReadAllText(gtFile));
            if (gtRoot is null) { Console.WriteLine($"  SKIP {stem} (ground truth unparsable)"); continue; }

            var perVendor = new Dictionary<string, VendorResult>();
            foreach (var vendor in Vendors)
            {
                var vendorPath = Path.Combine(dir, $"{vendor}_{stem}.json");
                if (!File.Exists(vendorPath))
                {
                    perVendor[vendor] = new VendorResult(0, 0, 0, [new("(file)", "MISSING", "—", "missing")], []);
                    continue;
                }
                try
                {
                    var vendorRoot = JsonNode.Parse(File.ReadAllText(vendorPath));
                    var extraction = vendorRoot?["extraction"];
                    if (extraction is null) { perVendor[vendor] = new VendorResult(0, 0, 0, [new("(extraction)", "missing", "—", "missing block")], []); continue; }
                    perVendor[vendor] = Compare(gtRoot, extraction);
                }
                catch (Exception ex)
                {
                    perVendor[vendor] = new VendorResult(0, 0, 0, [new("(parse)", "error", "—", ex.Message)], []);
                }
            }
            docResults.Add(new DocResult(stem, perVendor));
            var line = string.Join("  ", Vendors.Select(v =>
            {
                var r = perVendor[v];
                return $"{v}: {r.Match}/{r.Match + r.Mismatch} ({Pct(r.Match, r.Match + r.Mismatch)}%)";
            }));
            Console.WriteLine($"  {stem}  {line}");
        }

        var ts = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        var outPath = Path.Combine(outDir ?? Path.Combine(dir, "..", "evaluation"), $"{ts}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var report = BuildReport(ts, dir, docResults);
        File.WriteAllText(outPath, report);

        Console.WriteLine();
        Console.WriteLine($"Report: {Path.GetFullPath(outPath)}");
        return 0;
    }

    private record FieldMismatch(string Path, string Expected, string Actual, string Note);
    private record VendorResult(int Match, int Mismatch, int Skipped, List<FieldMismatch> Mismatches, List<string> MatchedPaths);
    private record DocResult(string Stem, Dictionary<string, VendorResult> PerVendor);

    // Critical fields = numbers and identifiers that affect accounting / posting.
    // Distinct from cosmetic fields (addresses, descriptions, MOL, fiscal qr_code etc.)
    // where extraction noise has no business impact.
    private static readonly string[] CriticalFieldPatterns =
    [
        "extraction.document.number",
        "extraction.document.date",
        "extraction.document.tax_event_date",
        "extraction.document.due_date",
        "extraction.document.currency",
        "extraction.document.exchange_rate",
        "extraction.supplier.eik",
        "extraction.supplier.vat_number",
        "extraction.customer.eik",
        "extraction.customer.vat_number",
        "extraction.totals.",          // .net, .vat, .gross, .discount, .rounding, .amount_due
        "extraction.vat_breakdown[",   // [N].rate / .net / .vat / .gross
        "extraction.line_items[",      // [N].quantity / .unit_price / .vat_rate / .net / .vat / .gross / .discount_pct
        "extraction.payments[",        // [N].amount / .iban / .bic / .currency
        "extraction.document_type",
    ];

    private static bool IsCriticalField(string path)
    {
        foreach (var pat in CriticalFieldPatterns)
        {
            if (path == pat || path.StartsWith(pat, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    // Subset of critical fields that are MONEY values specifically.
    private static bool IsMoneyField(string path) =>
        (path.StartsWith("extraction.totals.", StringComparison.Ordinal)
         && path != "extraction.totals.")
        || path.EndsWith(".net", StringComparison.Ordinal)
        || path.EndsWith(".vat", StringComparison.Ordinal)
        || path.EndsWith(".gross", StringComparison.Ordinal)
        || path.EndsWith(".amount", StringComparison.Ordinal)
        || path.EndsWith(".amount_due", StringComparison.Ordinal)
        || path.EndsWith(".unit_price", StringComparison.Ordinal)
        || path.EndsWith(".rate", StringComparison.Ordinal)
        || path.EndsWith(".vat_rate", StringComparison.Ordinal)
        || path.EndsWith(".discount_pct", StringComparison.Ordinal)
        || path.EndsWith(".discount", StringComparison.Ordinal)
        || path.EndsWith(".rounding", StringComparison.Ordinal);

    private static VendorResult Compare(JsonNode expected, JsonNode actual)
    {
        var mismatches = new List<FieldMismatch>();
        var matched = new List<string>();
        var matchCount = 0;
        var mismatchCount = 0;
        Walk(expected, actual, "extraction", mismatches, matched, ref matchCount, ref mismatchCount);
        return new VendorResult(matchCount, mismatchCount, 0, mismatches, matched);
    }

    private static void Walk(JsonNode? exp, JsonNode? act, string path, List<FieldMismatch> mismatches, List<string> matched, ref int match, ref int mismatch)
    {
        // Both null at a leaf → match
        if (exp is null && act is null) { match++; matched.Add(path); return; }

        // Object descent
        if (exp is JsonObject expObj && act is JsonObject actObj)
        {
            foreach (var kvp in expObj)
            {
                actObj.TryGetPropertyValue(kvp.Key, out var actChild);
                Walk(kvp.Value, actChild, $"{path}.{kvp.Key}", mismatches, matched, ref match, ref mismatch);
            }
            return;
        }

        // Array descent — compare by index up to max length
        if (exp is JsonArray expArr && act is JsonArray actArr)
        {
            var len = Math.Max(expArr.Count, actArr.Count);
            for (var i = 0; i < len; i++)
            {
                var e = i < expArr.Count ? expArr[i] : null;
                var a = i < actArr.Count ? actArr[i] : null;
                Walk(e, a, $"{path}[{i}]", mismatches, matched, ref match, ref mismatch);
            }
            return;
        }

        // Type mismatch (e.g., expected object, got null) → mismatch
        if ((exp is null) != (act is null) ||
            (exp is JsonObject) != (act is JsonObject) ||
            (exp is JsonArray) != (act is JsonArray))
        {
            mismatch++;
            mismatches.Add(new FieldMismatch(path, ToStr(exp), ToStr(act), "shape mismatch"));
            return;
        }

        // Both are leaf values — compare canonically
        var expCanon = Canonical(ToStr(exp));
        var actCanon = Canonical(ToStr(act));
        if (expCanon == actCanon) { match++; matched.Add(path); return; }

        mismatch++;
        mismatches.Add(new FieldMismatch(path, ToStr(exp), ToStr(act), "value differs"));
    }

    private static string ToStr(JsonNode? n) => n is null ? "null" : n.ToJsonString();

    private static string Canonical(string s)
    {
        if (s == "null") return s;
        var trimmed = s.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed)) return "null";
        trimmed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"([.,:;])(?=\S)", "$1 ");
        trimmed = System.Text.RegularExpressions.Regex.Replace(trimmed, @"\s+", " ");
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            sb.Append(CyrillicConfusables.GetValueOrDefault(c, c));
        }
        var lookalike = sb.ToString();
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

    private static int Pct(int n, int d) => d == 0 ? 0 : (int)Math.Round(100.0 * n / d);

    private static (int match, int mismatch) AggregateFiltered(List<DocResult> docs, string vendor, Func<string, bool> filter)
    {
        var match = 0;
        var mismatch = 0;
        foreach (var doc in docs)
        {
            var r = doc.PerVendor.GetValueOrDefault(vendor);
            if (r is null) continue;
            match += r.MatchedPaths.Count(filter);
            mismatch += r.Mismatches.Count(m => filter(m.Path));
        }
        return (match, mismatch);
    }

    private static string BuildReport(string ts, string sourceDir, List<DocResult> docs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Vendor evaluation — {ts}");
        sb.AppendLine();
        sb.AppendLine($"Source: `{Path.GetFullPath(sourceDir)}`");
        sb.AppendLine($"Documents: {docs.Count}");
        sb.AppendLine();
        sb.AppendLine("Comparison is canonical (whitespace, Cyrillic look-alikes, decimal value normalised).");
        sb.AppendLine();

        // Vendor scorecard — all fields
        sb.AppendLine("## Vendor scorecard — all fields");
        sb.AppendLine();
        sb.AppendLine("| Vendor | Total fields | Matches | Mismatches | Accuracy |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var v in Vendors)
        {
            var totals = docs.Select(d => d.PerVendor.GetValueOrDefault(v))
                             .Where(r => r is not null)
                             .ToList();
            var match = totals.Sum(r => r!.Match);
            var mismatch = totals.Sum(r => r!.Mismatch);
            var total = match + mismatch;
            sb.AppendLine($"| {v} | {total} | {match} | {mismatch} | {Pct(match, total)}% |");
        }
        sb.AppendLine();

        // Critical-fields scorecard
        sb.AppendLine("## Vendor scorecard — critical accounting fields only");
        sb.AppendLine();
        sb.AppendLine("Critical = identifiers (`document.number`, `document.date`, `*.eik`, `*.vat_number`),");
        sb.AppendLine("`document_type`, all monetary fields (`totals.*`, `vat_breakdown[*].*`,");
        sb.AppendLine("`line_items[*].quantity / unit_price / vat_rate / discount_pct / net / vat / gross`),");
        sb.AppendLine("and `payments[*].amount / iban / bic / currency`. Excludes addresses, names, descriptions,");
        sb.AppendLine("MOL, fiscal operator, qr_code, notes, and other cosmetic fields.");
        sb.AppendLine();
        sb.AppendLine("| Vendor | Critical fields | Matches | Mismatches | Accuracy |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var v in Vendors)
        {
            var (m, mm) = AggregateFiltered(docs, v, IsCriticalField);
            sb.AppendLine($"| {v} | {m + mm} | {m} | {mm} | {Pct(m, m + mm)}% |");
        }
        sb.AppendLine();

        // Money-only scorecard (subset of critical)
        sb.AppendLine("## Vendor scorecard — money fields only");
        sb.AppendLine();
        sb.AppendLine("Subset of critical: only the numeric monetary fields and rates.");
        sb.AppendLine();
        sb.AppendLine("| Vendor | Money fields | Matches | Mismatches | Accuracy |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        foreach (var v in Vendors)
        {
            var (m, mm) = AggregateFiltered(docs, v, IsMoneyField);
            sb.AppendLine($"| {v} | {m + mm} | {m} | {mm} | {Pct(m, m + mm)}% |");
        }
        sb.AppendLine();

        // Critical mismatches grouped by field
        sb.AppendLine("## Critical-field mismatches (grouped)");
        sb.AppendLine();
        sb.AppendLine("| Field | Claude | Codex | Gemini | Total |");
        sb.AppendLine("|---|---:|---:|---:|---:|");
        var critFieldErrors = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var (vendor, vr) in doc.PerVendor)
            {
                foreach (var m in vr.Mismatches)
                {
                    if (!IsCriticalField(m.Path)) continue;
                    if (!critFieldErrors.TryGetValue(m.Path, out var byVendor))
                    {
                        byVendor = new Dictionary<string, int>(StringComparer.Ordinal);
                        critFieldErrors[m.Path] = byVendor;
                    }
                    byVendor[vendor] = byVendor.GetValueOrDefault(vendor) + 1;
                }
            }
        }
        foreach (var (path, byVendor) in critFieldErrors.OrderByDescending(kvp => kvp.Value.Values.Sum()).Take(30))
        {
            var c = byVendor.GetValueOrDefault("Claude");
            var co = byVendor.GetValueOrDefault("Codex");
            var g = byVendor.GetValueOrDefault("Gemini");
            sb.AppendLine($"| `{path}` | {c} | {co} | {g} | {c + co + g} |");
        }
        sb.AppendLine();

        // Per-field hardest
        sb.AppendLine("## Hardest fields (by total mismatches across all vendors)");
        sb.AppendLine();
        var fieldErrorCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var (_, vr) in doc.PerVendor)
            {
                foreach (var m in vr.Mismatches)
                {
                    fieldErrorCounts[m.Path] = fieldErrorCounts.GetValueOrDefault(m.Path) + 1;
                }
            }
        }
        sb.AppendLine("| Field | Mismatches |");
        sb.AppendLine("|---|---:|");
        foreach (var (path, count) in fieldErrorCounts.OrderByDescending(kvp => kvp.Value).Take(20))
        {
            sb.AppendLine($"| `{path}` | {count} |");
        }
        sb.AppendLine();

        // Per-document detail
        sb.AppendLine("## Per-document detail");
        sb.AppendLine();
        foreach (var doc in docs)
        {
            sb.AppendLine($"### {doc.Stem}");
            sb.AppendLine();
            sb.AppendLine("| Vendor | Match | Mismatch | Accuracy |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var v in Vendors)
            {
                var r = doc.PerVendor.GetValueOrDefault(v);
                if (r is null) { sb.AppendLine($"| {v} | — | — | — |"); continue; }
                var total = r.Match + r.Mismatch;
                sb.AppendLine($"| {v} | {r.Match} | {r.Mismatch} | {Pct(r.Match, total)}% |");
            }
            sb.AppendLine();

            var anyMismatches = doc.PerVendor.Any(p => p.Value.Mismatches.Count > 0);
            if (!anyMismatches) { sb.AppendLine("All vendors match ground truth."); sb.AppendLine(); continue; }

            // Pivot mismatches per field path
            var allPaths = doc.PerVendor.Values.SelectMany(v => v.Mismatches.Select(m => m.Path)).Distinct().OrderBy(p => p, StringComparer.Ordinal);
            sb.AppendLine("| Field | Expected | Claude | Codex | Gemini |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var path in allPaths)
            {
                var expected = doc.PerVendor.Values.SelectMany(v => v.Mismatches).FirstOrDefault(m => m.Path == path)?.Expected ?? "—";
                var cells = Vendors.Select(v =>
                {
                    var m = doc.PerVendor.GetValueOrDefault(v)?.Mismatches.FirstOrDefault(x => x.Path == path);
                    return m is null ? "✓" : Md(m.Actual);
                });
                sb.AppendLine($"| `{path}` | {Md(expected)} | {string.Join(" | ", cells)} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Md(string raw) => $"`{raw.Replace("|", "\\|").Replace("\n", " ")}`";

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            evaluate — compare vendor outputs against `<stem>.ground_truth.json` and report.

            USAGE:
              dotnet run --project Accountant.Processors -- evaluate
                  --dir <path>
                  [--out <path>]

            EXAMPLES:
              evaluate --dir ../docs/facturi
              evaluate --dir ../docs/facturi --out C:\reports\

            DETAILS:
              - Reads `<dir>/ground_truth/<stem>.ground_truth.json` files.
              - For each, compares Claude_<stem>.json, Codex_<stem>.json, Gemini_<stem>.json
                against the ground truth `extraction` block.
              - Comparison is canonical: whitespace around punctuation, Cyrillic look-alikes,
                and decimal-equivalent values are treated as equal.
              - Writes Markdown report to `<dir>/../evaluation/<timestamp>.md` (or --out).
              - Console shows per-document accuracy summary; report has full scorecard,
                hardest-fields ranking, and per-document mismatch tables.
            """);
    }
}
