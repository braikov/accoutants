# Проектна структура: Accountant

Този документ описва архитектурата на C# (.NET 10 / C# 14) проекта Accountant — система за извличане на данни от изображения на български платежни документи (фактури, касови бонове, проформи и др.) през множество AI vendor-и.

Документът е жив. При промяна в структурата на solution-а или ролите на проектите — обнови този файл в същия change set.

## Текущ scope (research mode)

Проектът е в **research фаза** — продължение на Python research-а, но с C# tooling. Целта на C# solution-а на този етап е:

1. **Source control за vendor промпти и конфигурации** — всеки `Accountant.<Vendor>` проект съдържа конкретния prompt, model config, и call логиката за един vendor. Промените в промпти се track-ват в git.
2. **Споделен contract** (`Accountant.Contracts`) — единна C# имплементация на R1-R13 валидаторите и нормализацията, която всички vendor-и могат да use-ват.
3. **Console sandbox** (`Accountant.Processors`) — място за batch екстракция, diff между vendor-и, eval скриптове.

**НЕ е в scope сега:**

- Web UI работа (`Accountant.Web` остава скелет)
- Persistence (`Accountant.DataAccess`, `Accountant.MySql` остават скелет — без entities, без migrations)
- User management (`Accountant.Identity` остава скелет — потребителите ще дойдат по-късно: admin + клиенти, минимум две роли)
- Notifications

Тези проекти съществуват в solution-а, за да е готова структурата когато се мине в production-feature mode. Засега имат само празни `.csproj` файлове и placeholder.

## 1. Хранилище

**Mono-repo**, локално в `c:\Projects\Miro\Accountant\`. Засега не публикуван в GitHub.

Repo-то комбинира:
- **C# имплементация** (новата работа) — под `Source\`
- **Python research артефакти** — `Claude\`, `Codex\`, `Gemini\`, `Unified_Extraction_Contract\`, `ReviewSite\`, `docs\facturi\` (тестов corpus + референтни outputs)
- **Споделена документация** — `docs\`

Python research-ът е **frozen reference**. C# имплементацията не модифицира Python кода или историческите outputs — само ги чете като benchmark спрямо който трябва да съвпадне (или да обясни защо се различава).

## 2. Структура на root ниво

```text
c:\Projects\Miro\Accountant\
│
├── AGENTS.md                              # инструкции за всички AI агенти (sync-нат с CLAUDE.md)
├── CLAUDE.md                              # инструкции специално за Claude (sync-нат с AGENTS.md)
├── NuGet.Config                           # (бъдещ) NuGet feeds
│
├── docs\                                  # споделена документация
│   ├── Project_Structure.md               # този документ
│   ├── tasks\                             # план/прогрес на отделни tasks
│   │   └── README.md                      # task tracker
│   ├── followups.md                       # отложени cleanup-и, gaps между сесиите
│   └── facturi\                           # 23-doc тестов corpus + референтни Claude/Codex/Gemini JSON-и
│
├── Unified_Extraction_Contract\           # schema + правила (R1-R13) + example — source of truth за extraction формата
│   ├── EXTRACTION_CONTRACT.md
│   ├── NORMALIZATION_RULES.md
│   ├── accountant.document.v2.schema.json
│   ├── example.result.json
│   └── ...
│
├── Claude\, Codex\, Gemini\               # Python research (frozen, само за reference)
├── ReviewSite\                            # Python diff/review tooling (frozen)
│
└── Source\                                # C# solution (новата работа)
    ├── Accountant.slnx
    ├── Accountant.Web\                    # ASP.NET Core MVC
    ├── Accountant.DataAccess\             # entities, ApplicationDbContext, EF configurations
    ├── Accountant.MySql\                  # MySQL provider, EF migrations
    ├── Accountant.Identity\               # ApplicationUser, identity модели
    ├── Accountant.Contracts\              # IAccountingDocumentExtractor, ExtractionResult DTOs, R1-R13 валидатори, нормализация
    ├── Accountant.Claude\                 # IAccountingDocumentExtractor implementation (Anthropic SDK)
    ├── Accountant.Codex\                  # IAccountingDocumentExtractor implementation (OpenAI SDK)
    ├── Accountant.Gemini\                 # IAccountingDocumentExtractor implementation (Google AI SDK)
    ├── Accountant.Processors\             # console app — sandbox / batch processing CLI
    └── Accountant.Tests\                  # xUnit тестове (validators, normalization, contract conformance)
```

## 3. Solution структура (`Source\Accountant.slnx`)

```text
Source\
│
├── Accountant.Web              # (.NET MVC) Главното Web приложение, в което се прави UI работата по-натам.
│
├── Accountant.DataAccess       # (Class Library) Domain entities, ApplicationDbContext, IEntityTypeConfiguration. Без provider-specific NuGet пакети.
├── Accountant.MySql            # (Class Library) MySQL provider импл., EF Core Migrations, Pomelo.EntityFrameworkCore.MySql.
├── Accountant.Identity         # (Class Library) ApplicationUser, ASP.NET Core Identity модели и контекст.
│
├── Accountant.Contracts        # (Class Library) Споделените интерфейси (IAccountingDocumentExtractor), DTO-та (ExtractionResult и под-блоковете на v2 schema), enum-и, R1-R13 валидатори, нормализационни helpers.
│
├── Accountant.Claude           # (Class Library) IAccountingDocumentExtractor имплементация през Anthropic SDK (Claude Sonnet като primary VLM).
├── Accountant.Codex            # (Class Library) IAccountingDocumentExtractor имплементация през OpenAI SDK.
├── Accountant.Gemini           # (Class Library) IAccountingDocumentExtractor имплементация през Google.Cloud.AIPlatform или официален Gemini SDK.
│
├── Accountant.Processors       # (Console App) Sandbox / batch CLI за изпълнение на различни задачи: пакетна екстракция върху corpus, diff между vendor-и, нормализация на исторически JSON-и, eval скриптове.
│
└── Accountant.Tests            # (xUnit) Unit тестове върху валидаторите, нормализацията, contract conformance срещу schema-та.
```

## 4. Зависимости между проектите

**Активни сега (research scope):**

```text
Accountant.Claude / Codex / Gemini
  └── Accountant.Contracts          (имплементират IAccountingDocumentExtractor от тук)

Accountant.Processors
  ├── Accountant.Contracts
  └── Accountant.Claude / Codex / Gemini    (per-CLI команда се избира кой extractor да се използва)

Accountant.Tests
  ├── Accountant.Contracts          (тества validators, normalization)
  └── Accountant.Claude / Codex / Gemini    (по преценка — integration тестове)
```

**Скелет (още не са свързани):**

```text
Accountant.Web
  ├── Accountant.Contracts
  ├── Accountant.Identity
  ├── Accountant.DataAccess
  ├── Accountant.MySql              (само за DI: options.UseMySql(...))
  └── Accountant.Claude / Codex / Gemini

Accountant.MySql
  └── Accountant.DataAccess

Accountant.Identity
  └── Accountant.DataAccess
```

**Правила:**
- `Accountant.Contracts` НЕ зависи от vendor-specific SDK-ове. Това е neutral слой.
- Всеки vendor проект зависи САМО от `Accountant.Contracts` + неговия конкретен SDK. Не зависи от другите vendor-и.
- `Accountant.Web` НЕ имплементира extraction логика — само consum-ва `IAccountingDocumentExtractor` през DI (когато стане активен).
- `Accountant.Processors` е CLI sandbox — позволено е "разни задачи свързани с разработката". Не пуска production-grade код.

## 5. Контракт на основния интерфейс

`Accountant.Contracts/Extraction/IAccountingDocumentExtractor.cs`:

```csharp
public interface IAccountingDocumentExtractor
{
    Task<IReadOnlyList<ExtractionResult>> ExtractAsync(
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);
}
```

`ExtractionResult` DTO е C# огледало на v2 schema-та от
[`Unified_Extraction_Contract/accountant.document.v2.schema.json`](../Unified_Extraction_Contract/accountant.document.v2.schema.json) — top-level: `SchemaVersion`, `Source`, `Extraction`, `Validation`, `ModelAssessment`, `Evidence`, `Provider`. Всеки блок е отделен record/class със същите имена, snake_case → PascalCase, със `[JsonPropertyName]` атрибути за wire-формата.

DTO-тата са имутабилни (records с init-only properties).

## 6. Source of truth за extraction формата

C# DTO-тата и валидаторите ОТРАЗЯВАТ, не дефинират контракта. Authoritative source-ът остава:

- **Schema (machine-readable):** [`Unified_Extraction_Contract/accountant.document.v2.schema.json`](../Unified_Extraction_Contract/accountant.document.v2.schema.json)
- **Spec (human-readable):** [`Unified_Extraction_Contract/EXTRACTION_CONTRACT.md`](../Unified_Extraction_Contract/EXTRACTION_CONTRACT.md)
- **Нормализационни правила R1-R13:** [`Unified_Extraction_Contract/NORMALIZATION_RULES.md`](../Unified_Extraction_Contract/NORMALIZATION_RULES.md)
- **Пример:** [`Unified_Extraction_Contract/example.result.json`](../Unified_Extraction_Contract/example.result.json)

При промяна в контракта (нови R-правила, schema bump): първо се обновява `Unified_Extraction_Contract/`, след това се отразява в C# DTO-тата и валидаторите. Никога обратното. `Accountant.Tests` валидира че сериализиран `ExtractionResult` минава JSON Schema-та.

## 7. Конфигурация и среди

Стандартен .NET pattern. Засега в research фазата е достатъчно `appsettings.json` + user-secrets за локална разработка. Multi-environment setup (Development / Test / Production) се добавя когато се излезе от research mode.

Vendor API ключове (Anthropic, OpenAI, Google) — `dotnet user-secrets` локално, environment variables на сървър. Никога не commit-ват в repo-то.

## 8. Логване

Засега console logging е достатъчно за `Accountant.Processors`. Serilog + MySQL sink се добавя когато тръгне Web/persistence work.

Structured logging е препоръчителен pattern от старта — vendor calls, latency, token usage, cost estimates трябва да минават като structured properties, дори когато sink-ът е само console. Това спестява rewrite по-късно.

## 9. Тестване

- **`Accountant.Tests`** (xUnit):
  - Валидация на R1-R13 нормализация helpers
  - Contract conformance: сериализиран `ExtractionResult` минава JSON Schema-та
  - Validators: EIK checksum, IBAN mod-97, BIC regex, totals reconciliation, VAT breakdown
  - Snapshot тестове срещу референтни JSON-и в `docs/facturi/`

- **Integration тестове** (per-vendor): пускат се ръчно или по cron, не на всеки build (костват пари за vendor API). Резултатът се сравнява с историческия benchmark.

- **Web user-facing flows:** няма Playwright e2e foundation засега. Ако се добави Web UI работа — добавя се отделно.

## 10. Background Jobs

Hangfire (както Assistant), MySQL storage. **Засега не задължително** — добавя се когато имаме нужда от scheduled extraction batches или async re-runs. Dashboard в Web admin секция.

## 11. Notifications

[`Braikov.Notifications.*`](../../Assistant/Source/Backend/) пакетите от Assistant repo ще се консумират като NuGet (когато стане ready) или като git submodule/path reference. **Засега не интегрирани.** Когато се добавят:
- `Accountant.Notifications` — host adapter (мап-ва Accountant entities към Braikov.Notifications contracts)
- DI wiring в `Accountant.Web`

Сценарии за нотификации (бъдеще): email при batch extraction готов, in-app notification при failure rate spike, push на mobile (ако се добави) при ръчна review нужда.

## 12. Управление на тайните

- **Local dev:** `dotnet user-secrets` — vendor API keys, MySQL connection strings.
- **CI/CD:** GitHub Secrets (когато се добави CI).
- **Production:** Environment variables на хост сървъра.

## 13. Отворени въпроси (deferred)

Всички въпроси по-долу са **отложени** до излизане от research фазата. Не блокират текущата работа.

- **Persistence модел за extractions:** MySQL entities (`ExtractionRun`, `ExtractionResult`) или само disk JSON-и под `Source/Accountant.Processors/runs/`? — решава се когато тръгне production-feature work.
- **User scope:** Минимум две роли (admin + клиенти) са потвърдени. Multi-tenant boundary, регистрация flow, и authorization detail-и — решават се при първата `Accountant.Web` / `Accountant.Identity` task.
- **Web role:** review/diff на batch резултати, интерактивно качване на снимка → live extraction, или и двете — решава се когато се тръгне Web UI работата.
