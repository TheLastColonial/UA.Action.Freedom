# HMRC.PushPullNotifications

A typed .NET client for the HMRC [Push Pull Notifications API](https://developer.service.hmrc.gov.uk/api-documentation/docs/api/service/push-pull-notifications-api/1.0/oas/page) (PPNS) — pulling and acknowledging the notifications HMRC posts to a registered box (used here to collect GMR outcomes without exposing an inbound webhook).

The client in `Generated/` is [NSwag](https://github.com/RicoSuter/NSwag) codegen from the committed OpenAPI spec and is not hand-edited. This package adds a small hand-written DI surface on top.

## Install

```
dotnet add package HMRC.PushPullNotifications
```

## Usage

```csharp
services.AddPushPullNotificationsClient(options =>
    {
        // Defaults to PushPullNotificationsClientOptions.ProductionBaseUrl; switch to the
        // HMRC sandbox with:
        options.BaseUrl = new Uri(PushPullNotificationsClientOptions.SandboxBaseUrl);
    })
    // Authentication is the caller's responsibility — attach a handler that adds the
    // OAuth 2.0 client-credentials bearer token (scope
    // PushPullNotificationsClientOptions.ReadScope for GET, WriteScope for the acknowledge):
    .AddHttpMessageHandler<HmrcOAuthHandler>();
```

`AddPushPullNotificationsClient` registers `IPushPullNotificationsClient` as a typed `HttpClient`
and sets the `Accept: application/vnd.hmrc.1.0+json` header (HMRC versions by content negotiation,
not URL path). It returns the `IHttpClientBuilder` for further chaining. The two operations are
`GetNotifications` and `AcknowledgeNotifications`.

## Regenerating the client

See [`build/nswag/README.md`](../../build/nswag/README.md):

```
pwsh build/nswag/regenerate.ps1 -Api push-pull-notifications
```

Do not hand-edit `Generated/`.

## Known bug — notifications cannot be deserialised

HMRC sends `"messageContentType": "application/json"`. NSwag generated the enum with
`[EnumMember(Value = "application/json")]` but decorated the property with
`JsonStringEnumConverter<MessageContentType>`, which matches C# member names (`Application_json`)
and ignores `[EnumMember]` — so every notification response throws `JsonException`. This affects
real HMRC, not just a stub. The fix belongs in `build/nswag/` plus a regeneration.
