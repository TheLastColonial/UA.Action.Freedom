# EDI.ELO

A typed .NET client for the French customs ELO (*Enveloppe Logistique Obligatoire*) EDI
API — creating, modifying and retrieving ELO envelopes used to pair customs declarations
with a physical crossing (APPAIRAGE/EMBARQUEMENT/DEBARQUEMENT).

The client in `Generated/` is [NSwag](https://github.com/RicoSuter/NSwag) codegen from the
committed OpenAPI spec (`docs/schemas/edi/API_BREXIT_ELO-1.2.0.yaml`) and is not
hand-edited. This package adds a small hand-written DI surface on top.

## Install

```
dotnet add package EDI.ELO
```

## Usage

```csharp
services.AddEloClient(options =>
{
    options.BaseUrl = new Uri("https://<gun2-endpoint>/");
});
```

Unlike the HMRC SDKs, `EloClientOptions.BaseUrl` has **no default** — the spec declares no
`servers:` entry, so there is no known production host to bake in. `AddEloClient` requires
a `configure` callback and throws `InvalidOperationException` immediately if it does not
set `BaseUrl`.

`AddEloClient` registers `IEloClient` as a typed `HttpClient` and sets an
`Accept: application/json` header. It returns the `IHttpClientBuilder` for further
chaining.

There is no OAuth handler to chain, unlike GVMS/PPNS: the ELO API has no security scheme
at all. `Authorization`, `messageCode`, `functionalId`, `messageId` and `correlationId`
are required header *parameters* on every one of the three generated operations
(create/modify/retrieve an envelope), so the caller supplies each of them on every call:

```csharp
var envelope = await client.CreerENVAsync(
    authorization: $"Bearer {token}",
    messageCode: "ENV_CRE01",
    functionalId: correlationId,
    messageId: Guid.NewGuid().ToString(),
    correlationId: correlationId,
    body: new ENV_CRE01 { IdentifiantsDeclaration = ["23GB..."] },
    cancellationToken);
```

The three operations are `CreerENVAsync` (create), `ModifierENVAsync` (modify) and
`RecupererENVAsync` (retrieve) — NSwag's PascalCase rendering of the spec's `creerENV`/
`modifierENV`/`recupererENV` operation IDs. Each throws `EloApiException` (or the generic
`EloApiException<ENV_CRE03>`/`<ENV_MOD03>`/`<ENV_REC03>` for the modelled 400 response) on
a non-2xx/non-declared status.

## Regenerating the client

See [`build/nswag/README.md`](../../build/nswag/README.md):

```
pwsh build/nswag/regenerate.ps1 -Api elo -Raw
```

**`-Raw` is not optional for this API.** The normal preprocessing pipeline unconditionally
strips any header parameter literally named `Authorization` (a `HttpClient`-pipeline
concern for the HMRC specs); for ELO that header is a real per-call caller-supplied
parameter, so preprocessing would silently delete it from every generated method.
`regenerate.ps1`/`.sh` refuse to run `-Api elo` without `-Raw`.

Do not hand-edit `Generated/`.

## Known generator quirks (harmless)

- `dateCreation` / `dateModification` / `dateValidation` / `dateAppairage` /
  `dateEmbarquement` / `dateDebarquement` are declared in the spec as plain `type: string`
  (no `format: date-time`), so NSwag generates them as `string?`, not `DateTimeOffset?`.
  Callers must parse/format these themselves. Left as-is rather than hand-editing the
  committed third-party spec.
- `pdf` is declared as `type: string, enum: [formatbytebase64]` — a single-value enum used
  by the French customs API to *label* the encoding of a (currently absent) payload rather
  than carry real content. NSwag generates this as a genuine one-member C# enum. Harmless,
  but odd if you go looking for the actual PDF bytes.
- `ENV_NOT01` (the inbound passage/pairing notification: APPAIRAGE/EMBARQUEMENT/
  DEBARQUEMENT) is not referenced by any path in the spec, so it does not appear on
  `IEloClient` — it is generated purely as a DTO (`ENV_NOT01`, kept as-is by the generator
  since it's already a valid C# identifier) for callers who need to deserialise it from
  wherever ELO delivers passage notifications (out of scope for this library, which only
  wraps the three documented HTTP operations).
