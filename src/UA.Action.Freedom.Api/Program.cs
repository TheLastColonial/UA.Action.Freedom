using System.Text.Json.Serialization;
using FluentValidation;
using Scalar.AspNetCore;
using UA.Action.Freedom.Api.Configuration;
using UA.Action.Freedom.Api.Health;
using UA.Action.Freedom.Api.Installer;
using UA.Action.Freedom.Api.Vehicles;
using UA.Action.Freedom.Application;
using UA.Action.Freedom.Data;

var builder = WebApplication.CreateBuilder(args);

// Configuration comes from the environment and nothing else: the same image runs locally,
// in the local Azure simulation and on Container Apps, told apart only by what it is given.
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<OidcOptions>(builder.Configuration.GetSection(OidcOptions.SectionName));

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

app.UseAuthentication();
app.UseAuthorization();

app.MapFreedomHealthChecks();
app.MapFreedomVehicles();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

/// <summary>Exposed so component tests can host the application in memory.</summary>
public partial class Program;
