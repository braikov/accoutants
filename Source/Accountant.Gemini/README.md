# Accountant.Gemini

Google Gemini-backed implementation of `IAccountingDocumentExtractor`.

**Status:** ⏳ Not yet implemented. Owned by the Gemini agent.

## Where to start

1. Read [`docs/tasks/0001_vendor_extractor_implementation.md`](../../docs/tasks/0001_vendor_extractor_implementation.md) end-to-end. It is the single source of truth for what to build, what to reuse, and how to wire in. Pay attention to the Gemini-specific notes (response schema dialect quirks; the array-wrapping bug from the Python research phase).
2. Read the reference implementation in [`Source/Accountant.Claude/`](../Accountant.Claude/) — same shape, different SDK. Mirror it.
3. Implement `GeminiExtractor`, `GeminiPrompt`, `GeminiExtractorOptions`. Wire into [`Source/Accountant.Processors/ExtractCommand.cs`](../Accountant.Processors/ExtractCommand.cs) `BuildExtractor` switch.
4. Smoke test: `dotnet run --project Accountant.Processors -- extract --vendor gemini --dir ../docs/facturi --limit 1`.

## Do not touch

- `Source/Accountant.Contracts/` — shared territory. DTOs, validators, JSON config are authoritative.
- `Unified_Extraction_Contract/` — schema and rules. Changes go through the user.
- `Source/Accountant.Claude/` — owned by the Claude agent.
