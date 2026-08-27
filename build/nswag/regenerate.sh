#!/usr/bin/env bash
#
# Regenerates the UA.Action.Freedom.Hmrc.Gvms client from the committed HMRC OpenAPI spec.
# Mirror of regenerate.ps1 for Linux/macOS. See README.md for what each step does.
#
#   ./regenerate.sh [--raw] [--json-library SystemTextJson|NewtonsoftJson]
#
set -euo pipefail

raw=0
json_library="SystemTextJson"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --raw) raw=1; shift ;;
        --json-library) json_library="$2"; shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$script_dir/../.." && pwd)"
scratch="$repo/build/nswag/generated"
raw_spec="$repo/docs/schemas/hmrc/goods-vehicle-movements-1.0.yaml"
pre_spec="$scratch/goods-vehicle-movements.preprocessed.json"
monolith="$scratch/GvmsClient.cs"
out_dir="$repo/src/UA.Action.Freedom.Hmrc.Gvms/Generated"
csproj="$repo/src/UA.Action.Freedom.Hmrc.Gvms/UA.Action.Freedom.Hmrc.Gvms.csproj"

mkdir -p "$scratch"
cd "$repo"

echo "==> dotnet tool restore"
dotnet tool restore

if [[ $raw -eq 1 ]]; then
    spec="$raw_spec"
    echo "==> preprocessing SKIPPED (--raw)"
else
    echo "==> preprocessing spec"
    dotnet run "$script_dir/PreprocessSpec.cs" -- "$raw_spec" "$pre_spec"
    spec="$pre_spec"
fi

echo "==> nswag run"
dotnet nswag run "$script_dir/goods-vehicle-movements.nswag" \
    "/variables:SpecPath=$spec,JsonLibrary=$json_library,OutFile=$monolith"

echo "==> splitting into one file per type"
dotnet run "$script_dir/Split.cs" -- "$monolith" "$out_dir"

echo "==> smoke build"
dotnet build "$csproj" -c Release

echo
echo "Done. Review the diff under $out_dir and commit it with any codegen-config changes."
