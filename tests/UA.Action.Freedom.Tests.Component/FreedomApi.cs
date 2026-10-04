using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Declarations;
using UA.Action.Freedom.Application.Donations;
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

    /// <summary>As above, with the volunteer roster the caller's login is looked up in.</summary>
    internal static WebApplicationFactory<Program> WithVehicles(
        IVehicleRepository repository,
        IPersonRepository people,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(repository);
            services.Replace(people);
        });

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
        InMemoryConvoyRepository repository,
        IPersonRepository? people = null,
        IManifestRepository? manifests = null,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            // One fake backs both ports: they share the truck list, and the split in production is
            // two tables rather than two stores.
            services.Replace<IConvoyRepository>(repository);
            services.Replace<IConvoyVehicleRepository>(repository);
            services.Replace<IRoutePointReferences>(repository);
            services.Replace<IConvoyLeaderRepository>(repository);
            services.Replace(people ?? InMemoryPersonRepository.WithLinkedTestUser());
            var manifestFake = manifests ?? new InMemoryManifestRepository();
            ShareCargo(repository, manifestFake);

            // Opening a manifest is a convoy route now — POST /convoys/{id}/vehicles/{vin}/manifest
            // — so the convoy tests need somewhere for it to land.
            services.Replace<IManifestRepository>(manifestFake);
        });

    /// <summary>The convoy fake together with the budget fake, for the budget, cost and equipment routes.</summary>
    internal static WebApplicationFactory<Program> WithConvoyBudget(
        InMemoryConvoyRepository convoys,
        InMemoryConvoyBudgetRepository budget,
        InMemoryVehicleEquipmentRepository? equipment = null,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace<IConvoyRepository>(convoys);
            services.Replace<IConvoyVehicleRepository>(convoys);
            services.Replace<IConvoyBudgetRepository>(budget);
            services.Replace<IVehicleEquipmentRepository>(equipment ?? new InMemoryVehicleEquipmentRepository(convoys));
        });

    /// <summary>
    /// The convoy fake together with the accommodation fake, for the accommodation, coverage and task routes. The
    /// accommodation fake also tells the convoy fake which route points it stays at, as the foreign keys do.
    /// </summary>
    internal static WebApplicationFactory<Program> WithAccommodation(
        InMemoryConvoyRepository convoys,
        InMemoryAccommodationRepository accommodation,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace<IConvoyRepository>(convoys);
            services.Replace<IConvoyVehicleRepository>(convoys);
            services.Replace<IRoutePointReferences>(convoys);
            services.Replace<IConvoyLeaderRepository>(convoys);
            services.Replace<IAccommodationRepository>(accommodation);
            services.Replace(InMemoryPersonRepository.WithLinkedTestUser());
            services.Replace(new InMemoryManifestRepository());

            // The task list reads the declarations too, which read the boxes on each vehicle.
            services.Replace(new InMemoryBoxRepository());
            services.Replace<IDeclarationRepository>(new InMemoryDeclarationRepository(convoys));
        });

    /// <summary>
    /// The two SQL repositories read and write one allocation table; their fakes share one ledger.
    /// </summary>
    private static void ShareCargo(InMemoryConvoyRepository convoys, IManifestRepository manifests)
    {
        if (manifests is not InMemoryManifestRepository fake)
        {
            return;
        }

        convoys.Ledger.Absorb(fake.Ledger);
        fake.Ledger = convoys.Ledger;
    }

    /// <summary>As above, with the receivers a vehicle handover is checked against.</summary>
    internal static WebApplicationFactory<Program> WithConvoys(
        InMemoryConvoyRepository repository,
        IReceiverRepository receivers,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace<IConvoyRepository>(repository);
            services.Replace<IConvoyVehicleRepository>(repository);
            services.Replace<IConvoyLeaderRepository>(repository);
            services.Replace(receivers);
            services.Replace(new InMemoryManifestRepository());
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
        WithReceivers(receivers, detail, InMemoryPersonRepository.WithLinkedTestUser(), authenticated, roles);

    /// <summary>As above, with the volunteer roster the caller's login is looked up in.</summary>
    internal static WebApplicationFactory<Program> WithReceivers(
        IReceiverRepository receivers,
        IReceiverDetailRepository detail,
        IPersonRepository people,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(receivers);
            services.Replace(detail);
            services.Replace(people);
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
        IItemCategoryRepository? categories = null,
        InMemoryDonationRepository? donations = null,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(boxes);
            services.Replace(people);
            services.Replace(bays ?? new InMemoryBayRepository());
            if (categories is not null)
            {
                services.Replace(categories);
            }

            if (donations is not null)
            {
                services.Replace<IDonorRepository>(donations);
                services.Replace<IDonationRepository>(donations);
            }

        });

    /// <summary>As above, with the receivers a box destination is checked against.</summary>
    internal static WebApplicationFactory<Program> WithBoxes(
        IBoxRepository boxes,
        IReceiverRepository receivers,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(boxes);
            services.Replace(receivers);
        });

    /// <summary>
    /// Everything resource scope reads, in one place: convoys (and who leads them), boxes, locations and the Loader
    /// assignments, with the test caller linked to a volunteer. For tests about who may reach what (ADR 0010).
    /// </summary>
    internal static WebApplicationFactory<Program> WithScope(
        InMemoryConvoyRepository? convoys = null,
        InMemoryBoxRepository? boxes = null,
        InMemoryLocationRepository? locations = null,
        InMemoryLoaderAssignmentRepository? loaders = null,
        InMemoryPersonRepository? people = null,
        params string[] roles) =>
        WithFakes(true, roles, services =>
        {
            var convoyFake = convoys ?? new InMemoryConvoyRepository();
            var manifestFake = new InMemoryManifestRepository();
            ShareCargo(convoyFake, manifestFake);
            services.Replace<IConvoyRepository>(convoyFake);
            services.Replace<IConvoyVehicleRepository>(convoyFake);
            services.Replace<IConvoyLeaderRepository>(convoyFake);
            services.Replace<IManifestRepository>(manifestFake);
            services.Replace<IBoxRepository>(boxes ?? new InMemoryBoxRepository());
            services.Replace<ILocationRepository>(locations ?? new InMemoryLocationRepository());
            services.Replace<IBayRepository>(new InMemoryBayRepository());
            services.Replace<ILoaderAssignmentRepository>(loaders ?? new InMemoryLoaderAssignmentRepository());
            services.Replace<IPersonRepository>(people ?? InMemoryPersonRepository.WithLinkedTestUser());
        });

    /// <summary>The application with donor and donation persistence swapped for <paramref name="donations"/>, one fake behind both ports.</summary>
    internal static WebApplicationFactory<Program> WithDonations(
        InMemoryDonationRepository donations,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace<IDonorRepository>(donations);
            services.Replace<IDonationRepository>(donations);
        });

    /// <summary>The application with its item category persistence swapped for <paramref name="categories"/>.</summary>
    internal static WebApplicationFactory<Program> WithCategories(
        IItemCategoryRepository categories,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services => services.Replace(categories));

    /// <summary>The application with location and bay persistence swapped out.</summary>
    internal static WebApplicationFactory<Program> WithLocations(
        ILocationRepository locations,
        IBayRepository bays,
        bool authenticated = true,
        params string[] roles) =>
        WithLocations(locations, bays, InMemoryLoaderAssignmentRepository.ForTheTestCaller(), null, authenticated, roles);

    /// <summary>As above, with the Loader assignments (and optionally the roster they are checked against).</summary>
    internal static WebApplicationFactory<Program> WithLocations(
        ILocationRepository locations,
        IBayRepository bays,
        InMemoryLoaderAssignmentRepository loaders,
        IPersonRepository? people = null,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            services.Replace(locations);
            services.Replace(bays);
            services.Replace<ILoaderAssignmentRepository>(loaders);
            if (people is not null)
            {
                services.Replace(people);
            }
        });

    /// <summary>
    /// The application with manifest persistence, the convoy repository, the volunteer roster and
    /// the customs queue all swapped out. The manifest slice reaches across all four: proposing
    /// checks the convoy's truck list, crewing checks the roster, and approving queues a GMR.
    /// </summary>
    internal static WebApplicationFactory<Program> WithManifests(
        IManifestRepository manifests,
        InMemoryConvoyRepository convoys,
        IPersonRepository people,
        IManifestWorkQueue queue,
        IEloEnvelopeStore? envelopes = null,
        InMemoryDeclarationRepository? declarations = null,
        IEnsDeclarationStore? ensDetails = null,
        DeclarationSubmissionModes? submissionModes = null,
        IBoxRepository? boxes = null,
        IReceiverRepository? receivers = null,
        bool authenticated = true,
        params string[] roles) =>
        WithFakes(authenticated, roles, services =>
        {
            ShareCargo(convoys, manifests);

            // Replacing a box moves its cargo allocation, which both fakes read through the one ledger.
            if (boxes is InMemoryBoxRepository replaceable)
            {
                replaceable.Cargo = convoys.Ledger;
            }

            // A declaration's snapshot reads the boxes on the vehicle, their items and their receivers.
            services.Replace(boxes ?? new InMemoryBoxRepository());
            if (receivers is not null)
            {
                services.Replace(receivers);
            }

            services.Replace(manifests);
            services.Replace<IConvoyRepository>(convoys);
            services.Replace<IConvoyVehicleRepository>(convoys);
            services.Replace(people);
            services.Replace(queue);

            // The read side of the envelope hand-off. Empty unless a test says otherwise, which is
            // what a manifest looks like before the Customs Worker has got to it.
            services.Replace(envelopes ?? new InMemoryEloEnvelopeStore());

            // A vehicle's declarations. Empty unless a test says otherwise: no ENS is accepted, so an ELO is
            // refused until a test seeds one. Manual submission is the default, as in production (ADR 0006).
            services.Replace<IDeclarationRepository>(declarations ?? new InMemoryDeclarationRepository(convoys));
            services.Replace(ensDetails ?? new InMemoryEnsDeclarationStore());
            services.AddSingleton(submissionModes ?? new DeclarationSubmissionModes());
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
                EnsureCallerIsOnFile(services);
                EnsureReceiversAreFaked(services);
                EnsureCategoriesAreFaked(services);
                EnsureDonationsAreFaked(services);
                EnsureBudgetIsFaked(services);
                EnsureEquipmentIsFaked(services);
                EnsureScopeIsFaked(services);
                EnsureCargoIsFaked(services);
                EnsureAccommodationIsFaked(services);

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
        services.AddScoped(provider =>
        {
            if (instance is IRecordsWhoChanged fake)
            {
                fake.Attach(
                    provider.GetRequiredService<IChangeAttribution>(),
                    instance as IPersonRepository ?? provider.GetRequiredService<IPersonRepository>());
            }

            return instance;
        });
    }

    /// <summary>
    /// Naming a receiver, for a box or a vehicle, checks it is registered, so a test that did not supply
    /// a receiver store gets an empty one rather than a route to a real database.
    /// </summary>
    private static void EnsureReceiversAreFaked(IServiceCollection services)
    {
        var fake = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IReceiverRepository) && descriptor.ImplementationFactory is not null);

        if (!fake)
        {
            services.Replace<IReceiverRepository>(new InMemoryReceiverRepository());
        }
    }

    /// <summary>
    /// Packing an item names its category, so a test that did not supply a category store gets a small fixed
    /// list rather than a route to a real database.
    /// </summary>
    private static void EnsureCategoriesAreFaked(IServiceCollection services)
    {
        var fake = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IItemCategoryRepository) && descriptor.ImplementationFactory is not null);

        if (!fake)
        {
            services.Replace<IItemCategoryRepository>(InMemoryItemCategoryRepository.WithDefaults());
        }
    }

    /// <summary>
    /// Packing an item may name its donation, so a test that did not supply a donation store gets an empty one
    /// rather than a route to a real database.
    /// </summary>
    private static void EnsureDonationsAreFaked(IServiceCollection services)
    {
        var fake = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IDonationRepository) && descriptor.ImplementationFactory is not null);

        if (!fake)
        {
            var donations = new InMemoryDonationRepository();
            services.Replace<IDonorRepository>(donations);
            services.Replace<IDonationRepository>(donations);
        }
    }

    /// <summary>
    /// The convoy routes that read the budget (readiness) get an empty one when a test did not supply its own,
    /// rather than a route to a real database.
    /// </summary>
    private static void EnsureBudgetIsFaked(IServiceCollection services)
    {
        var fake = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IConvoyBudgetRepository) && descriptor.ImplementationFactory is not null);

        if (!fake)
        {
            services.Replace<IConvoyBudgetRepository>(new InMemoryConvoyBudgetRepository());
        }
    }

    /// <summary>
    /// Replacing a box asks where its cargo is, and whether that vehicle's load is frozen, so a test that did not
    /// supply a truck list and manifests gets empty ones, sharing one ledger as the SQL repositories share a table.
    /// </summary>
    private static void EnsureCargoIsFaked(IServiceCollection services)
    {
        var fake = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IConvoyVehicleRepository) && descriptor.ImplementationFactory is not null);

        if (fake)
        {
            return;
        }

        var convoys = new InMemoryConvoyRepository();
        var manifests = new InMemoryManifestRepository();
        ShareCargo(convoys, manifests);
        services.Replace<IConvoyRepository>(convoys);
        services.Replace<IConvoyVehicleRepository>(convoys);
        services.Replace<IManifestRepository>(manifests);    
    }
  
    /// <summary>Accommodation is read into the budget and the task list, so a test that did not supply a store gets an empty one.</summary>
    private static void EnsureAccommodationIsFaked(IServiceCollection services)
    {
        var fake = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IAccommodationRepository) && descriptor.ImplementationFactory is not null);

        if (!fake)
        {
            services.Replace<IAccommodationRepository>(new InMemoryAccommodationRepository());
        }
    }

    /// <summary>Equipment is read into the budget summary, so a test that did not supply a store gets an empty one.</summary>
    private static void EnsureEquipmentIsFaked(IServiceCollection services)
    {
        var fake = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IVehicleEquipmentRepository) && descriptor.ImplementationFactory is not null);

        if (!fake)
        {
            services.Replace<IVehicleEquipmentRepository>(new InMemoryVehicleEquipmentRepository());
        }
    }

    /// <summary>
    /// Scope reads two assignment stores on every request (ADR 0010): who leads which convoy, and which locations a
    /// Loader manages. A test that supplied neither gets empty ones, so nobody is scoped into anything and no route
    /// reaches for a real database.
    /// </summary>
    private static void EnsureScopeIsFaked(IServiceCollection services)
    {
        if (!IsFaked<IConvoyLeaderRepository>(services))
        {
            services.Replace<IConvoyLeaderRepository>(new InMemoryConvoyRepository());
        }

        if (!IsFaked<ILoaderAssignmentRepository>(services))
        {
            services.Replace<ILoaderAssignmentRepository>(InMemoryLoaderAssignmentRepository.ForTheTestCaller());
        }

        // A scoped Loader is checked against where the box is, so the box store is never a route to a real database.
        if (!IsFaked<IBoxRepository>(services))
        {
            services.Replace<IBoxRepository>(new InMemoryBoxRepository());
        }
    }

    private static bool IsFaked<TService>(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(TService) && descriptor.ImplementationFactory is not null);

    /// <summary>
    /// Every write is signed as the caller's linked volunteer, so a test that did not supply its own
    /// roster still gets one in which the test caller is on file.
    /// </summary>
    private static void EnsureCallerIsOnFile(IServiceCollection services)
    {
        var rosterIsFake = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IPersonRepository) && descriptor.ImplementationFactory is not null);

        if (!rosterIsFake)
        {
            services.Replace<IPersonRepository>(InMemoryPersonRepository.WithLinkedTestUser());
        }
    }
}
