using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Schema;
using Accountant.Contracts;
using Accountant.Contracts.Validators;
using OpenAI.Chat;

namespace Accountant.Codex;

public sealed class CodexExtractorOptions
{
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "gpt-5.4-mini";
    public int MaxOutputTokens { get; init; } = 4096;
    // Published rates per 1M tokens for OpenAI gpt-5.4-mini (verified 2026-05-10 from
    // OpenAI pricing aggregators). Note: gpt-5 family produces invisible reasoning tokens
    // billed as output; those may not appear in our `output_tokens` counter, so the
    // computed cost can under-report actual billing. Override via Codex:CostInputPerMtok
    // / Codex:CostOutputPerMtok in user-secrets when switching to a different model.
    public decimal CostInputPerMtok { get; init; } = 0.75m;
    public decimal CostOutputPerMtok { get; init; } = 4.50m;
}

/// Extracts one or more images sequentially. Caller controls parallelism.
public sealed class CodexExtractor : IAccountingDocumentExtractor
{
    private readonly CodexExtractorOptions _options;
    private readonly ChatClient _client;
    private readonly ChatTool _extractDocumentTool;

    public CodexExtractor(CodexExtractorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("OPENAI_API_KEY is required.", nameof(options));
        _options = options;
        _client = new ChatClient(options.Model, options.ApiKey);
        _extractDocumentTool = BuildToolFunction();
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

        List<ChatMessage> messages =
        [
            new SystemChatMessage(CodexPrompt.SystemPrompt),
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart(
                    $"Extract this Bulgarian payment document. Filename: {fileName}. Schema: accountant.document.v2."),
                ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(image.Bytes), image.MediaType)),
        ];

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = _options.MaxOutputTokens,
            ToolChoice = ChatToolChoice.CreateFunctionChoice("extract_document"),
            AllowParallelToolCalls = false,
        };
        options.Tools.Add(_extractDocumentTool);

        var response = await _client.CompleteChatAsync(messages, options, cancellationToken);
        sw.Stop();
        var durationMs = (int)sw.ElapsedMilliseconds;
        var completion = response.Value;

        var inputTokens = completion.Usage?.InputTokenCount ?? 0;
        var outputTokens = completion.Usage?.OutputTokenCount ?? 0;
        // OpenAI returns the actual model that ran (may differ from what we requested
        // if the alias auto-routes or falls back). Capture it so the cost calculation
        // and the audit trail reflect reality, not what we configured.
        var actualModel = completion.Model ?? _options.Model;

        var toolCall = completion.ToolCalls.FirstOrDefault(t => t.FunctionName == "extract_document")
            ?? throw new InvalidOperationException(
                $"OpenAI did not call extract_document for '{fileName}'. Finish reason: {completion.FinishReason}");

        var modelInput = ModelInputSanitizer.Sanitize(DeserializeModelInput(toolCall.FunctionArguments));

        var source = new Source
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
            Engine = Engine.OpenAi,
            Model = actualModel,
            Pipeline = Pipeline.VisionDirect,
            OcrUsed = false,
            PromptVersion = CodexPrompt.PromptVersion,
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

    private static ModelExtractionInput DeserializeModelInput(BinaryData? input)
    {
        if (input is null)
            throw new InvalidOperationException("Tool input is null.");

        var node = JsonNode.Parse(input.ToString())
            ?? throw new InvalidOperationException("Tool input parsed to null.");
        NormalizeEvidence(node);

        return node.Deserialize<ModelExtractionInput>(ExtractionJson.Compact)
            ?? throw new InvalidOperationException("Tool input deserialized to null.");
    }

    private static void NormalizeEvidence(JsonNode node)
    {
        if (node is not JsonObject obj || obj["evidence"] is not JsonObject evidence)
            return;

        foreach (var (key, value) in evidence.ToList())
        {
            if (value is JsonObject)
                continue;

            if (value is null)
            {
                evidence[key] = new JsonObject
                {
                    ["text"] = null,
                    ["confidence"] = null,
                };
                continue;
            }

            evidence[key] = new JsonObject
            {
                ["text"] = value.ToString(),
                ["confidence"] = null,
            };
        }
    }

    private static ChatTool BuildToolFunction()
    {
        var schemaOptions = new JsonSerializerOptions(ExtractionJson.Default)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(schemaOptions, typeof(ModelExtractionInput));
        var cleaned = CleanSchema(schema);

        return ChatTool.CreateFunctionTool(
            functionName: "extract_document",
            functionDescription: "Submit the structured extraction of the Bulgarian payment document. " +
                                 "Call exactly once per document. The harness assembles the final v2 JSON " +
                                 "by adding source.file_*, validation, and provider metadata.",
            functionParameters: BinaryData.FromString(cleaned.ToJsonString()));
    }

    private static JsonNode CleanSchema(JsonNode node)
    {
        var clone = node.DeepClone();
        RemoveUnsupportedSchemaKeywords(clone);
        if (clone is JsonObject obj)
            obj["type"] = "object";
        return clone;
    }

    private static void RemoveUnsupportedSchemaKeywords(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove("$schema");
            obj.Remove("$id");

            foreach (var kvp in obj.ToList())
            {
                if (kvp.Value is not null)
                    RemoveUnsupportedSchemaKeywords(kvp.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                    RemoveUnsupportedSchemaKeywords(item);
            }
        }
    }
}
