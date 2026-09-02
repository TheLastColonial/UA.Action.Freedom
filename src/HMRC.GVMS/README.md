# HMRC.GVMS

A typed .NET client for the HMRC [Goods Vehicle Movements Haulier API](https://developer.service.hmrc.gov.uk/api-documentation/docs/api/service/goods-movement-system-haulier-api/1.0/oas/page) (GVMS) — creating, reading and updating Goods Movement References (GMRs).

The client in `Generated/` is [NSwag](https://github.com/RicoSuter/NSwag) codegen from the committed OpenAPI spec and is not hand-edited. This package adds a small hand-written DI surface on top.

## Install

```
dotnet add package HMRC.GVMS
```

## Usage

```csharp
services.AddGvmsClient(options =>
    {
        // Defaults to GvmsClientOptions.ProductionBaseUrl; switch to the HMRC sandbox with:
        options.BaseUrl = new Uri(GvmsClientOptions.SandboxBaseUrl);
    })
    // Authentication is the caller's responsibility — attach a handler that adds the
    // OAuth 2.0 bearer token GVMS requires:
    .AddHttpMessageHandler<HmrcOAuthHandler>();
```

`AddGvmsClient` registers `IGvmsClient` as a typed `HttpClient` and sets the
`Accept: application/vnd.hmrc.1.0+json` header (HMRC versions by content negotiation, not URL path).
It returns the `IHttpClientBuilder` for further chaining.

## Regenerating the client

See [`build/nswag/README.md`](../../build/nswag/README.md):

```
pwsh build/nswag/regenerate.ps1 -Api goods-vehicle-movements
```

Do not hand-edit `Generated/`.

## Known limitations

- The GMR body's "exactly one of `emptyVehicle` / `dbcDeclaration` / …" rule is not enforced by the client (it was a `not` block the generator drops). Callers must respect it.
- A few structurally identical sub-shapes are merged, and a few that genuinely differ between the summary and full GMR keep numeric suffixes (`Link` / `Link2`, `RuleFailure` / `RuleFailure2`).
