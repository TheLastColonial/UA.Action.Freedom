#!/usr/bin/env bash
#
# Regenerates one of the HMRC typed API clients from its committed OpenAPI spec.
# Mirror of regenerate.ps1 for Linux/macOS. See README.md for what each step does.
#
#   ./regenerate.sh [--api goods-vehicle-movements|push-pull-notifications] \
#                   [--raw] [--json-library SystemTextJson|NewtonsoftJson]
#
set -euo pipefail

api="goods-vehicle-movements"
raw=0
json_library="SystemTextJson"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --api) api="$2"; shift 2 ;;
        --raw) raw=1; shift ;;
        --json-library) json_library="$2"; shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

case "$api" in
    goods-vehicle-movements) project="HMRC.GVMS" ;;
    push-pull-notifications) project="HMRC.PushPullNotifications" ;;
    *) echo "unknown --api: $api" >&2; exit 2 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$script_dir/../.." && pwd)"
scratch="$repo/build/nswag/generated"
raw_spec="$repo/docs/schemas/hmrc/$api-1.0.yaml"
config="$repo/build/nswag/$api.preprocess.json"
pre_spec="$scratch/$api.preprocessed.json"
monolith="$scratch/$api.monolith.cs"
out_dir="$repo/src/$project/Generated"
csproj="$repo/src/$project/$project.csproj"

mkdir -p "$scratch"
cd "$repo"

echo "==> dotnet tool restore"
dotnet tool restore

if [[ $raw -eq 1 ]]; then
    spec="$raw_spec"
    echo "==> preprocessing SKIPPED (--raw)"
else
    echo "==> preprocessing spec"
    dotnet run "$script_dir/PreprocessSpec.cs" -- "$raw_spec" "$pre_spec" "$config"
    spec="$pre_spec"
fi

echo "==> nswag run"
dotnet nswag run "$script_dir/$api.nswag" \
    "/variables:SpecPath=$spec,JsonLibrary=$json_library,OutFile=$monolith"

echo "==> splitting into one file per type"
dotnet run "$script_dir/Split.cs" -- "$monolith" "$out_dir"

echo "==> smoke build"
dotnet build "$csproj" -c Release

echo
echo "Done. Review the diff under $out_dir and commit it with any codegen-config changes."
