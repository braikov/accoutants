param(
    [string]$Password,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptRoot "..")
$projectPath = Join-Path $projectRoot "ReviewSite.csproj"

$arguments = @(
    "publish",
    $projectPath,
    "--configuration", "Release",
    "/p:PublishProfile=Test"
)

if ($NoBuild) {
    $arguments += "--no-build"
}

if ($Password) {
    $arguments += "/p:Password=$Password"
}

Write-Host "Publishing ReviewSite to https://accountant.ima.bg using Test.pubxml..." -ForegroundColor Cyan
dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

Write-Host "Publish command finished." -ForegroundColor Green
