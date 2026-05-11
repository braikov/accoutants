# Полезни команди (Useful Commands)

Често използвани команди по време на разработката на Accountant проекта.

## Build

### 1. Build на цялото solution

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet build Accountant.slnx
```

### 2. Build само на един проект

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet build Accountant.Claude\Accountant.Claude.csproj
dotnet build Accountant.Processors\Accountant.Processors.csproj
dotnet build Accountant.ReviewSite\Accountant.ReviewSite.csproj
```

> Важно: ако `Accountant.ReviewSite` върви (`dotnet run`), build-ът на solution-а ще падне със заключен `.exe`. Спри го преди rebuild.

## Extraction (Accountant.Processors)

### 1. Един vendor върху един image

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet run --project Accountant.Processors -- extract --vendor claude ../docs/facturi/20240213_190514.jpg
```

### 2. Един vendor върху цял folder

```powershell
dotnet run --project Accountant.Processors -- extract --vendor claude --dir ../docs/facturi
dotnet run --project Accountant.Processors -- extract --vendor claude --dir ../docs/facturi --limit 3
```

### 3. Всичките три vendor-а върху един image (за сравнение)

```powershell
dotnet run --project Accountant.Processors -- extract --vendor all ../docs/facturi/20240213_190514.jpg
```

### 4. Подмножество vendor-и

```powershell
dotnet run --project Accountant.Processors -- extract --vendor claude,gemini --dir ../docs/facturi --limit 5
```

### 5. Help

```powershell
dotnet run --project Accountant.Processors -- extract --help
```

## Normalize (post-process на готови JSON-и без API call)

Прилага `ResultSanitizer` върху съществуващи `<Vendor>_<stem>.json` файлове — coerce-ва null nested obj-и към schema default shape, без да извиква vendor.

### 1. Dry-run (само показва какво БИ се променило)

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet run --project Accountant.Processors -- normalize --dir ../docs/facturi --dry-run
```

### 2. Реално нормализиране (с .bak)

```powershell
dotnet run --project Accountant.Processors -- normalize --dir ../docs/facturi
```

### 3. Само един vendor

```powershell
dotnet run --project Accountant.Processors -- normalize --dir ../docs/facturi --vendor claude
dotnet run --project Accountant.Processors -- normalize --dir ../docs/facturi --vendor claude,gemini
```

### 4. Без backup (ако вече си правил)

```powershell
dotnet run --project Accountant.Processors -- normalize --dir ../docs/facturi --no-backup
```

## ReviewSite (browser UI за сравнение)

```powershell
cd C:\Projects\Miro\Accountant\Source\Accountant.ReviewSite
dotnet run
```

Default ports: `https://localhost:7094`, `http://localhost:5141`. Достъп с BasicAuth по credentials в `appsettings.*.json`.

`Review:ImageFolder` се определя от `appsettings.json` (default `..\..\docs\facturi`). Override в `appsettings.Development.json` или env var.

## User-secrets (vendor API keys)

Локални тайни живеят в `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`. UserSecretsId на `Accountant.Processors` е в `<UserSecretsId>` тага на `.csproj`.

### 1. Добавяне / обновяване на API key

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet user-secrets --project Accountant.Processors set "Claude:ApiKey" "sk-ant-..."
dotnet user-secrets --project Accountant.Processors set "Codex:ApiKey" "sk-..."
dotnet user-secrets --project Accountant.Processors set "Gemini:ApiKey" "AIza..."
```

### 2. Списък на всички записани keys

```powershell
dotnet user-secrets --project Accountant.Processors list
```

### 3. Изтриване на един key

```powershell
dotnet user-secrets --project Accountant.Processors remove "Claude:ApiKey"
```

### 4. Локация на файла на диска

```powershell
$id = ([xml](Get-Content Source/Accountant.Processors/Accountant.Processors.csproj)).Project.PropertyGroup.UserSecretsId
"$env:APPDATA\Microsoft\UserSecrets\$id\secrets.json"
```

## Deployment (Accountant.Web + ReviewSite на vic.bg)

Двата сайта се хостват на същия IIS сървър:

| App | Domain | App Pool |
|---|---|---|
| `Accountant.Web` | `accountant.ima.bg` | `accountant.ima.bg` |
| `Accountant.ReviewSite` | `accountant-tune.ima.bg` | `accountant-tune.ima.bg` |

### Изпращане към prod (recommended — script-based)

`scripts/publish.ps1` обхожда password проблема (DPAPI / Test.pubxml.user) като чете deploy паролата от env var:

```powershell
cd C:\Projects\Miro\Accountant
$env:ACCOUNTANT_DEPLOY_PASSWORD = '<deploy_password>'

pwsh scripts\publish.ps1                        # двата сайта
pwsh scripts\publish.ps1 -Project Web           # само Accountant.Web
pwsh scripts\publish.ps1 -Project ReviewSite    # само ReviewSite
pwsh scripts\publish.ps1 -SkipSmoke             # без HTTP smoke check
```

Deploy account: `WIN-5DK51C7DFUB\deploy`.

### Алтернатива — directly от Visual Studio

`Right-click project → Publish → Test profile → Save password (еднократно)` и след това VS DPAPI-encrypt-ва паролата в `Test.pubxml.user`. След това `Publish` бутонът работи без env var. `.user` файловете са gitignored.

### Production connection string (НЕ е в git)

Конфигурирана е като IIS App Pool environment variable на `accountant.ima.bg`:
```
ConnectionStrings__Accountant=Server=localhost;Port=3306;Database=accountant;Uid=accountant;Pwd=<...>;
```

ASP.NET Core конфигурация автоматично взима env vars с `__` като section separator. За промяна виж `scripts/set_web_prod_env.ps1` (в `C:\temp` локално като пример).

## Database (MySQL/MariaDB)

Persistence layer: `Accountant.DataAccess` + `Accountant.MySql`. Connection string lives in `appsettings.json` under `ConnectionStrings:Accountant` or via user-secrets.

### 1. Set local connection string (user-secrets — preferred for password)

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet user-secrets --project Accountant.Processors set "ConnectionStrings:Accountant" "Server=localhost;Port=3306;Database=accountant;Uid=root;Pwd=YOURPASS;"
```

(`appsettings.json` has the default placeholder; user-secrets override it.)

### 2. Create the database (one-time, manual)

```powershell
& 'C:\Program Files\MariaDB 12.2\bin\mariadb.exe' -u root -e "CREATE DATABASE IF NOT EXISTS accountant DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;"
```

### 3. Apply migrations to the dev DB

```powershell
cd C:\Projects\Miro\Accountant\Source
$env:ACCOUNTANT_DB_CONNECTION = "Server=localhost;Port=3306;Database=accountant;Uid=root;Pwd=YOURPASS;"
dotnet ef database update --project Accountant.MySql\Accountant.MySql.csproj --context AccountantDbContext
```

### 4. Add a new migration after entity changes

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet ef migrations add <DescriptiveName> --project Accountant.MySql\Accountant.MySql.csproj --context AccountantDbContext
```

> Important: stop any running `Accountant.Web` / `Accountant.Processors` instance before adding/applying migrations — the DLLs are locked and the build will fail.

### 5. Roll back the last unapplied migration

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet ef migrations remove --project Accountant.MySql\Accountant.MySql.csproj --context AccountantDbContext
```

### 6. Roll back the database to a previous migration

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet ef database update <PreviousMigrationName> --project Accountant.MySql\Accountant.MySql.csproj --context AccountantDbContext
```

### 7. Generate a prod-safe idempotent migration script

For applying to the production DB without `dotnet ef database update` against prod:

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet ef migrations script 0 --idempotent `
  --project Accountant.MySql\Accountant.MySql.csproj `
  --startup-project Accountant.Web\Accountant.Web.csproj `
  --context AccountantDbContext `
  --output C:\temp\schema.sql
```

Copy via `scp` to the prod host; apply through the IIS App Pool env var connection string (see [scripts/apply_notifications.ps1](scripts/apply_notifications.ps1) for the pattern). **Reminder:** schema changes go through migrations only — never hand-edit SQL against the DB. See `~/.claude/CLAUDE.md`.

## Notifications schema (`NotificationDbContext`)

Notifications use a separate EF context shipped inside `Braikov.Notifications.MySql`. Same DB, separate `__BraikovNotificationsMigrationsHistory` table.

### 1. List migrations

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet ef migrations list `
  --project Accountant.Web\Accountant.Web.csproj `
  --startup-project Accountant.Web\Accountant.Web.csproj `
  --context NotificationDbContext
```

### 2. Apply to dev DB

```powershell
dotnet ef database update `
  --project Accountant.Web\Accountant.Web.csproj `
  --startup-project Accountant.Web\Accountant.Web.csproj `
  --context NotificationDbContext
```

The Braikov package owns the migrations — we don't author or modify them in this repo. Bump the package version to pull in new schema (see "Braikov.Notifications.* packages" below).

## Identity audit schema (`AccountEventDbContext` — from `Braikov.Identity.Events.MySql`)

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet ef database update `
  --project Accountant.Web\Accountant.Web.csproj `
  --startup-project Accountant.Web\Accountant.Web.csproj `
  --context AccountEventDbContext
```

Creates `AccountEvents` table + `__BraikovIdentityEventsMigrationsHistory` in the same `accountant` DB. Package owns the migration.

## Identity short-code mapping schema (`ShortCodeTokenDbContext` — from `Braikov.Identity.ShortCodes.MySql`)

```powershell
dotnet ef database update `
  --project Accountant.Web\Accountant.Web.csproj `
  --startup-project Accountant.Web\Accountant.Web.csproj `
  --context ShortCodeTokenDbContext
```

Creates `ShortCodeTokens` table + `__BraikovIdentityShortCodesMigrationsHistory`. Package owns the migration.

## `dotnet new braikov-identity` template — bootstrap a new project

One-time install from local folder:

```powershell
dotnet new install C:\Projects\Miro\Braikov\templates\braikov-identity
```

Use in a fresh project:

```powershell
mkdir MyApp.Web && cd MyApp.Web
dotnet new mvc          # standard MVC scaffold
dotnet new braikov-identity
# Follow BRAIKOV_IDENTITY_SETUP.md (printed at the project root)
```

The placeholder `BraikovHost` namespace is replaced with the project folder name automatically.

## Identity / dev-only helpers

### Manually confirm a user (skip email-confirmation step in dev)

When `Email:Enabled=false` the registration email is logged but not sent, so the confirmation link never reaches a real inbox. Quick way to unblock the dev account:

```powershell
$env:MYSQL_PWD = '<root-password>'
& 'C:\Program Files\MariaDB 12.2\bin\mariadb.exe' -u root -e `
  "USE dev_accountant; UPDATE aspnetusers SET EmailConfirmed=1 WHERE Email='you@example.com';"
```

For the real flow, grab the callback URL from the `[NullEmailSender] EmailConfirmation -> ...` warn line in stdout and paste it into the browser.

### Wipe all dev users

```powershell
$env:MYSQL_PWD = '<root-password>'
& 'C:\Program Files\MariaDB 12.2\bin\mariadb.exe' -u root -e `
  "USE dev_accountant; DELETE FROM aspnetuserclaims; DELETE FROM aspnetuserlogins; DELETE FROM aspnetuserroles; DELETE FROM aspnetusertokens; DELETE FROM aspnetusers;"
```

### Local SMTP testing with smtp4dev

To exercise the real SMTP path without using a live mailbox:

```powershell
dotnet tool install -g Rnwood.Smtp4dev
smtp4dev --smtpport 25 --imapport 143 --urls http://localhost:5050
```

Then flip `appsettings.Development.json` → `Email.Enabled = true` (host already points at `localhost:25`). The web UI on `http://localhost:5050` shows every sent message; tear down with `Ctrl+C`.

## Braikov.Notifications.* packages (local NuGet feed)

The notification packages live in their own repo: `C:\Projects\Miro\Braikov\`. They're packed locally to `C:\Projects\Miro\NuGet\` and consumed by Accountant via `NuGet.Config` (key `braikov-local`).

### 1. Rebuild the packages after a Braikov change

```powershell
cd C:\Projects\Miro\Braikov
dotnet pack Braikov.Notifications.slnx -c Release
```

`PackageOutputPath` from `Directory.Build.props` drops the .nupkg files into `C:\Projects\Miro\NuGet\`.

### 2. Add a Braikov package to an Accountant project

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet add Accountant.Web/Accountant.Web.csproj package Braikov.Notifications.Core
dotnet add Accountant.Web/Accountant.Web.csproj package Braikov.Notifications.MySql
dotnet add Accountant.Web/Accountant.Web.csproj package Braikov.Notifications.Email
```

### 3. Pull a newer version after a Braikov bump

Bump `<Version>` in `Braikov\Directory.Build.props`, repack (#1), then in Accountant:

```powershell
cd C:\Projects\Miro\Accountant\Source
dotnet restore
```

If the new version isn't picked up automatically, clear the NuGet cache for that package and restore again:

```powershell
dotnet nuget locals http-cache --clear
dotnet restore --force-evaluate
```

## Schema / contract validation

Проверка че example.result.json минава JSON Schema-та:

```powershell
cd C:\Projects\Miro\Accountant
python -c "import json, jsonschema; jsonschema.Draft202012Validator(json.load(open('Unified_Extraction_Contract/accountant.document.v2.schema.json', encoding='utf-8'))).validate(json.load(open('Unified_Extraction_Contract/example.result.json', encoding='utf-8'))); print('OK')"
```

(Изисква `pip install jsonschema`.)

## Cleanup

### 1. Изчистване на runs/ (timestamped extraction outputs)

```powershell
Remove-Item -Recurse -Force C:\Projects\Miro\Accountant\Source\Accountant.Processors\runs\*
```

### 2. Изчистване на .bak файловете в docs/facturi

```powershell
Get-ChildItem C:\Projects\Miro\Accountant\docs\facturi -Filter '*.bak*' | Remove-Item
```

### 3. Изчистване на bin/obj за всички проекти

```powershell
cd C:\Projects\Miro\Accountant\Source
Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force
```
