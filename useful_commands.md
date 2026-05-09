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

## ReviewSite deployment

Публикуване към `accountant.ima.bg` на `vic.bg` IIS server чрез Web Deploy:

```powershell
cd C:\Projects\Miro\Accountant\Source\Accountant.ReviewSite
dotnet publish -c Release -p:PublishProfile=Properties/PublishProfiles/Test.pubxml
```

Парола за deploy account-а живее в `Properties/PublishProfiles/Test.pubxml.user` (DPAPI-encrypted от Visual Studio, current user, current machine — не commit-ва се в git).

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
