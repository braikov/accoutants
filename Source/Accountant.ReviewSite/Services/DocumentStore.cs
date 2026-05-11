using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Accountant.ReviewSite.Services;

public sealed class DocumentStore
{
    public static readonly string[] AiNames = ["Codex", "Claude", "Gemini"];
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private readonly string _rootPath;
    private readonly string _contentRootPath;
    private readonly string? _configuredImageFolder;

    public DocumentStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _contentRootPath = environment.ContentRootPath;
        _rootPath = Directory.GetParent(environment.ContentRootPath)?.FullName
            ?? throw new InvalidOperationException("Cannot resolve project root.");
        _configuredImageFolder = configuration["Review:ImageFolder"];
    }

    public string ImageFolder => ResolveImageFolder();

    public IReadOnlyList<string> CandidateImageFolders => GetCandidateImageFolders();

    public IReadOnlyList<DocumentListItem> GetDocuments()
    {
        if (!Directory.Exists(ImageFolder))
        {
            return [];
        }

        return Directory.EnumerateFiles(ImageFolder)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var stem = Path.GetFileNameWithoutExtension(path);
                var results = AiNames.ToDictionary(ai => ai, ai => File.Exists(GetResultPath(stem, ai)));
                var bestSummary = AiNames
                    .Select(ai => GetResultSummary(stem, ai))
                    .FirstOrDefault(summary => summary.Exists) ?? ResultSummary.Empty;

                return new DocumentListItem(
                    fileName,
                    stem,
                    new FileInfo(path).Length,
                    results,
                    bestSummary.DocumentType,
                    bestSummary.DocumentNumber,
                    bestSummary.Supplier,
                    bestSummary.Gross,
                    bestSummary.Currency);
            })
            .ToList();
    }

    public DocumentDetail? GetDetail(string fileName)
    {
        var imagePath = ResolveImagePath(fileName);
        if (imagePath is null)
        {
            return null;
        }

        var docs = GetDocuments();
        var currentIndex = docs.ToList().FindIndex(doc => doc.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        var stem = Path.GetFileNameWithoutExtension(imagePath);
        var results = AiNames.Select(ai => GetResult(stem, ai)).ToList();
        var differences = BuildDifferences(results);

        return new DocumentDetail(
            fileName,
            stem,
            currentIndex > 0 ? docs[currentIndex - 1].FileName : null,
            currentIndex >= 0 && currentIndex < docs.Count - 1 ? docs[currentIndex + 1].FileName : null,
            results,
            differences);
    }

    public GroundTruthEditDetail? GetGroundTruthEdit(string fileName)
    {
        var imagePath = ResolveImagePath(fileName);
        if (imagePath is null) return null;

        var stem = Path.GetFileNameWithoutExtension(imagePath);
        var docs = GetDocuments();
        var idx = docs.ToList().FindIndex(d => d.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

        var groundTruthPath = GetGroundTruthFilePath(stem);
        var gtRoot = File.Exists(groundTruthPath) ? JsonNode.Parse(File.ReadAllText(groundTruthPath)) : null;

        var vendorExtractions = AiNames.ToDictionary(
            ai => ai,
            ai =>
            {
                var path = GetResultPath(stem, ai);
                if (!File.Exists(path)) return null;
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(path));
                    return root?["extraction"];
                }
                catch (JsonException) { return (JsonNode?)null; }
            });

        // Collect every leaf path that appears in any source.
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        CollectLeafPaths(gtRoot, "extraction", paths);
        foreach (var (_, node) in vendorExtractions)
            CollectLeafPaths(node, "extraction", paths);

        var rows = paths
            .OrderBy(EditRowPriority)
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(p =>
            {
                var rel = p[("extraction.".Length)..];
                var gt = ValueAsString(WalkPath(gtRoot, rel));
                var vendors = AiNames.ToDictionary(
                    ai => ai,
                    ai => ValueAsString(WalkPath(vendorExtractions[ai], rel)));
                var canonValues = vendors.Values.Append(gt)
                    .Select(CanonicalForComparison)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                return new EditRow(p, gt, vendors, canonValues.Count() > 1);
            })
            .ToList();

        return new GroundTruthEditDetail(
            fileName,
            stem,
            idx > 0 ? docs[idx - 1].FileName : null,
            idx >= 0 && idx < docs.Count - 1 ? docs[idx + 1].FileName : null,
            File.Exists(groundTruthPath),
            rows);
    }

    public bool SaveGroundTruth(string stem, IReadOnlyDictionary<string, string?> updates)
    {
        var groundTruthPath = GetGroundTruthFilePath(stem);
        Directory.CreateDirectory(Path.GetDirectoryName(groundTruthPath)!);

        JsonNode root = File.Exists(groundTruthPath)
            ? JsonNode.Parse(File.ReadAllText(groundTruthPath)) ?? new JsonObject()
            : new JsonObject();

        foreach (var (rawPath, rawValue) in updates)
        {
            var path = rawPath.StartsWith("extraction.") ? rawPath["extraction.".Length..] : rawPath;
            SetPath(root, path, rawValue);
        }

        // Backup once
        var bak = groundTruthPath + ".bak";
        if (File.Exists(groundTruthPath) && !File.Exists(bak))
            File.Copy(groundTruthPath, bak);

        File.WriteAllText(groundTruthPath, root.ToJsonString(GroundTruthWriteOptions) + "\n");
        return true;
    }

    private string GetGroundTruthFilePath(string stem) =>
        Path.Combine(ImageFolder, "ground_truth", $"{stem}.ground_truth.json");

    private static readonly JsonSerializerOptions GroundTruthWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void CollectLeafPaths(JsonNode? node, string path, ICollection<string> paths)
    {
        if (node is JsonObject obj)
        {
            foreach (var kvp in obj)
            {
                if (kvp.Value is JsonObject || kvp.Value is JsonArray)
                    CollectLeafPaths(kvp.Value, $"{path}.{kvp.Key}", paths);
                else
                    paths.Add($"{path}.{kvp.Key}");
            }
        }
        else if (node is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                var child = arr[i];
                if (child is JsonObject || child is JsonArray)
                    CollectLeafPaths(child, $"{path}[{i}]", paths);
                else
                    paths.Add($"{path}[{i}]");
            }
        }
        else
        {
            paths.Add(path);
        }
    }

    private static JsonNode? WalkPath(JsonNode? root, string relativePath)
    {
        if (root is null) return null;
        JsonNode? cur = root;
        foreach (var seg in SplitJsonPath(relativePath))
        {
            if (cur is null) return null;
            if (seg.StartsWith('['))
            {
                var idx = int.Parse(seg[1..^1]);
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

    private static void SetPath(JsonNode root, string relativePath, string? value)
    {
        var segments = SplitJsonPath(relativePath).ToList();
        if (segments.Count == 0) return;

        JsonNode cur = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var seg = segments[i];
            if (seg.StartsWith('['))
            {
                var idx = int.Parse(seg[1..^1]);
                if (cur is not JsonArray arr || idx >= arr.Count) return;
                cur = arr[idx]!;
            }
            else
            {
                if (cur is not JsonObject obj) return;
                if (!obj.ContainsKey(seg)) obj[seg] = new JsonObject();
                cur = obj[seg]!;
            }
        }

        var last = segments[^1];
        var newValue = ParseValue(value);
        if (last.StartsWith('['))
        {
            var idx = int.Parse(last[1..^1]);
            if (cur is JsonArray arr && idx < arr.Count) arr[idx] = newValue;
        }
        else
        {
            if (cur is JsonObject obj) obj[last] = newValue;
        }
    }

    private static JsonNode? ParseValue(string? input)
    {
        if (input is null) return null;
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(true);
        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(false);
        // Everything else is preserved as a string (decimal_string fields, names, etc.)
        return JsonValue.Create(input);
    }

    private static string? ValueAsString(JsonNode? node)
    {
        if (node is null) return null;
        if (node.GetValueKind() == JsonValueKind.Null) return null;
        if (node.GetValueKind() == JsonValueKind.String) return node.GetValue<string>();
        return node.ToJsonString();
    }

    private static IEnumerable<string> SplitJsonPath(string path)
    {
        var current = new System.Text.StringBuilder();
        foreach (var c in path)
        {
            if (c == '.') { if (current.Length > 0) { yield return current.ToString(); current.Clear(); } }
            else if (c == '[') { if (current.Length > 0) { yield return current.ToString(); current.Clear(); } current.Append(c); }
            else if (c == ']') { current.Append(c); yield return current.ToString(); current.Clear(); }
            else current.Append(c);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static int EditRowPriority(string path)
    {
        string[] order =
        [
            "extraction.document_type",
            "extraction.language",
            "extraction.country",
            "extraction.document.",
            "extraction.supplier.",
            "extraction.customer.",
            "extraction.totals.",
            "extraction.vat_breakdown",
            "extraction.payments",
            "extraction.line_items",
            "extraction.fiscal.",
        ];
        for (var i = 0; i < order.Length; i++)
        {
            if (path.StartsWith(order[i], StringComparison.Ordinal)) return i;
        }
        return order.Length;
    }

    public string? ResolveImagePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
        {
            return null;
        }

        var candidate = Path.GetFullPath(Path.Combine(ImageFolder, fileName));
        var imageRoot = Path.GetFullPath(ImageFolder);
        if (!candidate.StartsWith(imageRoot, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(candidate) || !ImageExtensions.Contains(Path.GetExtension(candidate)))
        {
            return null;
        }

        return candidate;
    }

    private AiResult GetResult(string stem, string ai)
    {
        var path = GetResultPath(stem, ai);
        if (!File.Exists(path))
        {
            return AiResult.Missing(ai);
        }

        var raw = File.ReadAllText(path);
        var pretty = PrettyJson(raw);
        var summary = ExtractSummary(raw);
        return new AiResult(ai, true, Path.GetFileName(path), pretty, summary);
    }

    private ResultSummary GetResultSummary(string stem, string ai)
    {
        var path = GetResultPath(stem, ai);
        return File.Exists(path) ? ExtractSummary(File.ReadAllText(path)) with { Exists = true } : ResultSummary.Empty;
    }

    private string GetResultPath(string stem, string ai) => Path.Combine(ImageFolder, $"{ai}_{stem}.json");

    private string ResolveImageFolder()
    {
        var candidates = GetCandidateImageFolders();
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private IReadOnlyList<string> GetCandidateImageFolders()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_configuredImageFolder))
        {
            candidates.Add(MakeAbsolute(_configuredImageFolder));
        }

        candidates.Add(Path.Combine(_contentRootPath, "docs", "facturi"));
        candidates.Add(Path.Combine(_rootPath, "docs", "facturi"));

        // Walk up from contentRoot looking for a docs/facturi folder. Lets the
        // app find the corpus regardless of how deeply the project is nested.
        var current = new DirectoryInfo(_contentRootPath);
        while (current is not null)
        {
            candidates.Add(Path.Combine(current.FullName, "docs", "facturi"));
            current = current.Parent;
        }

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string MakeAbsolute(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(_contentRootPath, path);

    private static string PrettyJson(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        return JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    private static ResultSummary ExtractSummary(string raw)
    {
        try
        {
            var root = JsonNode.Parse(raw);
            var extraction = root?["extraction"];
            var document = extraction?["document"];
            var supplier = extraction?["supplier"];
            var totals = extraction?["totals"];
            var validation = root?["validation"];
            var assessment = root?["model_assessment"]?["confidence"];
            var provider = root?["provider"];

            return new ResultSummary(
                true,
                extraction?["document_type"]?.GetValue<string?>(),
                document?["number"]?.GetValue<string?>(),
                document?["date"]?.GetValue<string?>(),
                supplier?["name"]?.GetValue<string?>(),
                totals?["gross"]?.GetValue<string?>(),
                document?["currency"]?.GetValue<string?>(),
                validation?["needs_review"]?.GetValue<bool?>(),
                assessment?["overall"]?.GetValue<decimal?>(),
                provider?["cost_estimate_usd"]?.GetValue<string?>());
        }
        catch
        {
            return ResultSummary.Empty with { Exists = true };
        }
    }

    private static IReadOnlyList<FieldDifference> BuildDifferences(IReadOnlyList<AiResult> results)
    {
        var valuesByPath = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var result in results.Where(result => result.Exists && result.PrettyJson is not null))
        {
            try
            {
                var root = JsonNode.Parse(result.PrettyJson!);
                AddComparableValues(valuesByPath, result.Ai, "extraction", root?["extraction"]);
                AddComparableValues(valuesByPath, result.Ai, "validation.needs_review", root?["validation"]?["needs_review"]);
            }
            catch
            {
                // Malformed JSON is already visible in the provider panel; skip it for field diffing.
            }
        }

        return valuesByPath
            .Where(item => HasDifference(item.Value))
            .OrderBy(item => DifferencePriority(item.Key))
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new FieldDifference(
                item.Key,
                AiNames.ToDictionary(ai => ai, ai => item.Value.TryGetValue(ai, out var value) ? value : "∅")))
            .ToList();
    }

    private static void AddComparableValues(
        IDictionary<string, Dictionary<string, string>> valuesByPath,
        string ai,
        string path,
        JsonNode? node)
    {
        if (node is null)
        {
            AddValue(valuesByPath, path, ai, "null");
            return;
        }

        if (node is JsonValue)
        {
            AddValue(valuesByPath, path, ai, NormalizeValue(node));
            return;
        }

        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                AddComparableValues(valuesByPath, ai, $"{path}.{property.Key}", property.Value);
            }
            return;
        }

        if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                AddComparableValues(valuesByPath, ai, $"{path}[{i}]", array[i]);
            }
        }
    }

    private static void AddValue(
        IDictionary<string, Dictionary<string, string>> valuesByPath,
        string path,
        string ai,
        string value)
    {
        if (!valuesByPath.TryGetValue(path, out var byAi))
        {
            byAi = new Dictionary<string, string>(StringComparer.Ordinal);
            valuesByPath[path] = byAi;
        }

        byAi[ai] = value;
    }

    private static string NormalizeValue(JsonNode node)
    {
        if (node.GetValueKind() == JsonValueKind.Null)
        {
            return "null";
        }

        if (node.GetValueKind() == JsonValueKind.String)
        {
            var value = node.GetValue<string?>()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? "∅" : value;
        }

        return node.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static bool HasDifference(IReadOnlyDictionary<string, string> valuesByAi)
    {
        var normalized = AiNames
            .Select(ai => valuesByAi.TryGetValue(ai, out var value) ? value : "∅")
            .Select(CanonicalForComparison)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return normalized > 1;
    }

    private static readonly Regex SpaceAfterPunct = new(@"([.,:;])(?=\S)", RegexOptions.Compiled);
    private static readonly Regex CollapseWhitespace = new(@"\s+", RegexOptions.Compiled);

    /// Same canonical form used by HasDifference, exposed so the editor can highlight diffs.
    public static string CanonicalForComparison(string? value)
    {
        if (value is null) return "null";
        return CanonicalForComparisonImpl(value);
    }

    private static string CanonicalForComparisonImpl(string value)
    {
        if (value is "null" or "∅") return value;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return trimmed;
        var withSpaces = SpaceAfterPunct.Replace(trimmed, "$1 ");
        var collapsed = CollapseWhitespace.Replace(withSpaces, " ");
        return MapConfusables(collapsed);
    }

    /// Cyrillic letters that look identical to their Latin counterparts. Vendors disagree
    /// at random which script they use ("PC" vs "РС"). For diff purposes treat as same.
    private static readonly Dictionary<char, char> CyrillicToLatinConfusables = new()
    {
        ['А'] = 'A', ['В'] = 'B', ['Е'] = 'E', ['К'] = 'K', ['М'] = 'M',
        ['Н'] = 'H', ['О'] = 'O', ['Р'] = 'P', ['С'] = 'C', ['Т'] = 'T',
        ['У'] = 'Y', ['Х'] = 'X', ['І'] = 'I',
        ['а'] = 'a', ['е'] = 'e', ['о'] = 'o', ['р'] = 'p', ['с'] = 'c',
        ['у'] = 'y', ['х'] = 'x', ['і'] = 'i',
    };

    private static string MapConfusables(string s)
    {
        Span<char> buf = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            buf[i] = CyrillicToLatinConfusables.TryGetValue(s[i], out var mapped) ? mapped : s[i];
        }
        return new string(buf);
    }

    private static int DifferencePriority(string path)
    {
        string[] important =
        [
            "extraction.document_type",
            "extraction.document.number",
            "extraction.document.date",
            "extraction.document.tax_event_date",
            "extraction.supplier.name",
            "extraction.supplier.eik",
            "extraction.supplier.vat_number",
            "extraction.customer.name",
            "extraction.customer.eik",
            "extraction.customer.vat_number",
            "extraction.totals.net",
            "extraction.totals.vat",
            "extraction.totals.gross",
            "validation.needs_review"
        ];

        var index = Array.IndexOf(important, path);
        return index >= 0 ? index : important.Length;
    }
}

public sealed record DocumentListItem(
    string FileName,
    string Stem,
    long SizeBytes,
    IReadOnlyDictionary<string, bool> Results,
    string? DocumentType,
    string? DocumentNumber,
    string? Supplier,
    string? Gross,
    string? Currency);

public sealed record DocumentDetail(
    string FileName,
    string Stem,
    string? PreviousFileName,
    string? NextFileName,
    IReadOnlyList<AiResult> Results,
    IReadOnlyList<FieldDifference> Differences);

public sealed record AiResult(
    string Ai,
    bool Exists,
    string? FileName,
    string? PrettyJson,
    ResultSummary Summary)
{
    public static AiResult Missing(string ai) => new(ai, false, null, null, ResultSummary.Empty);
}

public sealed record ResultSummary(
    bool Exists,
    string? DocumentType,
    string? DocumentNumber,
    string? Date,
    string? Supplier,
    string? Gross,
    string? Currency,
    bool? NeedsReview,
    decimal? OverallConfidence,
    string? CostEstimateUsd)
{
    public static ResultSummary Empty => new(false, null, null, null, null, null, null, null, null, null);
}

public sealed record FieldDifference(
    string Path,
    IReadOnlyDictionary<string, string> Values);

public sealed record EditRow(
    string Path,
    string? GroundTruth,
    IReadOnlyDictionary<string, string?> PerVendor,
    bool HasDisagreement);

public sealed record GroundTruthEditDetail(
    string FileName,
    string Stem,
    string? PreviousFileName,
    string? NextFileName,
    bool Exists,
    IReadOnlyList<EditRow> Rows);
