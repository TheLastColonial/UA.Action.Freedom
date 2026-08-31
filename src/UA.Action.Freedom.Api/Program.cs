using System.Text.Json.Serialization;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using FluentValidation;
using Scalar.AspNetCore;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Boxes;
using UA.Action.Freedom.Api.Convoys;
using UA.Action.Freedom.Api.Health;
using UA.Action.Freedom.Api.Manifests;
using UA.Action.Freedom.Api.Messaging;
using UA.Action.Freedom.Api.Receivers;
using UA.Action.Freedom.Api.Installer;
using UA.Action.Freedom.Api.People;
using UA.Action.Freedom.Api.Vehicles;
using UA.Action.Freedom.Application;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Data;

var builder = WebApplication.CreateBuilder(args);

// Configuration comes from the environment and nothing else: the same image runs locally,
// in the local Azure simulation and on Container Apps, told apart only by what it is given.
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<OidcOptions>(builder.Configuration.GetSection(OidcOptions.SectionName));
builder.Services.Configure<CustomsOptions>(builder.Configuration.GetSection(CustomsOptions.SectionName));

var hosting = builder.Configuration.GetSection(HostingOptions.SectionName).Get<HostingOptions>()
              ?? new HostingOptions();
var storage = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
              ?? new StorageOptions();
var oidc = builder.Configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>()
              ?? new OidcOptions();

// Accept and emit enum values (Fuel, Transmission) by name rather than ordinal.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddFreedomTelemetry();

builder.Services.AddFreedomStorage(storage);
builder.Services.AddFreedomDataProtection(storage);
builder.Services.AddFreedomHealthChecks();

builder.Services.AddProblemDetails();
builder.Services.AddFreedomAuthentication(oidc, builder.Environment.IsDevelopment());
builder.Services.AddFreedomAuthorization();

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFreedomApplication();
builder.Services.AddFreedomData();

// The durable hand-off to the Customs Worker. Pull, not push: Freedom exposes no callback
// endpoint and the worker polls HMRC for outcomes (recommendations 4.1).
builder.Services.AddScoped<IManifestWorkQueue>(provider => new AzureManifestWorkQueue(
    // GetService, not GetRequiredService: the queue client is only registered when a storage
    // account is configured, and the application is expected to start without one.
    provider.GetService<QueueServiceClient>(),
    provider.GetRequiredService<IOptions<StorageOptions>>(),
    provider.GetRequiredService<IOptions<CustomsOptions>>()));

var app = builder.Build();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Off wherever something in front already terminates TLS — Cloudflare into Container Apps
// in the target design, Traefik into this container locally. See HostingOptions.
if (hosting.UseHttpsRedirection)
{
    app.UseHttpsRedirection();
}

// The operator SPA is baked into wwwroot/app at image-build time and served from the same
// origin as the API, under /app so its client routes never collide with an API route.
// Static assets are public — the SPA runs its own OIDC flow — so this sits ahead of
// authentication. A no-op when wwwroot is absent.
if (hosting.ServeStaticFrontend)
{
    app.UseStaticFiles();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapFreedomHealthChecks();
app.MapFreedomVehicles();
app.MapFreedomPeople();
app.MapFreedomConvoys();
app.MapFreedomReceivers();
app.MapFreedomBoxes();
app.MapFreedomManifests();

// Only paths under /app that are not a real static asset reach here — the SPA's own router
// then takes over. Scoped to /app, so no API route, health probe or OpenAPI document is
// ever shadowed. Misses (404) when the SPA has not been built into wwwroot/app.
if (hosting.ServeStaticFrontend)
{
    app.MapFallbackToFile("/app/{*path}", "app/index.html");
}

app.Run();

/// <summary>Exposed so component tests can host the application in memory.</summary>
public partial class Program;
