using AwesomeAssertions;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Domain;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// The closing report (O10, O25): boxes by outcome with counts and value, "delivered" defined once, vehicles by value, and
/// Receivers at organisation and region only.
/// </summary>
public class ConvoyReportsTests
{
    private static ReportBoxFacts ABox(
        int id, BoxOutcome? outcome, decimal value = 10m, string? organisation = "Lviv Hospital 4", string? region = "Lviv") =>
        new(id, outcome, value, organisation, region);

    private static ReportVehicleFacts AVehicle(
        string vin = "VIN-1", bool delivered = true, bool withdrawn = false, decimal? value = 4_000m,
        params ReportBoxFacts[] boxes) =>
        new(vin, "AB12CDE", value, ValueSource.Estimate, withdrawn, delivered, boxes);

    private static ConvoyReportFacts Facts(params ReportVehicleFacts[] vehicles) =>
        new(ConvoyTestData.Id, ConvoyTestData.Arrived, null, null, vehicles);

    [Fact]
    public void Counts_boxes_by_outcome_with_their_value()
    {
        var report = ConvoyReports.Compose(Facts(AVehicle(boxes:
        [
            ABox(1, BoxOutcome.Delivered, 100m),
            ABox(2, BoxOutcome.Accepted, 50m),
            ABox(3, BoxOutcome.Seized, 400m),
            ABox(4, BoxOutcome.Seized, 25m),
        ])));

        var seized = report.Vehicles.Single().ByOutcome.Single(o => o.Outcome == "Seized");
        seized.Boxes.Should().Be(2);
        seized.ValueGbp.Should().Be(425m);
    }

    [Fact]
    public void Delivered_value_leaves_out_seized_returned_and_undeliverable_boxes()
    {
        var report = ConvoyReports.Compose(Facts(AVehicle(boxes:
        [
            ABox(1, BoxOutcome.Delivered, 100m),
            ABox(2, BoxOutcome.Accepted, 50m),
            ABox(3, BoxOutcome.Seized, 400m),
            ABox(4, BoxOutcome.ReturnedToHub, 30m),
            ABox(5, BoxOutcome.Undeliverable, 20m),
        ])));

        report.Vehicles.Single().DeliveredValueGbp.Should().Be(150m);
        report.Totals.DeliveredValueGbp.Should().Be(150m);
        report.Totals.BoxesDelivered.Should().Be(2);
        report.Totals.BoxesNotDelivered.Should().Be(3);
    }

    [Fact]
    public void A_box_with_no_outcome_is_undecided_and_is_neither_delivered_nor_not_delivered()
    {
        var report = ConvoyReports.Compose(Facts(AVehicle(boxes: [ABox(1, null, 70m), ABox(2, BoxOutcome.Delivered, 5m)])));

        report.Vehicles.Single().ByOutcome.Single(o => o.Outcome == "Undecided").Boxes.Should().Be(1);
        report.Totals.BoxesUndecided.Should().Be(1);
        report.Totals.BoxesDelivered.Should().Be(1);
        report.Totals.BoxesNotDelivered.Should().Be(0);
        report.Totals.DeliveredValueGbp.Should().Be(5m);
    }

    [Fact]
    public void Receivers_are_aggregated_by_organisation_and_region_with_no_identifier()
    {
        var report = ConvoyReports.Compose(Facts(AVehicle(boxes:
        [
            ABox(1, BoxOutcome.Delivered),
            ABox(2, BoxOutcome.Seized),
            ABox(3, BoxOutcome.Delivered, organisation: "Kyiv Clinic", region: "Kyiv"),
        ])));

        var receivers = report.Vehicles.Single().Receivers;
        receivers.Should().HaveCount(2);
        var lviv = receivers.Single(r => r.Organisation == "Lviv Hospital 4");
        lviv.Region.Should().Be("Lviv");
        lviv.Boxes.Should().Be(2);
        lviv.BoxesDelivered.Should().Be(1);
    }

    [Fact]
    public void A_box_with_no_receiver_is_still_counted_under_an_unassigned_receiver()
    {
        var report = ConvoyReports.Compose(Facts(AVehicle(boxes: [ABox(1, BoxOutcome.Delivered, organisation: null, region: null)])));

        report.Vehicles.Single().Receivers.Single().Organisation.Should().Be("Unassigned");
    }

    [Fact]
    public void Vehicle_value_counts_only_vehicles_that_were_delivered_and_not_withdrawn()
    {
        var report = ConvoyReports.Compose(Facts(
            AVehicle("VIN-1", delivered: true, value: 4_000m),
            AVehicle("VIN-2", delivered: true, value: 2_500m),
            AVehicle("VIN-3", delivered: false, value: 9_000m),
            AVehicle("VIN-4", delivered: false, withdrawn: true, value: 1_000m)));

        report.Totals.VehicleValueGbp.Should().Be(6_500m);
    }

    [Fact]
    public void A_vehicle_with_no_recorded_value_adds_nothing()
    {
        var report = ConvoyReports.Compose(Facts(AVehicle(value: null)));

        report.Totals.VehicleValueGbp.Should().Be(0m);
        report.Vehicles.Single().ValueGbp.Should().BeNull();
    }

    [Fact]
    public void Carries_the_convoy_dates_and_budget_through()
    {
        var closed = ConvoyTestData.Arrived.AddDays(3);

        var report = ConvoyReports.Compose(Facts() with { ClosedAt = closed });

        report.ConvoyId.Should().Be(ConvoyTestData.Id);
        report.ArrivedAt.Should().Be(ConvoyTestData.Arrived);
        report.ClosedAt.Should().Be(closed);
    }
}
