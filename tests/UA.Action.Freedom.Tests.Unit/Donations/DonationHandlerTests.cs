using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Tests.Unit.Donations;

/// <summary>Recording a drop-off against a donor, and reading what a donor gave.</summary>
public class DonationHandlerTests
{
    [Fact]
    public async Task Recording_a_donation_returns_the_identifier_the_database_assigned()
    {
        var donations = Substitute.For<IDonationRepository>();
        donations.AddAsync(DonationTestData.DonorId, DonationTestData.ReceivedOn, "Tins", Arg.Any<CancellationToken>())
            .Returns(DonationTestData.DonationId);
        var handler = new CreateDonationHandler(donations, Substitute.For<IDonorRepository>().Knowing(DonationTestData.ADonor()));

        var result = await handler.HandleAsync(
            new CreateDonationCommand(DonationTestData.DonorId, DonationTestData.ReceivedOn, "Tins"),
            TestContext.Current.CancellationToken);

        result.Should().Be(new CreateDonationResult(CreateDonationOutcome.Created, DonationTestData.DonationId));
    }

    [Fact]
    public async Task A_donation_cannot_be_recorded_against_a_donor_who_is_not_on_file()
    {
        var donations = Substitute.For<IDonationRepository>();
        var handler = new CreateDonationHandler(donations, Substitute.For<IDonorRepository>());

        var result = await handler.HandleAsync(
            new CreateDonationCommand(DonationTestData.DonorId, DonationTestData.ReceivedOn, null),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(CreateDonationOutcome.DonorNotFound);
        await donations.DidNotReceive().AddAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, UpdateDonationOutcome.Updated)]
    [InlineData(false, UpdateDonationOutcome.NotFound)]
    public async Task Updating_a_donation_reports_whether_there_was_one(bool matched, UpdateDonationOutcome expected)
    {
        var donations = Substitute.For<IDonationRepository>();
        donations.UpdateAsync(DonationTestData.DonationId, DonationTestData.ReceivedOn, null, Arg.Any<CancellationToken>())
            .Returns(matched);
        var handler = new UpdateDonationHandler(donations);

        var outcome = await handler.HandleAsync(
            new UpdateDonationCommand(DonationTestData.DonationId, DonationTestData.ReceivedOn, null),
            TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(DeleteDonationResult.Deleted, DeleteDonationOutcome.Deleted)]
    [InlineData(DeleteDonationResult.NotFound, DeleteDonationOutcome.NotFound)]
    [InlineData(DeleteDonationResult.StillReferenced, DeleteDonationOutcome.StillReferenced)]
    public async Task Deleting_a_donation_reports_what_the_delete_found(DeleteDonationResult result, DeleteDonationOutcome expected)
    {
        var donations = Substitute.For<IDonationRepository>();
        donations.DeleteAsync(DonationTestData.DonationId, Arg.Any<CancellationToken>()).Returns(result);
        var handler = new DeleteDonationHandler(donations);

        var outcome = await handler.HandleAsync(
            new DeleteDonationCommand(DonationTestData.DonationId), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task A_donors_donations_are_listed_even_after_the_donor_is_erased()
    {
        var donations = Substitute.For<IDonationRepository>();
        donations.DonorNameAsync(DonationTestData.DonorId, Arg.Any<CancellationToken>()).Returns(DonorNames.FormerDonor);
        donations.ListAsync(DonationTestData.DonorId, 1, 50, Arg.Any<CancellationToken>())
            .Returns([DonationTestData.ADonation(DonorNames.FormerDonor)]);
        var handler = new ListDonorDonationsHandler(donations);

        var listed = await handler.HandleAsync(
            new ListDonorDonationsQuery(DonationTestData.DonorId, 1, 50), TestContext.Current.CancellationToken);

        listed.Should().ContainSingle().Which.DonorName.Should().Be(DonorNames.FormerDonor);
    }

    [Fact]
    public async Task There_are_no_donations_to_list_for_a_donor_who_was_never_on_file()
    {
        var donations = Substitute.For<IDonationRepository>();
        donations.DonorNameAsync(DonationTestData.DonorId, Arg.Any<CancellationToken>()).Returns((string?)null);
        var handler = new ListDonorDonationsHandler(donations);

        var listed = await handler.HandleAsync(
            new ListDonorDonationsQuery(DonationTestData.DonorId, 1, 50), TestContext.Current.CancellationToken);

        listed.Should().BeNull();
    }

    [Fact]
    public async Task Listing_donations_for_a_donor_passes_the_donor_and_clamps_the_page_size()
    {
        var donations = Substitute.For<IDonationRepository>();
        var handler = new ListDonationsHandler(donations);

        await handler.HandleAsync(new ListDonationsQuery(DonationTestData.DonorId, 1, 9999), TestContext.Current.CancellationToken);

        await donations.Received(1).ListAsync(DonationTestData.DonorId, 1, 50, Arg.Any<CancellationToken>());
    }
}

internal static class DonorRepositoryDoubles
{
    /// <summary>A donor repository that knows <paramref name="donor"/> and nobody else.</summary>
    internal static IDonorRepository Knowing(this IDonorRepository repository, DonorReadModel donor)
    {
        repository.GetByIdAsync(donor.Id, Arg.Any<CancellationToken>()).Returns(donor);
        return repository;
    }
}
