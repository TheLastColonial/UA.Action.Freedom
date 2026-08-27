# HMRC GVMS client code generation

Generates `src/UA.Action.Freedom.Hmrc.Gvms/Generated/` — a typed HTTP client for the HMRC
**Goods Vehicle Movements** API — from the committed OpenAPI spec at
`docs/schemas/hmrc/goods-vehicle-movements-1.0.yaml`, using [NSwag](https://github.com/RicoSuter/NSwag).

The generated code **is committed**. Regeneration is a manual step run by a developer when the
spec or the generator config changes; it is deliberately not wired into `dotnet build` or CI
(the CI `test`/`publish` jobs run `--no-build`).

## Regenerate

```pwsh
pwsh build/nswag/regenerate.ps1          # Windows / cross-platform
```

```bash
./build/nswag/regenerate.sh              # Linux / macOS
```

Then review the diff under `src/UA.Action.Freedom.Hmrc.Gvms/Generated/` and commit it together
with any change to the files in this folder. Re-running with no spec/config change produces no
diff (the pipeline is deterministic: LF endings, ordinal-sorted output, stable file names).

Options:

| Flag | Effect |
| --- | --- |
| `-Raw` / `--raw` | Skip preprocessing; feed the untouched spec to NSwag. Produces ~170 near-duplicate classes — for comparison only. |
| `-JsonLibrary` / `--json-library` | `SystemTextJson` (default) or `NewtonsoftJson`. |

## Pipeline

1. **`dotnet tool restore`** — restores NSwag, pinned in `.config/dotnet-tools.json`.
2. **`PreprocessSpec.cs`** (`dotnet run`, file-based app) — rewrites the raw spec into a
   conventional `$ref`-based OpenAPI **JSON** document at
   `build/nswag/generated/goods-vehicle-movements.preprocessed.json`. See below.
3. **`nswag run goods-vehicle-movements.nswag`** — generates one monolithic C# file
   (`build/nswag/generated/GvmsClient.cs`).
4. **`Split.cs`** (`dotnet run`, file-based app, Roslyn) — splits the monolith into one file
   per top-level type under `Generated/`, wiping stale `*.cs` first. A type and its generic
   sibling (`GvmsApiException` / `GvmsApiException<T>`) share a file.
5. **`dotnet build`** — smoke-builds the SDK project.

`build/nswag/generated/` is git-ignored scratch.

## Why preprocessing is needed

The published HMRC spec is a RAML → OpenAPI conversion with **zero `$ref`**: every schema is
inlined and duplicated 3–5×, and enums are modelled as `oneOf` of single-value `enum`
subschemas. Fed to NSwag as-is that yields ~170 classes — `Direction`, `Direction2`,
`Direction3`, `PlannedCrossing` … `PlannedCrossing5`, dozens of `Anonymous*` / `Response*`.

`PreprocessSpec.cs` applies, in order:

1. **Pin operationIds** — the 6 operations get explicit PascalCase ids so the generated method
   names are `GetGoodsMovementRecords`, `CreateGoodsMovementRecord`, `GetGoodsMovementRecord`,
   `UpdateGoodsMovementRecord`, `DeleteGoodsMovementRecord`, `GetReferenceData`
   (NSwag appends `Async`).
2. **Drop transport header params** — the explicit `Accept` / `Authorization` / `Content-Type`
   header parameters are removed; they are `HttpClient` concerns.
3. **Strip `not` / `not.anyOf`** — the mutual-exclusion blocks on the GMR body. NSwag cannot
   express `not` and drops it silently; removing it keeps the spec honest.
4. **Collapse `oneOf` enums** — `oneOf` of single-value `enum` subschemas becomes a single
   `type: string` + `enum: [...]`.
5. **De-duplicate into `components/schemas`** — seeded from the spec's own 55 named component
   schemas, every distinct object/enum shape is hoisted into `components/schemas` (keyed by a
   structural hash that ignores `description` / `example` / `title` / `default`) and each
   occurrence is replaced with a `$ref`. New shapes with no named twin are hoisted under a
   name derived from their `title` or the property/context they appear in.
6. **Rename conversion artifacts** — `post-gmr-schema_definitions` → `goodsMovementRecordRequest`,
   `get-gmr-schema_definitions` → `goodsMovementRecord`, `error-response_definitions` →
   `errorResponse`; the array-typed `definitions` is unwrapped to its element and renamed
   `goodsMovementRecordSummary`.
7. **Prune orphans** — component schemas nothing references (to a fixed point) are removed so
   NSwag does not emit classes for them.

Result: ~40 model types with meaningful names.

### Known residual gaps

- The GMR body's "exactly one of `emptyVehicle` / `dbcDeclaration` / …" rule is **not**
  enforced by the client (it was a `not` block). Callers must respect it.
- Structurally identical shapes are merged: `actualCrossing` properties are typed the same as
  `checkedInCrossing` because the shapes are identical in the spec.
- A few sub-shapes that genuinely differ between the summary and full GMR keep numeric
  suffixes (`Link` / `Link2`, `RuleFailure` / `RuleFailure2`, and the `Method` / `Rel` link
  enums).

## Pinned versions

| Tool | Version | Where |
| --- | --- | --- |
| `NSwag.ConsoleCore` | `14.7.1` | `.config/dotnet-tools.json` |
| `Microsoft.CodeAnalysis.CSharp` (splitter) | `4.14.0` | `#:package` in `Split.cs` |
| `YamlDotNet` (preprocessor) | `16.2.1` | `#:package` in `PreprocessSpec.cs` |

NSwag 14.7.1 ships a native `net10.0` build, so it runs directly on the .NET 10 SDK. The
manifest also sets `rollForward: true` as a safety net. If a future NSwag rejects
`"runtime": "Net100"` in `goods-vehicle-movements.nswag`, fall back to `"Net90"` — the output
is equivalent for this spec.
