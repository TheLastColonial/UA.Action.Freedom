# HMRC client code generation

Generates the typed HTTP clients under `src/UA.Action.Freedom.Hmrc.*/Generated/` from the
committed HMRC OpenAPI specs in `docs/schemas/hmrc/`, using
[NSwag](https://github.com/RicoSuter/NSwag).

| API | Spec | Project |
| --- | --- | --- |
| Goods Vehicle Movements | `goods-vehicle-movements-1.0.yaml` | `UA.Action.Freedom.Hmrc.Gvms` |
| Push Pull Notifications | `push-pull-notifications-1.0.yaml` | `UA.Action.Freedom.Hmrc.PushPullNotifications` |

The generated code **is committed**. Regeneration is a manual step run by a developer when a
spec or the generator config changes; it is deliberately not wired into `dotnet build` or CI
(the CI `test`/`publish` jobs run `--no-build`).

## Regenerate

```pwsh
pwsh build/nswag/regenerate.ps1 -Api goods-vehicle-movements     # default
pwsh build/nswag/regenerate.ps1 -Api push-pull-notifications
```

```bash
./build/nswag/regenerate.sh --api goods-vehicle-movements        # default
./build/nswag/regenerate.sh --api push-pull-notifications
```

Then review the diff under the project's `Generated/` folder and commit it together with any
change to the files in this folder. Re-running with no spec/config change produces no diff
(the pipeline is deterministic: LF endings, ordinal-sorted output, stable file names).

Options:

| Flag | Effect |
| --- | --- |
| `-Api` / `--api` | Which client to regenerate (see table above). Defaults to `goods-vehicle-movements`. |
| `-Raw` / `--raw` | Skip preprocessing; feed the untouched spec to NSwag. For comparison only. |
| `-JsonLibrary` / `--json-library` | `SystemTextJson` (default) or `NewtonsoftJson`. |

Everything else is derived from `-Api` by convention:

```
docs/schemas/hmrc/<api>-1.0.yaml        raw spec
build/nswag/<api>.preprocess.json       spec-specific preprocessing config (pass 1 + 5)
build/nswag/<api>.nswag                 NSwag code-generator config
src/<project>/Generated/                committed output
```

## Pipeline

1. **`dotnet tool restore`** — restores NSwag, pinned in `.config/dotnet-tools.json`.
2. **`PreprocessSpec.cs`** (`dotnet run`, file-based app) — rewrites the raw spec into a
   conventional `$ref`-based OpenAPI **JSON** document under `build/nswag/generated/`. See
   below.
3. **`nswag run <api>.nswag`** — generates one monolithic C# file under
   `build/nswag/generated/`.
4. **`Split.cs`** (`dotnet run`, file-based app, Roslyn) — splits the monolith into one file
   per top-level type under `Generated/`, wiping stale `*.cs` first. A type and its generic
   sibling (`GvmsApiException` / `GvmsApiException<T>`) share a file.
5. **`dotnet build`** — smoke-builds the SDK project.

`build/nswag/generated/` is git-ignored scratch.

## Why preprocessing is needed

Both published HMRC specs are RAML → OpenAPI conversions. `PreprocessSpec.cs` normalises them
into a conventional `$ref`-based document. Its structural passes run for every spec; passes 1
and 5 are spec-specific and driven by the `build/nswag/<api>.preprocess.json` sidecar:

```json
{
  "operationIds":          { "<raw operationId>": "<PascalCase name>" },
  "unwrapArrayComponents": { "<array component key>": "<element component name>" },
  "componentRenames":      { "<old component key>": "<new component key>" }
}
```

Passes, in order:

1. **Pin operationIds** (`operationIds`) — we own the generated method names (NSwag appends
   `Async`).
2. **Drop transport header params** — the explicit `Accept` / `Authorization` /
   `Content-Type` header parameters are removed; they are `HttpClient` concerns.
3. **Strip `not` / `not.anyOf`** — NSwag cannot express `not` and drops it silently; removing
   it keeps the spec honest.
4. **Collapse `oneOf` enums** — `oneOf` of single-value `enum` subschemas becomes a single
   `type: string` + `enum: [...]`.
5. **Rename / unwrap artifact components** (`componentRenames`, `unwrapArrayComponents`) —
   give the RAML conversion-artifact component schemas clean names, and unwrap an array-typed
   component down to its element object. Runs before de-duplication so the structural index is
   seeded under these names.
6. **De-duplicate into `components/schemas`** — every distinct object/enum shape is hoisted
   into `components/schemas` (keyed by a structural hash that ignores `description` /
   `example` / `title` / `default`) and each occurrence is replaced with a `$ref`.
7. **Prune orphans** — component schemas nothing references (to a fixed point) are removed so
   NSwag does not emit classes for them.

### Goods Vehicle Movements

The published spec contains **zero `$ref`**: every schema is inlined and duplicated 3–5×, and
enums are modelled as `oneOf` of single-value `enum` subschemas. Fed to NSwag as-is that
yields ~170 classes (`Direction`, `Direction2`, `Direction3`, `PlannedCrossing` …
`PlannedCrossing5`, dozens of `Anonymous*` / `Response*`). After preprocessing: ~40 model
types with meaningful names.

Known residual gaps:

- The GMR body's "exactly one of `emptyVehicle` / `dbcDeclaration` / …" rule is **not**
  enforced by the client (it was a `not` block). Callers must respect it.
- Structurally identical shapes are merged: `actualCrossing` properties are typed the same as
  `checkedInCrossing` because the shapes are identical in the spec.
- A few sub-shapes that genuinely differ between the summary and full GMR keep numeric
  suffixes (`Link` / `Link2`, `RuleFailure` / `RuleFailure2`, and the `Method` / `Rel` link
  enums).

### Push Pull Notifications

The published spec ships 5 clean `components/schemas` but the path operations still inline
their request/response bodies (the error body is inlined ~9×). Preprocessing pins the two
operationIds (`Getalistofnotifications` → `GetNotifications`,
`Acknowledgealistofnotifications` → `AcknowledgeNotifications`), renames
`Listofnotification` → `Notification` and `Acknowledgealistofnotifications` →
`AcknowledgeNotificationsRequest`, and the de-duplication pass points every inline body at
the matching component. Result: 5 model types, 2 operations.

## Pinned versions

| Tool | Version | Where |
| --- | --- | --- |
| `NSwag.ConsoleCore` | `14.7.1` | `.config/dotnet-tools.json` |
| `Microsoft.CodeAnalysis.CSharp` (splitter) | `4.14.0` | `#:package` in `Split.cs` |
| `YamlDotNet` (preprocessor) | `16.2.1` | `#:package` in `PreprocessSpec.cs` |

NSwag 14.7.1 ships a native `net10.0` build, so it runs directly on the .NET 10 SDK. The
manifest also sets `rollForward: true` as a safety net. If a future NSwag rejects
`"runtime": "Net100"` in the `.nswag` files, fall back to `"Net90"` — the output is
equivalent for these specs.
