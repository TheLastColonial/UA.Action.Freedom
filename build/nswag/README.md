# SDK client code generation

Generates the typed HTTP clients under `src/HMRC.*/Generated/` and `src/EDI.ELO/Generated/`
from the committed OpenAPI specs in `docs/schemas/hmrc/` and `docs/schemas/edi/`, using
[NSwag](https://github.com/RicoSuter/NSwag).

| API | Spec | Project |
| --- | --- | --- |
| Goods Vehicle Movements | `docs/schemas/hmrc/goods-vehicle-movements-1.0.yaml` | `HMRC.GVMS` |
| Push Pull Notifications | `docs/schemas/hmrc/push-pull-notifications-1.0.yaml` | `HMRC.PushPullNotifications` |
| French customs ELO (EDI) | `docs/schemas/edi/API_BREXIT_ELO-1.2.0.yaml` | `EDI.ELO` |

The generated code **is committed**. Regeneration is a manual step run by a developer when a
spec or the generator config changes; it is deliberately not wired into `dotnet build` or CI
(the CI `test`/`publish` jobs run `--no-build`).

## Regenerate

```pwsh
pwsh build/nswag/regenerate.ps1 -Api goods-vehicle-movements     # default
pwsh build/nswag/regenerate.ps1 -Api push-pull-notifications
pwsh build/nswag/regenerate.ps1 -Api elo                         # -Raw skips its corrections, see below
```

```bash
./build/nswag/regenerate.sh --api goods-vehicle-movements        # default
./build/nswag/regenerate.sh --api push-pull-notifications
./build/nswag/regenerate.sh --api elo                            # --raw skips its corrections, see below
```

Then review the diff under the project's `Generated/` folder and commit it together with any
change to the files in this folder. Re-running with no spec/config change produces no diff
(the pipeline is deterministic: LF endings, ordinal-sorted output, stable file names).

Options:

| Flag | Effect |
| --- | --- |
| `-Api` / `--api` | Which client to regenerate (see table above). Defaults to `goods-vehicle-movements`. |
| `-Raw` / `--raw` | Skip preprocessing; feed the untouched spec to NSwag. For comparison only — for ELO it also skips three corrections the client needs, and warns. |
| `-JsonLibrary` / `--json-library` | `SystemTextJson` (default) or `NewtonsoftJson`. |

Everything else is derived from `-Api` by convention:

```
docs/schemas/hmrc/<api>-1.0.yaml        raw spec (per-API override for elo, see below)
build/nswag/<api>.preprocess.json       spec-specific preprocessing config (passes 1, 2, 5, 6, 8)
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
into a conventional `$ref`-based document. Its structural passes run for every spec; the
spec-specific behaviour is driven by the `build/nswag/<api>.preprocess.json` sidecar:

```json
{
  "operationIds":             { "<raw operationId>": "<PascalCase name>" },
  "unwrapArrayComponents":    { "<array component key>": "<element component name>" },
  "componentRenames":         { "<old component key>": "<new component key>" },
  "preserveHeaderParameters": [ "<header name pass 2 must not drop>" ],
  "overrideProperties":       { "<component key>": { "<property>": { "type": "string" } } },
  "keepComponents":           [ "<component key pass 8 must not prune>" ]
}
```

Passes, in order:

1. **Pin operationIds** (`operationIds`) — we own the generated method names (NSwag appends
   `Async`).
2. **Drop transport header params** (`preserveHeaderParameters`) — the explicit `Accept` /
   `Authorization` / `Content-Type` header parameters are removed; they are `HttpClient`
   concerns. Except where they are not: a spec with no security scheme declares its bearer token
   as an ordinary header parameter, and dropping it deletes a credential from every generated
   signature. Name such a header in `preserveHeaderParameters` and this pass leaves it alone.
3. **Strip `not` / `not.anyOf`** — NSwag cannot express `not` and drops it silently; removing
   it keeps the spec honest.
4. **Collapse `oneOf` enums** — `oneOf` of single-value `enum` subschemas becomes a single
   `type: string` + `enum: [...]`.
5. **Override component properties** (`overrideProperties`) — replace (or add) a property on a
   named component schema, for a field the published spec describes in a way no client can use.
   Runs before de-duplication so the structural index is built from the corrected shape. Naming
   a component the spec does not define is an error rather than a silent no-op.
6. **Rename / unwrap artifact components** (`componentRenames`, `unwrapArrayComponents`) —
   give the RAML conversion-artifact component schemas clean names, and unwrap an array-typed
   component down to its element object. Runs before de-duplication so the structural index is
   seeded under these names.
7. **De-duplicate into `components/schemas`** — every distinct object/enum shape is hoisted
   into `components/schemas` (keyed by a structural hash that ignores `description` /
   `example` / `title` / `default`) and each occurrence is replaced with a `$ref`.
8. **Prune orphans** (`keepComponents`) — component schemas nothing references (to a fixed
   point) are removed so NSwag does not emit classes for them. `keepComponents` exempts one that
   callers genuinely want as a DTO even though no operation returns it — an inbound notification
   the API pushes, for instance — along with everything it transitively references.

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

### French customs ELO (EDI)

Unlike the two HMRC specs, `API_BREXIT_ELO-1.2.0.yaml` is a clean, hand-written OpenAPI 3.0.3
document with proper `$ref` reuse throughout — no RAML-conversion duplication to clean up. The
structural passes are therefore near no-ops for it, and `elo.preprocess.json` exists for three
corrections rather than for tidying:

1. **`preserveHeaderParameters: ["Authorization"]`.** The ELO spec declares no security scheme at
   all, so the bearer token is an ordinary header *parameter* — as are its siblings
   `messageCode`/`functionalId`/`messageId`/`correlationId`. Pass 2's default assumption would
   delete `Authorization` from every generated signature. This is why `-Api elo` used to demand
   `-Raw`; the exemption replaces that rule.
2. **`overrideProperties` on `pdf`.** See below.
3. **`keepComponents: ["ENV_NOT01"]`.** The passage notification
   (APPAIRAGE/EMBARQUEMENT/DEBARQUEMENT) is referenced by no path, so pass 8 would prune it and
   everything under it. It is kept deliberately: it is exactly the DTO a caller needs in order to
   deserialise a notification, even though no operation returns one.

`-Raw`/`--raw` still works for diffing but now warns, because it skips all three.

Its spec also has no `servers:` entry, so `EloClientOptions.BaseUrl` has no default (unlike
`GvmsClientOptions`/`PushPullNotificationsClientOptions`) — see `src/EDI.ELO/README.md`.

**The `pdf` correction, which was a real bug.** The spec declares
`pdf: { type: string, enum: [formatbytebase64] }` — a single-value enum naming an encoding rather
than carrying content — while its own worked examples send real base64 in that field. NSwag
faithfully generated a one-member C# enum with a `JsonStringEnumConverter`, which **throws on
every response that carries a barcode**, i.e. every successful create and retrieve. That is the
same defect class as the `HMRC.PushPullNotifications` `messageContentType` bug, except blocking
rather than cosmetic. `overrideProperties` rewrites it to plain `type: string`.

Two details worth keeping:

- **Plain `string`, not `format: byte`.** NJsonSchema would emit `byte[]` and base64-decode inside
  the deserialiser, which turns a malformed value into a crash. As a string the caller decodes
  explicitly, so a bad value becomes something a worker can dead-letter.
- **Both placements are bound.** The schema puts `pdf` inside `enveloppe`; the examples put it
  beside it. `overrideProperties` adds it to `ENV_CRE02`/`ENV_MOD02`/`ENV_REC02` as well as to
  `Enveloppe`/`EnveloppeREC02`, and a caller reads whichever arrived.

Remaining generator quirk, also documented in
[`src/EDI.ELO/README.md`](../../src/EDI.ELO/README.md):

- `dateCreation`/`dateModification`/etc. are `type: string` with no `format: date-time` in the
  spec, so they come out as `string?`, not `DateTimeOffset?`. Left as-is — unlike `pdf`, a string
  date is inconvenient rather than unreadable.

## Pinned versions

| Tool | Version | Where |
| --- | --- | --- |
| `NSwag.ConsoleCore` | `14.7.1` | `.config/dotnet-tools.json` |
| `Microsoft.CodeAnalysis.CSharp` (splitter) | `4.14.0` | `#:package` in `Split.cs` |
| `YamlDotNet` (preprocessor) | `16.2.1` | `#:package` in `PreprocessSpec.cs` |

Both `Split.cs` and `PreprocessSpec.cs` are file-based `dotnet run` apps, and the SDK
generates a virtual project for each on the fly. That virtual project lives under this repo
(so it inherits the root `Directory.Packages.props`), which would otherwise fail central
package management validation (`NU1008`) for a `#:package` version pin with no matching
`PackageVersion` entry. Both files carry `#:property ManagePackageVersionsCentrally=false`
right after their `#:package` line to opt the virtual project out — they're standalone
scripts, not part of the CPM-managed solution.

NSwag 14.7.1 ships a native `net10.0` build, so it runs directly on the .NET 10 SDK. The
manifest also sets `rollForward: true` as a safety net. If a future NSwag rejects
`"runtime": "Net100"` in the `.nswag` files, fall back to `"Net90"` — the output is
equivalent for these specs.
