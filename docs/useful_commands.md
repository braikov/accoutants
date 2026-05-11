# Useful commands

Жив cheatsheet с командите, които често се ползват по проекта. Всички команди се изпълняват от root-а на repo-то (`c:\Projects\Miro\Accountant\`) освен ако е изрично указано друго.

## Build / run

```powershell
# Цял solution.
dotnet build Source/Accountant.slnx

# Само Web (бързо, когато си в подмодул).
dotnet build Source/Accountant.Web/Accountant.Web.csproj

# Run Web в Development mode (URL-и от Properties/launchSettings.json).
dotnet run --project Source/Accountant.Web --launch-profile https
# По подразбиране на този проект → https://localhost:7162

# CLI sandbox.
dotnet run --project Source/Accountant.Processors -- --help
```

При работещ Web instance DLL-ите са locked → нов `dotnet build` гърми с `MSB3021` / `MSB3027`. Спри Web преди build за DataAccess / Jobs / Storage / Identity / Email / Notifications.

## Database

Connection string-ът за dev е в `Source/Accountant.Web/appsettings.Development.json` под `ConnectionStrings:Accountant`. Production се чете от `dotnet user-secrets` или env vars.

### Read-only inspection

```powershell
# Достъп до MariaDB shell (read-only действия — schema промени НЕ).
mariadb -u accountant_dev -p dev_accountant

# Quick row count.
mariadb -u accountant_dev -p dev_accountant -e "SELECT COUNT(*) FROM documents;"
```

### Migrations (schema changes)

**Никога** не променяй схемата с raw SQL. Винаги през EF Core migration (виж глобалните инструкции в `CLAUDE.md`).

```powershell
# Генериране на нова migration. Web инстанцията трябва да е спряна.
$env:ACCOUNTANT_DB_CONNECTION = "Server=localhost;Port=3306;Database=dev_accountant;Uid=accountant_dev;Pwd=<dev-password>;"
dotnet ef migrations add <Name> `
  --project Source/Accountant.MySql `
  --startup-project Source/Accountant.MySql `
  --context AccountantDbContext

# Прилагане в dev.
dotnet ef database update `
  --project Source/Accountant.MySql `
  --startup-project Source/Accountant.MySql `
  --context AccountantDbContext

# Production: генерирай idempotent script + ревюирай → applyaй ръчно.
dotnet ef migrations script --idempotent `
  --project Source/Accountant.MySql `
  --startup-project Source/Accountant.MySql `
  --context AccountantDbContext `
  --output deploy/migrations.sql
```

`__AccountantMigrationsHistory` пази историята на главния DbContext. Braikov.* пакетите имат собствени history таблици — `__BraikovIdentityEventsMigrationsHistory`, `__BraikovIdentityShortCodesMigrationsHistory`, `__BraikovNotificationsMigrationsHistory` — и собствени context-и; миграциите им се прилагат автоматично при стартиране (виж `AddBraikov*`).

Hangfire schema (`Hangfire_*` таблици) се създава автоматично при първи старт на Web (`PrepareSchemaIfNecessary = true`).

## Background jobs

```text
Dashboard: https://localhost:7162/Administration/Hangfire
```

Достъпен само за users в Admin role (виж `AdminDashboardAuthorizationFilter`). Anonymous + non-Admin → 401.

При рестарт на Web — `StuckJobRecoveryService` автоматично re-enqueue-ва документи с `Status=Processing` (орфани от предишния процес).

## Roles + admin bootstrap

Първият регистриран user (lowest `UserId`) автоматично получава `Admin` role при следващия Web start (`AdminRoleBootstrapService`). Idempotent — последващи стартове не правят нищо ако вече има admin.

Ръчно promote/demote през SQL (read-write — само в dev):

```sql
-- Промоция към Admin (предполага role-а вече съществува).
INSERT INTO AspNetUserRoles (UserId, RoleId)
SELECT u.Id, r.Id FROM AspNetUsers u, AspNetRoles r
WHERE u.Email = 'someone@example.com' AND r.Name = 'Admin';

-- Demote.
DELETE ur FROM AspNetUserRoles ur
JOIN AspNetUsers u ON u.Id = ur.UserId
JOIN AspNetRoles r ON r.Id = ur.RoleId
WHERE u.Email = 'someone@example.com' AND r.Name = 'Admin';
```

Cookie auth кешира role claims — потребителят трябва да logout/login или да изчака `SecurityStampValidator` re-validation (5 мин, виж Program.cs).

## Vendor API keys

Хост-локални в user-secrets на `Accountant.Web` (UserSecretsId `e03343bc-a839-4036-ac9c-8d20f6707f6b`):

```powershell
dotnet user-secrets --project Source/Accountant.Web set "Codex:ApiKey" "sk-proj-..."
dotnet user-secrets --project Source/Accountant.Web set "Claude:ApiKey" "sk-ant-..."
dotnet user-secrets --project Source/Accountant.Web set "Gemini:ApiKey" "AIza..."

# Виж текущите.
dotnet user-secrets --project Source/Accountant.Web list
```

Default vendor се избира от `/Administration/Settings` (DB-backed `Extraction.DefaultVendor`). При липса fallback е `appsettings.json:Extraction:DefaultVendor` (`codex` сега за cost-controlled тестване).

## Quick links (development)

- Workspace: `https://localhost:7162/App`
- Admin dashboard: `https://localhost:7162/Administration`
- Hangfire: `https://localhost:7162/Administration/Hangfire`
- Settings: `https://localhost:7162/Administration/Settings`

## Tests

```powershell
dotnet test Source/Accountant.Tests/Accountant.Tests.csproj
```

Vendor extractor integration tests са изключени от default build (струват пари). Виж `Accountant.Tests` README за манyално пускане.
