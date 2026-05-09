# Output Storage Convention

Each AI implementation must save every extracted document JSON in two locations.

## 1. Historical Run Folder

Save one immutable copy inside the implementation's run folder:

```text
{Implementation}/runs/{run_timestamp}/{AI}_{original_image_stem}.json
```

Examples:

```text
Codex/runs/2026-05-08_1530/Codex_20240213_190514.json
Claude/runs/2026-05-08_1530/Claude_20240213_190514.json
Gemini/runs/2026-05-08_1530/Gemini_20240213_190514.json
```

Purpose: preserve historical results so we can compare how extraction quality changes between model, prompt, schema, and validator versions.

The run folder may also contain:

```text
_run_summary.json
_run_report.md
```

## 2. Latest Result Next To Source Image

Save or overwrite the latest result next to the original image:

```text
{image_folder}/{AI}_{original_image_stem}.json
```

Example:

```text
docs/facturi/20240213_190514.jpg
docs/facturi/Codex_20240213_190514.json
docs/facturi/Claude_20240213_190514.json
docs/facturi/Gemini_20240213_190514.json
```

Purpose: make manual comparison easy. Opening the image folder should show the source image and the latest outputs from all AI implementations side by side.

## Naming Rules

Use this filename format:

```text
{AI}_{original_image_stem}.json
```

Allowed `{AI}` values:

```text
Codex
Claude
Gemini
```

Examples:

```text
Codex_20240213_190514.json
Claude_20240213_190514.json
Gemini_20240213_190514.json
```

The file next to the image is the latest result and may be overwritten on each run.

The file in `runs/{run_timestamp}` is historical and should not be modified after creation.

## JSON Metadata

Each JSON file must still include source and provider metadata:

```json
{
  "source": {
    "file_name": "20240213_190514.jpg",
    "file_path": "docs/facturi/20240213_190514.jpg"
  },
  "provider": {
    "engine": "openai",
    "model": "...",
    "prompt_version": "...",
    "created_at": "..."
  }
}
```
