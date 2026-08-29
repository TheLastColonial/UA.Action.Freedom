using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Application.Receivers;
using UA.Action.Freedom.Application.Vehicles;

namespace UA.Action.Freedom.Tests.Component;

/// <summary>
/// Builds the Freedom Application in memory with an explicit configuration, so each test
/// states the environment it is describing rather than inheriting one.
/// </summary>
internal static class FreedomApi
{
    /// <summary>
    /// The application as it runs with nothing behind it — no database, no storage account,
    /// no identity provider. This is the state a developer hits before
    /// <c>docker compose up</c>, and the application is expected to start anyway.
    /// </summary>
    internal static WebApplicationFactory<Program> WithNoBackingServices() =>
        With(new Dictionary<string, string?>());

    /// <summary>
    /// The application pointed at backing services that are configured but unreachable —
    /// the shape of a misconfiguration, or of Azure SQL still waking from auto-pause.
    /// </summary>
    internal static WebApplicationFactory<Program> WithUnreachableBackingServices() =>
        With(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Freedom"] =
                "Server=127.0.0.1,14330;Database=Freedom;User Id=sa;Password=nope;TrustServerCertificate=True;Connect Timeout=1",
            ["Storage:ConnectionString"] =
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10990/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10991/devstoreaccount1;",
            ["Oidc:MetadataAddress"] = "http://127.0.0.1:10992/realms/freedom/.well-known/openid-configuration",
        });

    /// <summary>
    /// The application with its vehicle persistence swapped for <paramref name="repository"/>
    /// and its JWT scheme swapped for <see cref="TestAuthHandler"/>. <paramref name="roles"/>
    /// are the app roles the caller's token carries; pass <c>authenticated: false</c> to send
    /// no credentials at all.
    /// </summary>
    internal static WebApplicationFactory<Program> WithVehicles(
        IVehicleRepository repository,
        bool authenticated = true,
        params string[] roles) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVehicleRepository>();
                services.AddScoped(_ => repository);

                services
                    .AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, options =>
                    {
                        options.Roles = roles;
                        options.Authenticated = authenticated;
                    });
            });
        });

    /// <summary>
    /// The application with its volunteer persistence swapped for <paramref name="repository"/>
    /// and its JWT scheme swapped for <see cref="TestAuthHandler"/>. <paramref name="roles"/> are
    /// the app roles the caller's token carries; pass <c>authenticated: false</c> to send no
    /// credentials at all.
    /// </summary>
    internal static WebApplicationFactory<Program> WithPeople(
        IPersonRepository repository,
        bool authenticated = true,
        params string[] roles) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPersonRepository>();
                services.AddScoped(_ => repository);

                services
                    .AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, options =>
                    {
                        options.Roles = roles;
                        options.Authenticated = authenticated;
                    });
            });
        });

    /// <summary>
    /// The application with its convoy persistence swapped for <paramref name="repository"/>
    /// and its JWT scheme swapped for <see cref="TestAuthHandler"/>.
    /// </summary>
    internal static WebApplicationFactory<Program> WithConvoys(
        IConvoyRepository repository,
        bool authenticated = true,
        params string[] roles) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConvoyRepository>();
                services.AddScoped(_ => repository);

                services
                    .AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, options =>
                    {
                        options.Roles = roles;
                        options.Authenticated = authenticated;
                    });
            });
        });

    /// <summary>
    /// The application with both halves of receiver persistence swapped out and its JWT scheme
    /// swapped for <see cref="TestAuthHandler"/>. Both halves are replaced together because the
    /// endpoints that matter here span them — deleting a receiver touches its address.
    /// </summary>
    internal static WebApplicationFactory<Program> WithReceivers(
        IReceiverRepository receivers,
        IReceiverDetailRepository detail,
        bool authenticated = true,
        params string[] roles) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IReceiverRepository>();
                services.AddScoped(_ => receivers);
                services.RemoveAll<IReceiverDetailRepository>();
                services.AddScoped(_ => detail);

                services
                    .AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, options =>
                    {
                        options.Roles = roles;
                        options.Authenticated = authenticated;
                    });
            });
        });

    internal static WebApplicationFactory<Program> With(IDictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
}
