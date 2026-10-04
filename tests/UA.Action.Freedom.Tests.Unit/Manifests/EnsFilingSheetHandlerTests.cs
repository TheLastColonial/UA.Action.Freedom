using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Application.Manifests;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Manifests;

/// <summary>
/// The filing sheet: everything an ICS2 Entry Summary Declaration asks for that Freedom can know.
/// </summary>
/// <remarks>
/// Not a submission. Freedom composes this, a Ground Officer reads it and types it into the EU
/// Customs Trader Portal, and what comes back is the MRN (<c>docs/adr/0003</c>).
///
/// <para>
/// Two properties matter more than any individual field. It is <strong>deliberately incomplete</strong>
/// — the consignee's address is not on it and never will be, because that address lives behind the
/// Ground Officer's own policy and a sheet that could carry one would be a targeting document. And it
/// <strong>reports its own gaps</strong>, because a dispatcher has to learn that a convoy cannot be
/// declared while there is still time to fix it, not at the terminal.
/// </para>
/// </remarks>
public class EnsFilingSheetHandlerTests
{
    private const string Id = "MAN-0001";
    private const int ConvoyId = 42;
    private const string Vin = "WVWZZZ1JZXW000001";
    private const string Plate = "AB12 CDE";

    private static readonly Guid Kharkiv = new("3f1a6c20-5b4d-4e71-8a92-1c0d7e2f4b33");
    private static readonly DateTime Departs = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The environment facts a declaration needs that no manifest knows. Supplied as a value rather
    /// than read from configuration here, the same boundary <c>GmrSubmissionRequest</c> draws: the
    /// Application composes what the manifest knows, and the API adapter supplies the rest.
    /// </summary>
    private static EnsFilingEnvironment AnEnvironment(
        string? eori = "GB123456789000",
        string? consignor = "Ukrainian Action",
        string? officeOfFirstEntry = "FR620001") =>
        new(eori, consignor, officeOfFirstEntry);

    private static ConvoyReadModel AConvoy(
        ChannelCrossing crossing = ChannelCrossing.Shuttle, string? vesselImo = null) => new(
        ConvoyId, Departs, Departs.AddDays(4), TruckListPublishedAt: Departs.AddDays(-10),
        ArrivedAt: null, CrossingMode: crossing, VesselImo: vesselImo);

    private static EnsGoodsLineReadModel AGoodsLine(
        int boxId = 1,
        string description = "Blankets",
        string? commodityCode = EnsCommodity.HumanitarianAid,
        bool validated = true,
        int weightKg = 30) =>
        new(boxId, weightKg, validated, Kharkiv, "Kharkiv Regional Aid", "Kharkiv oblast",
            description, commodityCode);

    private static IManifestRepository ARepository(params EnsGoodsLineReadModel[] lines)
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns(new ManifestReadModel(
            Id, ConvoyId, Vin, ManifestStatus.Created, null,
            GmrSubmittedAt: null));
        repository.GetVehiclePlateAsync(Id, Arg.Any<CancellationToken>()).Returns(Plate);
        repository.GetVehicleWeightKgAsync(Id, Arg.Any<CancellationToken>()).Returns(1_400);
        repository.GetEnsGoodsLinesAsync(Id, Arg.Any<CancellationToken>()).Returns(lines);
        return repository;
    }

    private static IConvoyRepository AConvoyRepository(
        ConvoyReadModel? convoy = null, params RouteStopReadModel[] route)
    {
        var convoys = Substitute.For<IConvoyRepository>();
        convoys.GetByIdAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns(convoy ?? AConvoy());
        convoys.GetRouteAsync(ConvoyId, Arg.Any<CancellationToken>()).Returns(
            route.Length == 0
                ? [AStop(1, "United Kingdom", "GB"), AStop(2, "France", "FR"), AStop(3, "Ukraine", "UA")]
                : route);
        return convoys;
    }

    private static RouteStopReadModel AStop(int sequence, string country, string? code) =>
        new(sequence, null, null, null, country, string.Empty, code);

    private static GetEnsFilingSheetHandler AHandler(
        IManifestRepository? manifests = null,
        IConvoyRepository? convoys = null,
        EnsFilingEnvironment? environment = null) =>
        new(manifests ?? ARepository(AGoodsLine()),
            convoys ?? AConvoyRepository(),
            environment ?? AnEnvironment());

    private static Task<EnsFilingSheetReadModel?> Sheet(GetEnsFilingSheetHandler handler) =>
        handler.HandleAsync(new GetEnsFilingSheetQuery(Id), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Reports_no_sheet_for_a_manifest_that_does_not_exist()
    {
        var repository = Substitute.For<IManifestRepository>();
        repository.GetByIdAsync(Id, Arg.Any<CancellationToken>()).Returns((ManifestReadModel?)null);

        var sheet = await Sheet(AHandler(manifests: repository));

        sheet.Should().BeNull();
    }

    [Fact]
    public async Task Names_the_charity_as_declarant_carrier_and_consignor()
    {
        var sheet = await Sheet(AHandler());

        sheet!.DeclarantEori.Should().Be("GB123456789000");
        sheet.CarrierEori.Should().Be("GB123456789000");
        sheet.ConsignorName.Should().Be("Ukrainian Action");
        sheet.OfficeOfFirstEntry.Should().Be("FR620001");
    }

    /// <summary>
    /// Mode of transport follows the crossing, and it is non-amendable in ICS2 — a mistake means
    /// invalidating the declaration and filing a new one, not correcting it.
    /// </summary>
    [Fact]
    public async Task A_shuttle_crossing_is_declared_as_road_and_names_the_lorry()
    {
        var sheet = await Sheet(AHandler(convoys: AConvoyRepository(AConvoy(ChannelCrossing.Shuttle))));

        sheet!.ModeOfTransportCode.Should().Be(EnsTransportMode.Road);
        sheet.ActiveMeansOfTransport.Should().Be(Plate);
        sheet.PassiveMeansOfTransport.Should().Be(Plate);
    }

    /// <summary>
    /// On a ferry the vessel is the active means of transport, so the IMO is what is declared — and
    /// the lorry becomes the passive means. The IMO may never be amended once sent.
    /// </summary>
    [Fact]
    public async Task A_ferry_crossing_is_declared_as_maritime_and_names_the_vessel()
    {
        var convoys = AConvoyRepository(AConvoy(ChannelCrossing.Ferry, vesselImo: "9245779"));

        var sheet = await Sheet(AHandler(convoys: convoys));

        sheet!.ModeOfTransportCode.Should().Be(EnsTransportMode.Maritime);
        sheet.ActiveMeansOfTransport.Should().Be("9245779");
        sheet.PassiveMeansOfTransport.Should().Be(Plate);
    }

    /// <summary>
    /// Countries of routing, in journey order and de-duplicated. Missing a transit country stops EU
    /// customs completing its pre-arrival risk assessment.
    /// </summary>
    [Fact]
    public async Task Declares_the_countries_the_convoy_passes_through_in_order()
    {
        var convoys = AConvoyRepository(
            AConvoy(),
            AStop(1, "United Kingdom", "GB"),
            AStop(2, "France", "FR"),
            AStop(3, "France", "FR"),
            AStop(4, "Poland", "PL"),
            AStop(5, "Ukraine", "UA"));

        var sheet = await Sheet(AHandler(convoys: convoys));

        sheet!.ItineraryCountryCodes.Should().Equal("GB", "FR", "PL", "UA");
    }

    /// <summary>
    /// An ENS groups goods by consignment, and a consignment is what goes to one consignee. Boxes on
    /// a manifest may be for different receivers, so the sheet groups them rather than listing boxes.
    /// </summary>
    [Fact]
    public async Task Groups_the_cargo_into_one_consignment_per_receiver()
    {
        var other = new Guid("8d2b7f31-6c5e-4f82-9b03-2d1e8f3a5c44");
        var repository = ARepository(
            AGoodsLine(boxId: 1, description: "Blankets"),
            AGoodsLine(boxId: 1, description: "Sleeping bags"),
            AGoodsLine(boxId: 2, description: "Bandages") with
            {
                ReceiverRef = other, ReceiverOrganisation = "Lviv Medical Trust", ReceiverRegion = "Lviv oblast",
            });

        var sheet = await Sheet(AHandler(manifests: repository));

        sheet!.Consignments.Should().HaveCount(2);

        var kharkiv = sheet.Consignments.Single(consignment => consignment.ReceiverRef == Kharkiv);
        kharkiv.ReceiverOrganisation.Should().Be("Kharkiv Regional Aid");
        kharkiv.PackageCount.Should().Be(1, "both items are in the same box, and a box is the package");
        kharkiv.GoodsItems.Select(item => item.Description).Should().Equal("Blankets", "Sleeping bags");
        kharkiv.GoodsItems.Select(item => item.Number).Should().Equal(1, 2);
    }

    [Fact]
    public async Task Declares_each_package_as_a_box()
    {
        var sheet = await Sheet(AHandler());

        sheet!.Consignments.Should().ContainSingle()
            .Which.PackageTypeCode.Should().Be(EnsPackaging.Box);
    }

    /// <summary>
    /// Gross mass is the border-check total: the vehicle, the cargo, the fixed crew-and-bags
    /// allowance and the fuel allowance, from the one place that convention lives.
    /// </summary>
    [Fact]
    public async Task Declares_the_border_check_gross_mass()
    {
        var sheet = await Sheet(AHandler());

        sheet!.GrossMassKg.Should().Be(ManifestWeight.Total(1_400, 30));
        sheet.GrossMassProvisional.Should().BeFalse();
    }

    /// <summary>
    /// An unvalidated box weighs zero until a Loader says otherwise, so a total that includes one is
    /// provisional — and filing a provisional gross mass silently is how a weight discrepancy becomes
    /// a border inspection.
    /// </summary>
    [Fact]
    public async Task Says_when_the_gross_mass_is_still_provisional()
    {
        var repository = ARepository(AGoodsLine(validated: false, weightKg: 0));

        var sheet = await Sheet(AHandler(manifests: repository));

        sheet!.GrossMassProvisional.Should().BeTrue();
        sheet.Missing.Should().Contain(missing => missing.Contains("validated"));
    }

    /// <summary>
    /// The heart of the redaction. The consignee is named at organisation and region only — the same
    /// precision as the travelling document — and the sheet says outright that the address is
    /// withheld, so the filer knows to fetch it themselves rather than assuming there is none.
    /// </summary>
    [Fact]
    public async Task Names_the_consignee_but_withholds_the_address()
    {
        var sheet = await Sheet(AHandler());

        var consignment = sheet!.Consignments.Should().ContainSingle().Subject;
        consignment.ReceiverOrganisation.Should().Be("Kharkiv Regional Aid");
        consignment.ReceiverRegion.Should().Be("Kharkiv oblast");
        consignment.ConsigneeAddressWithheld.Should().BeTrue();
        sheet.ConsigneeAddressSource.Should().Be("GET /receivers/{ref}/detail");
    }

    /// <summary>
    /// Asserted on the types, not on one instance. The sheet is composed from a connection that is
    /// DENY'd on the <c>sensitive</c> schema, so it could not carry an address today — this is what
    /// stops a later change adding a field that could.
    /// </summary>
    [Fact]
    public void The_filing_sheet_has_nowhere_to_put_an_address()
    {
        var names = typeof(EnsFilingSheetReadModel).GetProperties()
            .Concat(typeof(EnsConsignmentReadModel).GetProperties())
            .Concat(typeof(EnsGoodsItemReadModel).GetProperties())
            .Select(property => property.Name)
            .ToList();

        names.Should().NotContain("Street").And.NotContain("House").And.NotContain("City")
            .And.NotContain("Postcode").And.NotContain("Address")
            .And.NotContain("ContactName").And.NotContain("ContactPhone");
    }

    /// <summary>
    /// ICS2 wants at least six digits per goods item, and an item nobody has classified would be
    /// refused. Reported per item rather than as one flag, so a packer knows which box to look in.
    /// </summary>
    [Fact]
    public async Task Reports_a_goods_item_with_no_commodity_code_as_missing()
    {
        var repository = ARepository(
            AGoodsLine(description: "Blankets"),
            AGoodsLine(boxId: 2, description: "Assorted donations", commodityCode: null));

        var sheet = await Sheet(AHandler(manifests: repository));

        sheet!.Missing.Should().Contain(missing => missing.Contains("Assorted donations"));
    }

    [Fact]
    public async Task Names_the_category_to_fix_when_neither_the_item_nor_its_category_has_an_EU_code()
    {
        var repository = ARepository(
            AGoodsLine(boxId: 2, description: "Assorted donations", commodityCode: null) with { CategoryName = "Clothing" });

        var sheet = await Sheet(AHandler(manifests: repository));

        sheet!.Missing.Should().ContainSingle(missing =>
            missing.Contains("Assorted donations") && missing.Contains("category \"Clothing\""));
    }

    [Fact]
    public async Task Reports_a_route_stop_with_no_country_code_as_missing()
    {
        var convoys = AConvoyRepository(
            AConvoy(), AStop(1, "United Kingdom", "GB"), AStop(2, "Someplace", null));

        var sheet = await Sheet(AHandler(convoys: convoys));

        sheet!.Missing.Should().Contain(missing => missing.Contains("Someplace"));
        sheet.ItineraryCountryCodes.Should().Equal("GB");
    }

    /// <summary>
    /// A ferry crossing with no vessel cannot be declared at all: the IMO is the active means of
    /// transport, it has to come from the official list for the route, and it may never be amended.
    /// </summary>
    [Fact]
    public async Task Reports_a_ferry_crossing_with_no_vessel_as_missing()
    {
        var convoys = AConvoyRepository(AConvoy(ChannelCrossing.Ferry, vesselImo: null));

        var sheet = await Sheet(AHandler(convoys: convoys));

        sheet!.Missing.Should().Contain(missing => missing.Contains("IMO"));
        sheet.ActiveMeansOfTransport.Should().BeNull();
    }

    [Fact]
    public async Task Reports_a_manifest_with_no_cargo_as_missing()
    {
        var sheet = await Sheet(AHandler(manifests: ARepository()));

        sheet!.Consignments.Should().BeEmpty();
        sheet.Missing.Should().Contain(missing => missing.Contains("cargo"));
    }

    [Fact]
    public async Task Reports_nothing_missing_for_a_sheet_that_can_be_filed()
    {
        var sheet = await Sheet(AHandler());

        sheet!.Missing.Should().BeEmpty();
        sheet.Complete.Should().BeTrue();
    }

    [Fact]
    public async Task Says_the_office_of_first_entry_is_missing_when_it_is_not_configured()
    {
        var sheet = await Sheet(AHandler(environment: AnEnvironment(officeOfFirstEntry: null)));

        sheet!.Missing.Should().Contain(missing => missing.Contains("office of first entry"));
        sheet.Complete.Should().BeFalse();
    }
}
