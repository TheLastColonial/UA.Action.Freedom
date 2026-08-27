#!/usr/bin/env pwsh
#requires -Version 7
<#
.SYNOPSIS
    Regenerates the UA.Action.Freedom.Hmrc.Gvms client from the committed HMRC OpenAPI spec.

.DESCRIPTION
    Pipeline:
      1. dotnet tool restore                     (NSwag console, pinned in .config/dotnet-tools.json)
      2. PreprocessSpec.cs  raw yaml -> clean $ref-based OpenAPI json   (skipped with -Raw)
      3. nswag run          json -> one monolithic C# file
      4. Split.cs           monolith -> one file per type in src/.../Generated/
      5. dotnet build       smoke-build the SDK project

    See README.md in this folder for what the preprocessing step does and why.

.PARAMETER Raw
    Feed the untouched spec straight to NSwag (no preprocessing). Produces ~170 near-duplicate
    classes; use only to diff against the cleaned output.

.PARAMETER JsonLibrary
    NSwag serializer target: SystemTextJson (default) or NewtonsoftJson.
#>
[CmdletBinding()]
param(
    [switch] $Raw,
    [ValidateSet('SystemTextJson', 'NewtonsoftJson')]
    [string] $JsonLibrary = 'SystemTextJson'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$scratch = Join-Path $repo 'build/nswag/generated'
$rawSpec = Join-Path $repo 'docs/schemas/hmrc/goods-vehicle-movements-1.0.yaml'
$preSpec = Join-Path $scratch 'goods-vehicle-movements.preprocessed.json'
$monolith = Join-Path $scratch 'GvmsClient.cs'
$outDir = Join-Path $repo 'src/UA.Action.Freedom.Hmrc.Gvms/Generated'
$csproj = Join-Path $repo 'src/UA.Action.Freedom.Hmrc.Gvms/UA.Action.Freedom.Hmrc.Gvms.csproj'

New-Item -ItemType Directory -Force -Path $scratch | Out-Null

Push-Location $repo
try {
    Write-Host '==> dotnet tool restore' -ForegroundColor Cyan
    dotnet tool restore

    if ($Raw) {
        $spec = $rawSpec
        Write-Host '==> preprocessing SKIPPED (-Raw)' -ForegroundColor Yellow
    }
    else {
        Write-Host '==> preprocessing spec' -ForegroundColor Cyan
        dotnet run "$PSScriptRoot/PreprocessSpec.cs" -- $rawSpec $preSpec
        $spec = $preSpec
    }

    Write-Host '==> nswag run' -ForegroundColor Cyan
    dotnet nswag run "$PSScriptRoot/goods-vehicle-movements.nswag" `
        "/variables:SpecPath=$spec,JsonLibrary=$JsonLibrary,OutFile=$monolith"

    Write-Host '==> splitting into one file per type' -ForegroundColor Cyan
    dotnet run "$PSScriptRoot/Split.cs" -- $monolith $outDir

    Write-Host '==> smoke build' -ForegroundColor Cyan
    dotnet build $csproj -c Release
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host "Done. Review the diff under $outDir and commit it with any codegen-config changes." -ForegroundColor Green
