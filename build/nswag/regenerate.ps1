#!/usr/bin/env pwsh
#requires -Version 7
<#
.SYNOPSIS
    Regenerates one of the HMRC typed API clients from its committed OpenAPI spec.

.DESCRIPTION
    Pipeline:
      1. dotnet tool restore                     (NSwag console, pinned in .config/dotnet-tools.json)
      2. PreprocessSpec.cs  raw yaml -> clean $ref-based OpenAPI json   (skipped with -Raw)
      3. nswag run          json -> one monolithic C# file
      4. Split.cs           monolith -> one file per type in src/<project>/Generated/
      5. dotnet build       smoke-build the SDK project

    See README.md in this folder for what the preprocessing step does and why.

.PARAMETER Api
    Which client to regenerate. Determines the spec, preprocess config, nswag config and
    target project by convention:
      docs/schemas/hmrc/<Api>-1.0.yaml
      build/nswag/<Api>.preprocess.json
      build/nswag/<Api>.nswag
      src/<project>/Generated/

.PARAMETER Raw
    Feed the untouched spec straight to NSwag (no preprocessing). Produces many near-duplicate
    classes; use only to diff against the cleaned output.

.PARAMETER JsonLibrary
    NSwag serializer target: SystemTextJson (default) or NewtonsoftJson.
#>
[CmdletBinding()]
param(
    [ValidateSet('goods-vehicle-movements', 'push-pull-notifications', 'elo')]
    [string] $Api = 'goods-vehicle-movements',
    [switch] $Raw,
    [ValidateSet('SystemTextJson', 'NewtonsoftJson')]
    [string] $JsonLibrary = 'SystemTextJson'
)

$ErrorActionPreference = 'Stop'

$projectByApi = @{
    'goods-vehicle-movements' = 'HMRC.GVMS'
    'push-pull-notifications' = 'HMRC.PushPullNotifications'
    'elo'                     = 'EDI.ELO'
}
$project = $projectByApi[$Api]

# ELO's spec lives outside docs/schemas/hmrc/ and doesn't follow the "<api>-1.0.yaml"
# naming convention (it's a third-party filename: API_BREXIT_ELO-1.2.0.yaml). Override per
# API where the convention doesn't fit; everything not listed here still resolves below.
$specPathByApi = @{
    'elo' = 'docs/schemas/edi/API_BREXIT_ELO-1.2.0.yaml'
}

# ELO used to require -Raw, because PreprocessSpec.cs pass 2 dropped the 'Authorization' header
# parameter that for this API is a real caller-supplied credential. That pass now takes an
# exemption list (elo.preprocess.json -> preserveHeaderParameters), so ELO runs the normal
# pipeline — and must, because the same sidecar is what corrects the unusable 'pdf' schema.
# -Raw remains available for diffing, but produces a client whose barcode field cannot be read.
if ($Api -eq 'elo' -and $Raw) {
    Write-Warning ("Regenerating ELO with -Raw skips elo.preprocess.json, which is what makes " +
                   "the barcode 'pdf' field readable and keeps the ENV_NOT01 DTO. Use this only " +
                   "to diff against the preprocessed output; do not commit the result.")
}

$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$scratch = Join-Path $repo 'build/nswag/generated'
$rawSpec = if ($specPathByApi.ContainsKey($Api)) {
    Join-Path $repo $specPathByApi[$Api]
} else {
    Join-Path $repo "docs/schemas/hmrc/$Api-1.0.yaml"
}
$config = Join-Path $repo "build/nswag/$Api.preprocess.json"
$preSpec = Join-Path $scratch "$Api.preprocessed.json"
$monolith = Join-Path $scratch "$Api.monolith.cs"
$outDir = Join-Path $repo "src/$project/Generated"
$csproj = Join-Path $repo "src/$project/$project.csproj"

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
        dotnet run "$PSScriptRoot/PreprocessSpec.cs" -- $rawSpec $preSpec $config
        $spec = $preSpec
    }

    Write-Host '==> nswag run' -ForegroundColor Cyan
    dotnet nswag run "$PSScriptRoot/$Api.nswag" `
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
