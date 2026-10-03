using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Tests.Unit.Donations;

/// <summary>
/// The donor status report: what they gave, what it was worth and how far it has got, at a high level. It is
/// produced for the donor by a user, and shows nothing about where the goods are going.
/// </summary>
public class DonorReportHandlerTests
{
    private static IDonationRepository DonationsWith(string? donorName, params DonorReportItem[] items)
    {
        var donations = Substitute.For<IDonationRepository>();
        donations.DonorNameAsync(DonationTestData.DonorId, Arg.Any<CancellationToken>()).Returns(donorName);
        donations.ReportItemsAsync(DonationTestData.DonorId, Arg.Any<CancellationToken>()).Returns(items);
        return donations;
    }

    private static Task<DonorReport?> ReportAsync(IDonationRepository donations) =>
        new GetDonorReportHandler(donations).HandleAsync(
            new GetDonorReportQuery(DonationTestData.DonorId), TestContext.Current.CancellationToken);

    [Fact]
    public async Task There_is_no_report_for_a_donor_who_was_never_on_file()
    {
        var report = await ReportAsync(DonationsWith(donorName: null));

        report.Should().BeNull();
    }

    [Fact]
    public async Task Totals_the_items_and_their_value_by_category()
    {
        var report = await ReportAsync(DonationsWith(
            "Margaret Hollis",
            DonationTestData.AReportItem("Tinned food", quantity: 12, valueGbp: 30m),
            DonationTestData.AReportItem("Tinned food", quantity: 6, valueGbp: 15m),
            DonationTestData.AReportItem("Blankets", quantity: 4, valueGbp: null)));

        report!.ItemCount.Should().Be(22);
        report.TotalValueGbp.Should().Be(45m);
        report.ByCategory.Should().BeEquivalentTo(
        [
            new DonorReportCategory("Tinned food", 18, 45m),
            new DonorReportCategory("Blankets", 4, 0m),
        ]);
    }

    [Fact]
    public async Task Says_how_far_each_item_has_got_without_naming_where_it_is_going()
    {
        var report = await ReportAsync(DonationsWith(
            "Margaret Hollis",
            DonationTestData.AReportItem(boxValidated: false),
            DonationTestData.AReportItem(boxValidated: true)));

        report!.Donations.Should().ContainSingle()
            .Which.Items.Select(item => item.Status)
            .Should().Equal(DonorItemStatus.BeingPacked, DonorItemStatus.PackedAndChecked);
    }

    [Fact]
    public async Task Groups_items_under_the_donation_they_came_in()
    {
        var report = await ReportAsync(DonationsWith(
            "Margaret Hollis",
            DonationTestData.AReportItem(donationId: 1),
            DonationTestData.AReportItem(donationId: 2),
            DonationTestData.AReportItem(donationId: 2)));

        report!.Donations.Select(donation => (donation.DonationId, donation.Items.Count))
            .Should().Equal((1, 1), (2, 2));
    }

    [Fact]
    public async Task An_erased_donor_still_has_a_report_under_Former_donor_with_the_same_totals()
    {
        var report = await ReportAsync(DonationsWith(
            DonorNames.FormerDonor,
            DonationTestData.AReportItem(quantity: 12, valueGbp: 30m)));

        report!.DonorName.Should().Be(DonorNames.FormerDonor);
        report.ItemCount.Should().Be(12);
        report.TotalValueGbp.Should().Be(30m);
    }

    [Fact]
    public void The_report_has_nowhere_to_put_a_receiver_region_route_or_address()
    {
        var names = typeof(DonorReport).GetProperties().Select(property => property.Name)
            .Concat(typeof(DonorReportDonation).GetProperties().Select(property => property.Name))
            .Concat(typeof(DonorReportLine).GetProperties().Select(property => property.Name))
            .Concat(typeof(DonorReportCategory).GetProperties().Select(property => property.Name))
            .Concat(typeof(DonorReportItem).GetProperties().Select(property => property.Name));

        names.Should().NotContain(name =>
            name.Contains("Receiver") || name.Contains("Region") || name.Contains("Route") || name.Contains("Address")
            || name.Contains("Location") || name.Contains("Convoy") || name.Contains("Manifest"));
    }
}
