# Agent Instructions

These instructions apply to the whole repository. `AGENTS.md` and `CLAUDE.md` are kept in sync — update both in the same change set when project-wide rules change.

## Project Documentation

- Use `docs/Project_Structure.md` as the map of the solution structure, project responsibilities, and where new files belong.
- Put unfinished work, known gaps, and deferred cleanup in `docs/followups.md` so they are not lost between sessions.
- When project structure or follow-up conventions change, update the relevant document in the same change set.

## Working Language

- The user collaborates in Bulgarian. Respond in Bulgarian.
- Code identifiers, code comments, log messages, exception messages, commit messages, branch names — **English only**.
- Documentation (Markdown under `docs/` and `Unified_Extraction_Contract/`) is mostly in English; user-facing notes and changelog entries may be Bulgarian when the audience is the user.
- Emoji are allowed in UI strings and documentation when expressly appropriate. Do not use emoji in code.

## Repository Layout

The repo combines two layers — both important, with different lifecycles:

- **C# implementation** under `Source/` is the active build target. New extraction work, Web UI, and validators live here.
- **Python research artifacts** under `Claude/`, `Codex/`, `Gemini/`, `Unified_Extraction_Contract/`, `ReviewSite/`, `docs/facturi/` are the **frozen reference**: schema, R1-R13 normalization rules, the 23-document test corpus, and historical vendor outputs that the C# implementation must benchmark against.

C# work **reads from but does not modify** the Python research layer, with one exception: `Unified_Extraction_Contract/` is the source of truth for the extraction schema and rules. When the contract changes, update the docs there first, then mirror in C# DTOs and validators — never the other way around.

## Source of Truth for Extraction

- **Schema:** `Unified_Extraction_Contract/accountant.document.v2.schema.json`
- **Spec:** `Unified_Extraction_Contract/EXTRACTION_CONTRACT.md`
- **Normalization rules R1-R13:** `Unified_Extraction_Contract/NORMALIZATION_RULES.md`
- **Example output:** `Unified_Extraction_Contract/example.result.json`

Any C# change touching `ExtractionResult`, validators, or normalization helpers must remain consistent with these documents. `Accountant.Tests` is the contract conformance gate — run it before claiming an extractor implementation is done.

## Git Workflow

- **Never commit without explicit user approval.** The user reviews changes before they land.
- Each agent commits only the work it has performed in the current session. If the working tree contains files the current agent has not touched, treat them as belonging to the user or to another agent and ask before staging.
- Conflicts — overlapping changes, unfamiliar files, unexpected branches — are escalated to the user, not resolved silently.
- Commit messages are written in English. Use a `Co-Authored-By` trailer to identify the AI assistant.

## Working Tree Hygiene

- A new task should preferably start from a clean working tree. If `git status` shows uncommitted changes from the current task, continue carefully. If the changes are unrelated, unclear, or owned by another agent/user, ask the user how to proceed before editing or staging.
- Do not silently fold files from a previous in-progress task into a new commit.

## Multi-Agent Collaboration

- Multiple AI agents (Claude, Codex, Gemini) and the user collaborate on this repository. Vendor parallelism is real — each `Accountant.<Vendor>` project is owned in spirit by the corresponding agent during prompt iteration.
- Coordinate via the user when scopes overlap; do not unilaterally rewrite or commit another agent's in-progress work.
- The `Unified_Extraction_Contract/` and shared C# projects (`Accountant.Contracts`, `Accountant.Web`, `Accountant.Tests`) are shared territory — changes there should be coordinated through the user.

## Local Services

- `Accountant.Web` will run on a dev port (TBD when first wired). Document the port in this file when set.
- Running .NET instances hold DLL locks; stop the relevant service before:
  - rebuilding the same project (`dotnet build`)
  - generating or applying EF migrations (`dotnet ef migrations add` / `dotnet ef database update`)

## Status Conventions

- Task status icons in `docs/tasks/README.md`:
  - `⏳ Чакаща` — not started.
  - `⏳ Дизайн only` — design captured; no implementation yet.
  - `🔄 В процес` — actively in progress.
  - `⏸ Замразена` — paused on purpose.
  - `✅ Завършена` — all in-scope phases closed.
- Follow-ups in `docs/followups.md`:
  - `🔒 blocked` — depends on an external decision.
  - `🟡 ready` — actionable when picked up.
  - On close, remove the line; git log preserves history.

## Task Documents

- Every implementation change starts with a task in `docs/tasks/`.
- The task must be reviewed/updated before code work begins. Do not implement work that has no task.
- Track task progress in `docs/tasks/README.md`.
- When scope changes during implementation, update the relevant task document in the same change set.

## Testing

- **Backend business logic / services / validators / normalization** → unit test in `Accountant.Tests` (xUnit). The R1-R13 rules and the EIK/IBAN/BIC validators are deterministic — they belong here and should have full coverage.
- **Vendor extractor implementations** (`Accountant.Claude`, `Accountant.Codex`, `Accountant.Gemini`) → integration tests are run manually (or scheduled), not on every build, because they cost money. Snapshot results against the historical reference outputs in `docs/facturi/`.
- **Pure refactor / config** without behavior change → no new tests required, but do not break existing ones.

Не се пишат тестове "защото правилото казва" — ако функционалността е trivial DI wiring или config flag, тест не носи value. Преценявай.

Когато добавяш тест: винаги покривай happy path първо. Edge cases и error states се добавят само ако реална регресия ги е оправдала или ако сложността на feature-а ги изисква.
