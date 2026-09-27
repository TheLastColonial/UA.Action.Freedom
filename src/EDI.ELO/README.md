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
pwsh build/nswag/regenerate.ps1 -Api elo
```

`build/nswag/elo.preprocess.json` carries three corrections this API needs — the
`Authorization` header parameter is preserved rather than dropped as a transport concern,
the unusable `pdf` schema is rewritten (below), and `ENV_NOT01` is kept from being pruned.
`-Raw` skips all three and now warns; use it only to diff.

Do not hand-edit `Generated/`.

## The barcode document

`Enveloppe.Pdf` / `EnveloppeREC02.Pdf` and `ENV_CRE02.Pdf` / `ENV_MOD02.Pdf` /
`ENV_REC02.Pdf` are `string?` holding **base64**, which the caller decodes:

```csharp
var base64 = created.Enveloppe?.Pdf ?? created.Pdf;
```

Both placements exist because the spec contradicts itself: the schema puts `pdf` inside
`enveloppe`, its worked examples put it beside it. Read whichever is non-null.

Decode explicitly and treat failure as a permanent error — an envelope whose document is
unreadable will not become readable on retry:

```csharp
if (!Convert.TryFromBase64String(base64, buffer, out var written)) { /* dead-letter */ }
```

> **Previously a blocking bug.** The spec declares `pdf` as
> `type: string, enum: [formatbytebase64]` — a single-value enum naming an encoding rather
> than carrying content — so NSwag generated a one-member C# enum with a
> `JsonStringEnumConverter`. That converter throws on any value but the member name, and the
> real API sends base64, so **every successful create and retrieve failed to deserialise**.
> An earlier version of this file described it as harmless; it was not. Fixed in
> `build/nswag/elo.preprocess.json` rather than by hand-editing the committed spec.

## Known generator quirks (harmless)

- `dateCreation` / `dateModification` / `dateValidation` / `dateAppairage` /
  `dateEmbarquement` / `dateDebarquement` are declared in the spec as plain `type: string`
  (no `format: date-time`), so NSwag generates them as `string?`, not `DateTimeOffset?`.
  Callers must parse/format these themselves. Left as-is rather than hand-editing the
  committed third-party spec — unlike `pdf`, a string date is inconvenient, not unreadable.
- `ENV_NOT01` (the inbound passage/pairing notification: APPAIRAGE/EMBARQUEMENT/
  DEBARQUEMENT) is not referenced by any path in the spec, so it does not appear on
  `IEloClient` — it is generated purely as a DTO for callers who need to deserialise one
  from wherever ELO delivers passage notifications (out of scope for this library, which
  only wraps the three documented HTTP operations). It survives the pruning pass only
  because `elo.preprocess.json` lists it in `keepComponents`.
