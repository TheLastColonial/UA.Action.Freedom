using AwesomeAssertions;
using UA.Action.Freedom.Application.Donations;
using UA.Action.Freedom.Data.Donations;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Donations;

/// <summary>
/// The Dapper <see cref="DonorRepository"/> and <see cref="DonationRepository"/> against the real split donor
/// identity. Skips itself when the local stack is not up.
/// </summary>
[Trait("Category", "Integration")]
public class DonorRepositoryTests
{
    private static async Task<(DonorRepository Donors, DonationRepository Donations)> ConnectOrSkipAsync(
        CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(
            "SELECT COUNT(1) FROM dbo.Donor; SELECT COUNT(1) FROM dbo.DonorDetail; SELECT COUNT(1) FROM dbo.Donation;",
            cancellationToken);
        return (new DonorRepository(ConnectionFactory(), Unattributed), new DonationRepository(ConnectionFactory(), Unattributed));
    }

    private static DonorReadModel ADonor(Guid id, string name) =>
        new(id, name, "donor@example.org", "+447700900456");

    private static string NewName() => "IT Donor " + Guid.NewGuid().ToString("N")[..12];

    private static Task RemoveDonorAsync(Guid id) =>
        ExecuteAsync("DELETE FROM dbo.Donation WHERE DonorId = @id; DELETE FROM dbo.Donor WHERE Id = @id", ("@id", id));

    private static Task<int> AddBoxAsync() =>
        ScalarAsync("INSERT INTO dbo.Box (WeightKg) VALUES (0); SELECT CAST(SCOPE_IDENTITY() AS int);");

    [Fact]
    public async Task Adding_a_donor_writes_the_identity_and_the_detail_and_reads_back_every_field()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var name = NewName();

        try
        {
            await donors.AddAsync(ADonor(id, name), cancellationToken);

            var stored = await donors.GetByIdAsync(id, cancellationToken);

            stored.Should().Be(ADonor(id, name) with { LastChangedAt = stored!.LastChangedAt });
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.Donor WHERE Id = @id", ("@id", id))).Should().Be(1);
        }
        finally
        {
            await RemoveDonorAsync(id);
        }
    }

    [Fact]
    public async Task Updating_a_donor_reports_whether_a_row_matched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var name = NewName();

        try
        {
            await donors.AddAsync(ADonor(id, name), cancellationToken);

            (await donors.UpdateAsync(ADonor(id, name) with { Email = null }, cancellationToken)).Should().BeTrue();
            (await donors.GetByIdAsync(id, cancellationToken))!.Email.Should().BeNull();
            (await donors.UpdateAsync(ADonor(Guid.NewGuid(), name), cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveDonorAsync(id);
        }
    }

    [Fact]
    public async Task Erasing_a_donor_nobody_gave_through_deletes_the_identity_as_well()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, _) = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        await donors.AddAsync(ADonor(id, NewName()), cancellationToken);

        var erased = await donors.EraseAsync(id, cancellationToken);

        erased.Should().BeTrue();
        (await donors.GetByIdAsync(id, cancellationToken)).Should().BeNull();
        (await ScalarAsync("SELECT COUNT(1) FROM dbo.Donor WHERE Id = @id", ("@id", id))).Should().Be(0);
    }

    [Fact]
    public async Task Erasing_a_donor_who_has_given_deletes_the_details_and_keeps_the_donation_under_Former_donor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, donations) = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        await donors.AddAsync(ADonor(id, NewName()), cancellationToken);
        var donationId = await donations.AddAsync(id, new DateOnly(2026, 9, 20), "Two boxes of tins", cancellationToken);

        try
        {
            var erased = await donors.EraseAsync(id, cancellationToken);

            erased.Should().BeTrue();
            (await donors.GetByIdAsync(id, cancellationToken)).Should().BeNull();
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.DonorDetail WHERE DonorId = @id", ("@id", id))).Should().Be(0);
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.Donor WHERE Id = @id AND ErasedAt IS NOT NULL", ("@id", id))).Should().Be(1);

            var donation = await donations.GetByIdAsync(donationId, cancellationToken);
            donation!.DonorName.Should().Be(DonorNames.FormerDonor);
            donation.Notes.Should().Be("Two boxes of tins");
        }
        finally
        {
            await RemoveDonorAsync(id);
        }
    }

    [Fact]
    public async Task Erasing_a_donor_who_does_not_exist_reports_nothing_to_erase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, _) = await ConnectOrSkipAsync(cancellationToken);

        (await donors.EraseAsync(Guid.NewGuid(), cancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task Erasing_a_donor_does_not_touch_a_volunteer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, _) = await ConnectOrSkipAsync(cancellationToken);
        var volunteerId = await AddVolunteerAsync();
        var donorId = Guid.NewGuid();
        await donors.AddAsync(ADonor(donorId, NewName()), cancellationToken);

        try
        {
            await donors.EraseAsync(donorId, cancellationToken);

            (await ScalarAsync("SELECT COUNT(1) FROM dbo.PersonDetail WHERE PersonId = @id", ("@id", volunteerId))).Should().Be(1);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", volunteerId));
            await RemoveDonorAsync(donorId);
        }
    }

    [Fact]
    public async Task Lists_a_donors_donations_newest_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, donations) = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        await donors.AddAsync(ADonor(id, NewName()), cancellationToken);

        try
        {
            await donations.AddAsync(id, new DateOnly(2026, 8, 1), null, cancellationToken);
            await donations.AddAsync(id, new DateOnly(2026, 9, 1), null, cancellationToken);

            var listed = await donations.ListAsync(id, 1, 50, cancellationToken);

            listed.Select(donation => donation.ReceivedOn)
                .Should().Equal(new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1));
        }
        finally
        {
            await RemoveDonorAsync(id);
        }
    }

    [Fact]
    public async Task A_donation_that_items_still_name_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, donations) = await ConnectOrSkipAsync(cancellationToken);
        var donorId = Guid.NewGuid();
        await donors.AddAsync(ADonor(donorId, NewName()), cancellationToken);
        var donationId = await donations.AddAsync(donorId, new DateOnly(2026, 9, 1), null, cancellationToken);
        var categoryId = await AddCategoryAsync();
        var boxId = await AddBoxAsync();
        await ExecuteAsync(
            "INSERT INTO dbo.BoxItem (Id, BoxId, CategoryId, Description, DonationId) VALUES (NEWID(), @box, @category, 'Tins', @donation)",
            ("@box", boxId), ("@category", categoryId), ("@donation", donationId));

        try
        {
            (await donations.DeleteAsync(donationId, cancellationToken)).Should().Be(DeleteDonationResult.StillReferenced);
            (await donations.DeleteAsync(int.MaxValue, cancellationToken)).Should().Be(DeleteDonationResult.NotFound);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", boxId));
            await RemoveDonorAsync(donorId);
            await RemoveCategoryAsync(categoryId);
        }
    }

    [Fact]
    public async Task The_report_lists_what_a_donor_gave_and_whether_its_box_is_validated_and_nothing_else()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, donations) = await ConnectOrSkipAsync(cancellationToken);
        var donorId = Guid.NewGuid();
        await donors.AddAsync(ADonor(donorId, NewName()), cancellationToken);
        var donationId = await donations.AddAsync(donorId, new DateOnly(2026, 9, 1), null, cancellationToken);
        var categoryId = await AddCategoryAsync();
        var boxId = await AddBoxAsync();
        await ExecuteAsync(
            """
            INSERT INTO dbo.BoxItem (Id, BoxId, CategoryId, Description, DonationId, Quantity, ValueGbp, ValueSource)
            VALUES (NEWID(), @box, @category, 'Tins', @donation, 12, 30.00, 0)
            """,
            ("@box", boxId), ("@category", categoryId), ("@donation", donationId));

        try
        {
            var report = await donations.ReportItemsAsync(donorId, cancellationToken);

            report.Should().ContainSingle().Which.Should().Match<DonorReportItem>(item =>
                item.DonationId == donationId && item.Quantity == 12 && item.ValueGbp == 30.00m && !item.BoxValidated);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", boxId));
            await RemoveDonorAsync(donorId);
            await RemoveCategoryAsync(categoryId);
        }
    }

    [Fact]
    public async Task The_report_counts_a_replaced_boxs_items_once_because_the_voided_box_is_left_out()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (donors, donations) = await ConnectOrSkipAsync(cancellationToken);
        var donorId = Guid.NewGuid();
        await donors.AddAsync(ADonor(donorId, NewName()), cancellationToken);
        var donationId = await donations.AddAsync(donorId, new DateOnly(2026, 9, 1), null, cancellationToken);
        var categoryId = await AddCategoryAsync();
        var volunteer = await AddVolunteerAsync();
        var voidedBoxId = await AddBoxAsync();
        var replacementId = await AddBoxAsync();
        await ExecuteAsync(
            """
            UPDATE dbo.Box SET ValidatedByPersonId = @volunteer, ValidatedAt = SYSUTCDATETIME(), VoidedAt = SYSUTCDATETIME()
            WHERE Id = @voided;
            UPDATE dbo.Box SET ReplacesBoxId = @voided WHERE Id = @replacement;
            INSERT INTO dbo.BoxItem (Id, BoxId, CategoryId, Description, DonationId, Quantity, ValueGbp, ValueSource)
            VALUES (NEWID(), @voided, @category, 'Tins', @donation, 12, 30.00, 0),
                   (NEWID(), @replacement, @category, 'Tins', @donation, 12, 30.00, 0)
            """,
            ("@volunteer", volunteer), ("@voided", voidedBoxId), ("@replacement", replacementId),
            ("@category", categoryId), ("@donation", donationId));

        try
        {
            var report = await donations.ReportItemsAsync(donorId, cancellationToken);

            report.Should().ContainSingle().Which.Quantity.Should().Be(12);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", replacementId));
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", voidedBoxId));
            await ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", volunteer));
            await RemoveDonorAsync(donorId);
            await RemoveCategoryAsync(categoryId);
        }
    }
}
