# Publish Accountant.Web and/or Accountant.ReviewSite to the vic.bg IIS server
# via Web Deploy. Avoids relying on the DPAPI-encrypted Test.pubxml.user blob —
# instead reads the deploy account password from the ACCOUNTANT_DEPLOY_PASSWORD
# environment variable and passes it to MSDeploy via /p:Password=...
#
# Usage:
#   $env:ACCOUNTANT_DEPLOY_PASSWORD = '<deploy_password>'
#   pwsh scripts/publish.ps1                     # publishes both
#   pwsh scripts/publish.ps1 -Project Web        # publishes only Accountant.Web
#   pwsh scripts/publish.ps1 -Project ReviewSite # publishes only ReviewSite
#   pwsh scripts/publish.ps1 -SkipSmoke          # skip post-publish HTTP smoke check

param(
    [ValidateSet('Web', 'ReviewSite', 'Both')]
    [string]$Project = 'Both',
    [switch]$SkipSmoke
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$password = $env:ACCOUNTANT_DEPLOY_PASSWORD
if (-not $password) {
    throw "ACCOUNTANT_DEPLOY_PASSWORD env var is not set. " +
          "Set it before running: `$env:ACCOUNTANT_DEPLOY_PASSWORD = '<deploy_password>'"
}

$targets = @()
if ($Project -in @('Web', 'Both'))        { $targets += @{ Name = 'Web';        Csproj = "$repoRoot/Source/Accountant.Web/Accountant.Web.csproj";               Url = 'https://accountant.ima.bg/' } }
if ($Project -in @('ReviewSite', 'Both')) { $targets += @{ Name = 'ReviewSite'; Csproj = "$repoRoot/Source/Accountant.ReviewSite/Accountant.ReviewSite.csproj"; Url = 'https://accountant-tune.ima.bg/' } }

foreach ($t in $targets) {
    Write-Host "==== Publishing $($t.Name) -> $($t.Url) ====" -ForegroundColor Cyan
    & dotnet publish $t.Csproj `
        -c Release `
        -p:PublishProfile=Test `
        -p:Password=$password `
        -p:AllowUntrustedCertificate=true
    if ($LASTEXITCODE -ne 0) {
        throw "Publish of $($t.Name) failed."
    }
}

if (-not $SkipSmoke) {
    Write-Host ""
    Write-Host "==== Smoke check ====" -ForegroundColor Cyan
    foreach ($t in $targets) {
        try {
            $r = Invoke-WebRequest -Uri $t.Url -Method Head -TimeoutSec 15 -SkipCertificateCheck
            Write-Host "$($t.Name)`t$($t.Url)`t$($r.StatusCode)" -ForegroundColor Green
        } catch {
            Write-Host "$($t.Name)`t$($t.Url)`tFAILED: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
