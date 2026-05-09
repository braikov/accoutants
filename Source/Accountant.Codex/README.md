# Accountant.Codex

OpenAI-backed implementation of `IAccountingDocumentExtractor`.

**Status:** ✅ Implemented. Owned by the Codex agent.

## Shape

- `CodexPrompt.cs` - prompt version and extraction instructions.
- `CodexExtractor.cs` - OpenAI SDK call, forced `extract_document` tool, parsing into `ModelExtractionInput`, validation, provider metadata.
- `ImageLoader.cs` - local image loading and resize fallback for OpenAI image size limits.

## Smoke test

```powershell
dotnet run --project Source/Accountant.Processors -- extract --vendor codex --dir docs/facturi --limit 1
```

## Do not touch

- `Source/Accountant.Contracts/` - shared territory. DTOs, validators, JSON config are authoritative.
- `Unified_Extraction_Contract/` - schema and rules. Changes go through the user.
- `Source/Accountant.Claude/` - owned by the Claude agent.
