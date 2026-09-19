using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Locations;
using UA.Action.Freedom.Application.Manifests;
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
        WithFakes(authenticated, roles, services => services.Replace(repository));

    /// <summary>The application with its volunteer persistence swapped for <paramref name="repository"/>.</summary>
    internal static WebApplicationFactory<Program> WithPeople(
        IPersonRepository repository,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services => services.Replace(repository));

    /// <summary>
    /// The application with its convoy persistence swapped for <paramref name="repository"/>, and
    /// the volunteer roster for <paramref name="people"/> — crewing a vehicle checks the person
    /// named is a driver, so without it those routes would reach for a real database.
    /// </summary>
    internal static WebApplicationFactory<Program> WithConvoys(
        IConvoyRepository repository,
        IPersonRepository? people = null,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(repository);
            services.Replace(people ?? new InMemoryPersonRepository());
        });

    /// <summary>
    /// The application with both halves of receiver persistence swapped out. Both are replaced
    /// together because the endpoints that matter here span them — deleting a receiver touches
    /// its address.
    /// </summary>
    internal static WebApplicationFactory<Program> WithReceivers(
        IReceiverRepository receivers,
        IReceiverDetailRepository detail,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(receivers);
            services.Replace(detail);
        });

    /// <summary>
    /// The application with box persistence, the bay repository and the volunteer roster
    /// swapped out. All three are needed together: validating a box and assigning it a bay both
    /// check that the volunteer named is on file, and bay assignment looks the bay up too.
    /// </summary>
    internal static WebApplicationFactory<Program> WithBoxes(
        IBoxRepository boxes,
        IPersonRepository people,
        IBayRepository? bays = null,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(boxes);
            services.Replace(people);
            services.Replace(bays ?? new InMemoryBayRepository());
        });

    /// <summary>The application with location and bay persistence swapped out.</summary>
    internal static WebApplicationFactory<Program> WithLocations(
        ILocationRepository locations,
        IBayRepository bays,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(locations);
            services.Replace(bays);
        });

    /// <summary>
    /// The application with manifest persistence, the convoy repository, the volunteer roster and
    /// the customs queue all swapped out. The manifest slice reaches across all four: proposing
    /// checks the convoy's truck list, crewing checks the roster, and approving queues a GMR.
    /// </summary>
    internal static WebApplicationFactory<Program> WithManifests(
        IManifestRepository manifests,
        IConvoyRepository convoys,
        IPersonRepository people,
        IManifestWorkQueue queue,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(manifests);
            services.Replace(convoys);
            services.Replace(people);
            services.Replace(queue);
        });

    /// <summary>
    /// The application with its static web root pointed at <paramref name="webRootPath"/> — a
    /// stand-in for the <c>wwwroot</c> the operator SPA is baked into at image-build time.
    /// Pass <paramref name="serveStaticFrontend"/> <see langword="false"/> to prove the host
    /// serves no SPA even when the directory is populated.
    /// </summary>
    internal static WebApplicationFactory<Program> WithWebRoot(
        string webRootPath,
        bool? serveStaticFrontend = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");
            builder.UseWebRoot(webRootPath);

            if (serveStaticFrontend is bool flag)
            {
                builder.UseSetting("Hosting:ServeStaticFrontend", flag ? "true" : "false");
            }
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

    /// <summary>
    /// The application with the given persistence fakes swapped in and its JWT scheme swapped for
    /// <see cref="TestAuthHandler"/>, carrying <paramref name="roles"/>.
    /// </summary>
    private static WebApplicationFactory<Program> WithFakes(
        bool authenticated, string[] roles, Action<IServiceCollection> swapFakes) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Hosting:UseHttpsRedirection", "false");

            builder.ConfigureTestServices(services =>
            {
                swapFakes(services);

                services
                    .AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<TestAuthOptions, TestAuthHandler>(TestAuthHandler.SchemeName, options =>
                    {
                        options.Roles = roles;
                        options.Authenticated = authenticated;
                    });
            });
        });

    private static void Replace<TService>(this IServiceCollection services, TService instance)
        where TService : class
    {
        services.RemoveAll<TService>();
        services.AddScoped(_ => instance);
    }
}
