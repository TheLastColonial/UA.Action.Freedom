using AwesomeAssertions;
using NSubstitute;
using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Tests.Unit.Donations;

/// <summary>
/// Recording, correcting and erasing a donor. Like a volunteer, a donor is a split identity, so the handler mints
/// the key and erasure is a repository fact; unlike a volunteer, erasure is never refused.
/// </summary>
public class DonorHandlerTests
{
    [Fact]
    public async Task Creating_a_donor_persists_them_and_returns_the_identifier_it_minted()
    {
        var repository = Substitute.For<IDonorRepository>();
        var handler = new CreateDonorHandler(repository);

        var id = await handler.HandleAsync(DonationTestData.ACreateDonorCommand(), TestContext.Current.CancellationToken);

        id.Should().NotBeEmpty();
        await repository.Received(1).AddAsync(
            Arg.Is<DonorReadModel>(donor => donor.Id == id && donor.Name == "Margaret Hollis"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, UpdateDonorOutcome.Updated)]
    [InlineData(false, UpdateDonorOutcome.NotFound)]
    public async Task Updating_a_donor_reports_whether_there_was_one(bool matched, UpdateDonorOutcome expected)
    {
        var repository = Substitute.For<IDonorRepository>();
        repository.UpdateAsync(Arg.Any<DonorReadModel>(), Arg.Any<CancellationToken>()).Returns(matched);
        var handler = new UpdateDonorHandler(repository);

        var outcome = await handler.HandleAsync(DonationTestData.AnUpdateDonorCommand(), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData(true, EraseDonorOutcome.Erased)]
    [InlineData(false, EraseDonorOutcome.NotFound)]
    public async Task Erasing_a_donor_reports_whether_there_was_one(bool erased, EraseDonorOutcome expected)
    {
        var repository = Substitute.For<IDonorRepository>();
        repository.EraseAsync(DonationTestData.DonorId, Arg.Any<CancellationToken>()).Returns(erased);
        var handler = new EraseDonorHandler(repository);

        var outcome = await handler.HandleAsync(new EraseDonorCommand(DonationTestData.DonorId), TestContext.Current.CancellationToken);

        outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Getting_a_donor_returns_nothing_when_there_is_no_such_donor()
    {
        var repository = Substitute.For<IDonorRepository>();
        var handler = new GetDonorByIdHandler(repository);

        var donor = await handler.HandleAsync(new GetDonorByIdQuery(DonationTestData.DonorId), TestContext.Current.CancellationToken);

        donor.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 0, 1, 50)]
    [InlineData(3, 500, 3, 50)]
    [InlineData(2, 25, 2, 25)]
    public async Task Listing_donors_clamps_the_page_and_page_size(int page, int pageSize, int expectedPage, int expectedSize)
    {
        var repository = Substitute.For<IDonorRepository>();
        var handler = new ListDonorsHandler(repository);

        await handler.HandleAsync(new ListDonorsQuery(page, pageSize), TestContext.Current.CancellationToken);

        await repository.Received(1).ListAsync(expectedPage, expectedSize, Arg.Any<CancellationToken>());
    }
}
