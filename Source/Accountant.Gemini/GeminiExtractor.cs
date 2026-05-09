using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Accountant.Contracts;
using Accountant.Contracts.Validators;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;

namespace Accountant.Gemini;

public sealed class GeminiExtractorOptions
{
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "gemini-flash-latest";
    public decimal CostInputPerMtok { get; init; } = 0.075m;
    public decimal CostOutputPerMtok { get; init; } = 0.30m;
}

public sealed class GeminiExtractor : IAccountingDocumentExtractor
{
    private readonly GeminiExtractorOptions _options;
    private readonly GoogleAI _googleAI;
    private readonly Schema _responseSchema;

    public GeminiExtractor(GeminiExtractorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("GEMINI_API_KEY is required.", nameof(options));
        _options = options;
        _googleAI = new GoogleAI(apiKey: options.ApiKey);
        _responseSchema = BuildResponseSchema();
    }

    public async Task<IReadOnlyList<ExtractionResult>> ExtractAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExtractionResult>(filePaths.Count);
        foreach (var path in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ExtractOneAsync(path, cancellationToken));
        }
        return results;
    }

    private async Task<ExtractionResult> ExtractOneAsync(string filePath, CancellationToken cancellationToken)
    {
        var image = ImageLoader.Load(filePath, cancellationToken);
        var fileName = Path.GetFileName(filePath);
        var startedAtUtc = DateTime.UtcNow;
        var createdAtIso = startedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var model = _googleAI.GenerativeModel(_options.Model);
        
        var request = new GenerateContentRequest
        {
            Contents = new List<Content>
            {
                new Content
                {
                    Role = "user",
                    Parts = new List<IPart>
                    {
                        new Part { InlineData = new InlineData { MimeType = image.MediaType, Data = Convert.ToBase64String(image.Bytes) } },
                        new Part { Text = $"Extract this Bulgarian payment document. Filename: {fileName}. Schema: accountant.document.v2." }
                    }
                }
            },
            SystemInstruction = new Content
            {
                Role = "system",
                Parts = new List<IPart> { new Part { Text = GeminiPrompt.SystemPrompt } }
            },
            GenerationConfig = new GenerationConfig
            {
                Temperature = 0.0f,
                ResponseMimeType = "application/json",
                ResponseSchema = _responseSchema
            }
        };

        var response = await model.GenerateContent(request, cancellationToken: cancellationToken);
        sw.Stop();
        var durationMs = (int)sw.ElapsedMilliseconds;

        var inputTokens = response.UsageMetadata?.PromptTokenCount ?? 0;
        var outputTokens = response.UsageMetadata?.CandidatesTokenCount ?? 0;

        var jsonText = response.Text ?? throw new InvalidOperationException("Gemini returned empty text.");
        
        var modelInput = DeserializeModelInput(jsonText);

        var source = new Accountant.Contracts.Source
        {
            FileName = fileName,
            FilePath = filePath.Replace("\\", "/"),
            PageCount = 1,
            PageIndex = 0,
            DetectedDocumentCount = modelInput.DetectedDocumentCount ?? 1,
            ExtractedDocumentIndex = modelInput.ExtractedDocumentIndex ?? 0,
            ImageQuality = modelInput.ImageQuality,
        };

        var validation = ExtractionValidator.Validate(modelInput.Extraction, source, modelInput.ModelAssessment.Confidence);

        var provider = new Provider
        {
            Engine = Engine.Google,
            Model = _options.Model,
            Pipeline = Pipeline.VisionDirect,
            OcrUsed = false,
            PromptVersion = GeminiPrompt.PromptVersion,
            CreatedAt = createdAtIso,
            DurationMs = durationMs,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostEstimateUsd = CostEstimate(inputTokens, outputTokens),
        };

        return new ExtractionResult
        {
            Source = source,
            Extraction = modelInput.Extraction,
            Validation = validation,
            ModelAssessment = modelInput.ModelAssessment,
            Evidence = modelInput.Evidence,
            Provider = provider,
        };
    }

    private string CostEstimate(int inputTokens, int outputTokens)
    {
        var cost = (inputTokens * _options.CostInputPerMtok + outputTokens * _options.CostOutputPerMtok) / 1_000_000m;
        return cost.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static ModelExtractionInput DeserializeModelInput(string jsonText)
    {
        jsonText = jsonText.Trim();
        if (jsonText.StartsWith("```json"))
            jsonText = jsonText.Substring(7);
        if (jsonText.EndsWith("```"))
            jsonText = jsonText.Substring(0, jsonText.Length - 3);
        jsonText = jsonText.Trim();

        if (jsonText.StartsWith("[") && jsonText.EndsWith("]"))
        {
            var arr = JsonSerializer.Deserialize<JsonArray>(jsonText, ExtractionJson.Compact);
            if (arr != null && arr.Count > 0)
            {
                var first = arr[0];
                if (first != null)
                {
                    return first.Deserialize<ModelExtractionInput>(ExtractionJson.Compact)
                        ?? throw new InvalidOperationException("Tool input deserialized to null.");
                }
            }
        }

        return JsonSerializer.Deserialize<ModelExtractionInput>(jsonText, ExtractionJson.Compact)
            ?? throw new InvalidOperationException("Tool input deserialized to null.");
    }

    private static Schema BuildResponseSchema()
    {
        var exporterOptions = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };
        var schemaNode = JsonSchemaExporter.GetJsonSchemaAsNode(
            ExtractionJson.Default, typeof(ModelExtractionInput), exporterOptions);
        var cleaned = CleanSchema(schemaNode);
        return JsonSerializer.Deserialize<Schema>(cleaned.ToJsonString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static JsonNode CleanSchema(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var defs = obj["$defs"] as JsonObject;
            obj.Remove("$defs");
            obj.Remove("$id");
            obj.Remove("$schema");

            var resultNode = JsonNode.Parse(obj.ToJsonString())!;
            if (defs is not null) InlineRefs(resultNode, defs);
            NormalizeForGemini(resultNode);
            return resultNode;
        }
        return node;
    }

    private static void InlineRefs(JsonNode node, JsonObject defs)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("$ref"))
            {
                var refPath = obj["$ref"]!.ToString();
                var defName = refPath.Replace("#/$defs/", "");
                if (defs.ContainsKey(defName))
                {
                    obj.Remove("$ref");
                    var defObj = defs[defName]!.DeepClone().AsObject();
                    foreach (var kvp in defObj)
                    {
                        obj[kvp.Key] = kvp.Value?.DeepClone();
                    }
                }
            }
            foreach (var kvp in obj.ToList())
            {
                if (kvp.Value is not null) InlineRefs(kvp.Value, defs);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null) InlineRefs(item, defs);
            }
        }
    }

    /// Gemini's Schema deserializer requires:
    ///   1. type as a single string (not an array)
    ///   2. type values capitalized like the ParameterType enum names ("String", "Object", ...)
    ///   3. nullable: true to indicate optional fields (instead of "null" in a type union)
    /// JsonSchemaExporter emits lowercase types and union arrays for nullable; this normalises both.
    private static void NormalizeForGemini(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("additionalProperties");

            if (obj["type"] is JsonArray typesArray)
            {
                string? primary = null;
                var nullable = false;
                foreach (var item in typesArray)
                {
                    var s = item?.ToString();
                    if (s == "null") nullable = true;
                    else if (primary is null) primary = s;
                }
                if (primary is not null) obj["type"] = primary;
                else obj.Remove("type");
                if (nullable) obj["nullable"] = true;
            }

            // Enum entries from JsonSchemaExporter omit the type. Gemini rejects
            // enum without type=STRING. Default to String when none was set.
            if (obj.ContainsKey("enum") && !obj.ContainsKey("type"))
            {
                obj["type"] = "String";
            }

            if (obj["type"] is JsonValue tv && tv.TryGetValue<string>(out var typeStr))
            {
                obj["type"] = Capitalize(typeStr);
            }

            foreach (var kvp in obj.ToList())
            {
                if (kvp.Value is not null) NormalizeForGemini(kvp.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null) NormalizeForGemini(item);
            }
        }
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
}
