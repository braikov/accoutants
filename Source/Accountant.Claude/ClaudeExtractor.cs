using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Accountant.Contracts;
using Accountant.Contracts.Validators;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using CommonTool = Anthropic.SDK.Common.Tool;

namespace Accountant.Claude;

public sealed class ClaudeExtractorOptions
{
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "claude-sonnet-4-6";
    public int MaxTokens { get; init; } = 4096;
    public decimal CostInputPerMtok { get; init; } = 3.00m;
    public decimal CostOutputPerMtok { get; init; } = 15.00m;
}

/// Extracts one or more images sequentially. Caller controls parallelism.
public sealed class ClaudeExtractor : IAccountingDocumentExtractor
{
    private readonly ClaudeExtractorOptions _options;
    private readonly AnthropicClient _client;
    private readonly Function _toolFunction;

    public ClaudeExtractor(ClaudeExtractorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("ANTHROPIC_API_KEY is required.", nameof(options));
        _options = options;
        _client = new AnthropicClient(new APIAuthentication(options.ApiKey));
        _toolFunction = BuildToolFunction();
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

        var userMessage = new Message
        {
            Role = RoleType.User,
            Content =
            [
                new ImageContent
                {
                    Source = new ImageSource
                    {
                        Type = SourceType.base64,
                        MediaType = image.MediaType,
                        Data = Convert.ToBase64String(image.Bytes),
                    },
                },
                new TextContent
                {
                    Text = $"Extract this Bulgarian payment document. Filename: {fileName}. Schema: accountant.document.v2.",
                },
            ],
        };

        var parameters = new MessageParameters
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = [new SystemMessage(ClaudePrompt.SystemPrompt, cacheControl: null!)],
            Messages = [userMessage],
            Tools = [new CommonTool(_toolFunction)],
            ToolChoice = new ToolChoice { Type = ToolChoiceType.Tool, Name = "extract_document" },
        };

        var response = await _client.Messages.GetClaudeMessageAsync(parameters, cancellationToken);
        sw.Stop();
        var durationMs = (int)sw.ElapsedMilliseconds;

        var inputTokens = response.Usage?.InputTokens ?? 0;
        var outputTokens = response.Usage?.OutputTokens ?? 0;
        // Capture the actual model that ran. Anthropic returns this on the response;
        // it normally matches what we requested but pin it for the audit trail.
        var actualModel = response.Model ?? _options.Model;

        var toolBlock = response.Content?.OfType<ToolUseContent>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Claude did not call extract_document for '{fileName}'. Stop reason: {response.StopReason}");

        var modelInput = ModelInputSanitizer.Sanitize(DeserializeModelInput(toolBlock.Input));

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
            Engine = Engine.Anthropic,
            Model = actualModel,
            Pipeline = Pipeline.VisionDirect,
            OcrUsed = false,
            PromptVersion = ClaudePrompt.PromptVersion,
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

    private static ModelExtractionInput DeserializeModelInput(JsonNode? input)
    {
        if (input is null)
            throw new InvalidOperationException("Tool input is null.");
        return input.Deserialize<ModelExtractionInput>(ExtractionJson.Compact)
            ?? throw new InvalidOperationException("Tool input deserialized to null.");
    }

    private static Function BuildToolFunction()
    {
        var exporterOptions = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
        };
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(
            ExtractionJson.Default,
            typeof(ModelExtractionInput),
            exporterOptions);
        return new Function(
            name: "extract_document",
            description: "Submit the structured extraction of the Bulgarian payment document. " +
                         "Call exactly once per document. The harness assembles the final v2 JSON " +
                         "by adding source.file_*, validation, and provider metadata.",
            parameters: schema);
    }
}
