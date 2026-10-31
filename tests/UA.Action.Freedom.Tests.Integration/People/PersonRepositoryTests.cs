using AwesomeAssertions;
using UA.Action.Freedom.Application.People;
using UA.Action.Freedom.Data.People;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.People;

/// <summary>
/// The Dapper <see cref="PersonRepository"/> against a real <c>dbo.Person</c>. Needs the local
/// stack up (<c>iac/local</c> + <c>tofu apply</c>) or a <c>ConnectionStrings__Freedom</c>
/// pointing at an equivalent database; skips itself otherwise.
/// </summary>
[Trait("Category", "Integration")]
public class PersonRepositoryTests
{
    private static async Task<PersonRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync("SELECT COUNT(1) FROM dbo.Person", cancellationToken);
        return new PersonRepository(ConnectionFactory(), Unattributed);
    }

    /// <summary>
    /// A surname nothing else in the database will share, so a paged list can find this row
    /// without asserting on a total count.
    /// </summary>
    private static string NewSurname() => "IT" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    private static PersonReadModel APerson(Guid id, string surname, bool isDriver = false, bool committed = false) => new(
        Id: id,
        FirstName: "Integration",
        LastName: surname,
        DateOfBirth: new DateTime(1988, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        Joined: new DateTime(2024, 2, 24, 0, 0, 0, DateTimeKind.Utc),
        Phone: "+447700900123",
        IsDriver: isDriver,
        Committed: committed);

    private static Task RemoveAsync(Guid id) =>
        ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task Round_trips_every_field_through_the_database()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            await repository.AddAsync(APerson(id, surname, isDriver: true, committed: true), cancellationToken);

            var stored = await repository.GetByIdAsync(id, cancellationToken);

            // Record equality: proves every column round-trips as the CLR type the read model's
            // constructor expects, which is what catches a bit/int or datetime2 mismatch.
            stored.Should().Be(APerson(id, surname, isDriver: true, committed: true) with { LastChangedAt = stored!.LastChangedAt });
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Reports_whether_an_update_matched_a_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            await repository.AddAsync(APerson(id, surname), cancellationToken);

            var changed = await repository.UpdateAsync(
                APerson(id, surname) with { IsDriver = true, Committed = true, Phone = null },
                cancellationToken);

            changed.Should().BeTrue();

            var stored = await repository.GetByIdAsync(id, cancellationToken);
            stored!.IsDriver.Should().BeTrue();
            stored.Committed.Should().BeTrue();
            stored.Phone.Should().BeNull();

            var missed = await repository.UpdateAsync(APerson(Guid.NewGuid(), surname), cancellationToken);
            missed.Should().BeFalse();
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Exists_and_delete_follow_the_row_through_its_life()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            (await repository.ExistsAsync(id, cancellationToken)).Should().BeFalse();

            await repository.AddAsync(APerson(id, surname), cancellationToken);
            (await repository.ExistsAsync(id, cancellationToken)).Should().BeTrue();

            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.Deleted);
            (await repository.ExistsAsync(id, cancellationToken)).Should().BeFalse();

            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.NotFound);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    /// <summary>A convoy with one vehicle on its truck list and <paramref name="personId"/> in its crew.</summary>
    private static Task<int> ACrewedConvoyAsync(string vin, Guid personId, bool arrived, bool delivered = true) => ScalarAsync(
        """
        INSERT INTO dbo.Convoy (Start, ExpectedEnd, TruckListPublishedAt, ArrivedAt)
        VALUES ('2026-09-01', '2026-09-05', '2026-08-20', CASE WHEN @arrived = 1 THEN '2026-09-05' END);
        DECLARE @convoyId int = CAST(SCOPE_IDENTITY() AS int);
        INSERT INTO dbo.Vehicle (Vin, Plate, [Year], WeightKg, InspectionStatus) VALUES (@vin, 'IT12ABC', 2015, 1800, 2);
        INSERT INTO dbo.ConvoyVehicle (ConvoyId, Vin, DeliveredAt) VALUES (@convoyId, @vin, CASE WHEN @delivered = 1 THEN '2026-09-05' END);
        INSERT INTO dbo.ConvoyVehicleCrew (ConvoyId, Vin, PersonId) VALUES (@convoyId, @vin, @id);
        SELECT @convoyId;
        """,
        ("@vin", vin),
        ("@id", personId),
        ("@arrived", arrived),
        ("@delivered", delivered));

    /// <summary>Deleting the vehicle cascades its truck-list row, and that cascades the crew.</summary>
    private static Task RemoveConvoyAsync(int convoyId, string vin) => ExecuteAsync(
        """
        DELETE FROM dbo.BoxOutcome WHERE ConvoyId = @convoyId;
        DELETE FROM dbo.ConvoyVehicleBoxAllocation WHERE ConvoyId = @convoyId;
        DELETE FROM dbo.Vehicle WHERE Vin = @vin;
        DELETE FROM dbo.ConvoyVehicle WHERE ConvoyId = @convoyId;
        DELETE FROM dbo.Convoy WHERE Id = @convoyId;
        """,
        ("@vin", vin),
        ("@convoyId", convoyId));

    private static Task<int> PersonalDataRowsAsync(Guid id) =>
        ScalarAsync("SELECT COUNT(1) FROM dbo.PersonDetail WHERE PersonId = @id", ("@id", id));

    private static Task<int> IdentityRowsAsync(Guid id) =>
        ScalarAsync("SELECT COUNT(1) FROM dbo.Person WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task Erasing_a_volunteer_nothing_names_removes_them_entirely()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();

        try
        {
            await repository.AddAsync(APerson(id, NewSurname()), cancellationToken);

            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.Deleted);

            (await IdentityRowsAsync(id)).Should().Be(0);
            (await PersonalDataRowsAsync(id)).Should().Be(0);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Erasing_a_former_crew_member_deletes_their_personal_data_and_keeps_the_record()
    {
        // UK data protection: the person is erased. The crew of a past convoy still has a seat
        // filled, by an identity nothing can link back to them.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        await repository.AddAsync(APerson(id, NewSurname(), isDriver: true), cancellationToken);
        var convoyId = await ACrewedConvoyAsync(vin, id, arrived: true);

        try
        {
            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.Deleted);

            (await PersonalDataRowsAsync(id)).Should().Be(0);
            (await IdentityRowsAsync(id)).Should().Be(1);
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.ConvoyVehicleCrew WHERE PersonId = @id", ("@id", id))).Should().Be(1);
            (await repository.GetByIdAsync(id, cancellationToken)).Should().BeNull();
            (await repository.ExistsAsync(id, cancellationToken)).Should().BeFalse();
            (await repository.ListAsync(1, 200, driversOnly: false, cancellationToken)).Should().NotContain(person => person.Id == id);
            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.NotFound);
        }
        finally
        {
            await RemoveConvoyAsync(convoyId, vin);
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task A_volunteer_on_the_crew_of_a_convoy_still_under_way_is_not_erased()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        await repository.AddAsync(APerson(id, NewSurname(), isDriver: true), cancellationToken);
        var convoyId = await ACrewedConvoyAsync(vin, id, arrived: false);

        try
        {
            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.StillActive);

            (await PersonalDataRowsAsync(id)).Should().Be(1);
            (await repository.ExistsAsync(id, cancellationToken)).Should().BeTrue();
        }
        finally
        {
            await RemoveConvoyAsync(convoyId, vin);
            await RemoveAsync(id);
        }
    }

    [Theory]
    [InlineData(false, false, DeletePersonResult.StillActive)]
    [InlineData(true, false, DeletePersonResult.Deleted)]
    [InlineData(false, true, DeletePersonResult.Deleted)]
    public async Task A_volunteer_crewing_an_unfinished_vehicle_is_erased_only_once_it_is_delivered_or_withdrawn(
        bool delivered, bool withdrawn, DeletePersonResult expected)
    {
        // The convoy has arrived, so the first half of the erasure check is satisfied; what is left
        // is whether the vehicle they crewed finished: delivered, or withdrawn from the convoy.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        await repository.AddAsync(APerson(id, NewSurname(), isDriver: true), cancellationToken);
        var convoyId = await ACrewedConvoyAsync(vin, id, arrived: true, delivered: delivered);
        if (withdrawn)
        {
            await ExecuteAsync(
                "UPDATE dbo.ConvoyVehicle SET WithdrawnAt = SYSUTCDATETIME(), WithdrawnReason = 'Broke down' WHERE ConvoyId = @convoyId",
                ("@convoyId", convoyId));
        }

        try
        {
            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(expected);
        }
        finally
        {
            await RemoveConvoyAsync(convoyId, vin);
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task A_volunteer_crewing_a_delivered_vehicle_with_a_box_still_without_an_outcome_is_not_erased()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        await repository.AddAsync(APerson(id, NewSurname(), isDriver: true), cancellationToken);
        var convoyId = await ACrewedConvoyAsync(vin, id, arrived: true, delivered: true);
        var boxId = await ScalarAsync(
            """
            INSERT INTO dbo.Box DEFAULT VALUES;
            DECLARE @boxId int = CAST(SCOPE_IDENTITY() AS int);
            INSERT INTO dbo.ConvoyVehicleBoxAllocation (BoxId, ConvoyId, Vin) VALUES (@boxId, @convoyId, @vin);
            SELECT @boxId;
            """,
            ("@convoyId", convoyId),
            ("@vin", vin));

        try
        {
            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.StillActive);

            await ExecuteAsync(
                "INSERT INTO dbo.BoxOutcome (BoxId, ConvoyId, Vin, Outcome) VALUES (@boxId, @convoyId, @vin, @delivered)",
                ("@boxId", boxId),
                ("@convoyId", convoyId),
                ("@vin", vin),
                ("@delivered", (int)BoxOutcome.Delivered));

            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.Deleted);
        }
        finally
        {
            await RemoveConvoyAsync(convoyId, vin);
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @boxId", ("@boxId", boxId));
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Lists_the_volunteer_among_the_others()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            await repository.AddAsync(APerson(id, surname), cancellationToken);

            var page = await repository.ListAsync(1, 200, driversOnly: false, cancellationToken);

            page.Should().ContainSingle(person => person.Id == id);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Leaves_non_drivers_out_of_the_drivers_only_page()
    {
        // The dispatcher's shortlist. A non-driver appearing here is someone being asked to
        // drive a convoy they never volunteered for.
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var driverId = Guid.NewGuid();
        var packerId = Guid.NewGuid();
        var surname = NewSurname();

        try
        {
            await repository.AddAsync(APerson(driverId, surname, isDriver: true), cancellationToken);
            await repository.AddAsync(APerson(packerId, surname, isDriver: false), cancellationToken);

            var drivers = await repository.ListAsync(1, 200, driversOnly: true, cancellationToken);

            drivers.Should().Contain(person => person.Id == driverId);
            drivers.Should().NotContain(person => person.Id == packerId);
        }
        finally
        {
            await RemoveAsync(driverId);
            await RemoveAsync(packerId);
        }
    }
    private static string NewSubject() => "it-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task A_linked_login_is_found_as_its_person()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var subject = NewSubject();

        try
        {
            await repository.AddAsync(APerson(id, NewSurname()), cancellationToken);

            (await repository.FindBySubjectAsync(subject, cancellationToken)).Should().BeNull();
            (await repository.LinkLoginAsync(id, subject, cancellationToken)).Should().Be(LinkLoginResult.Linked);
            (await repository.FindBySubjectAsync(subject, cancellationToken)).Should().Be(id);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task A_login_cannot_be_linked_to_two_people_and_an_unknown_person_cannot_be_linked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var subject = NewSubject();

        try
        {
            await repository.AddAsync(APerson(first, NewSurname()), cancellationToken);
            await repository.AddAsync(APerson(second, NewSurname()), cancellationToken);
            await repository.LinkLoginAsync(first, subject, cancellationToken);

            (await repository.LinkLoginAsync(second, subject, cancellationToken)).Should().Be(LinkLoginResult.SubjectInUse);
            (await repository.FindBySubjectAsync(subject, cancellationToken)).Should().Be(first);
            (await repository.LinkLoginAsync(Guid.NewGuid(), NewSubject(), cancellationToken)).Should().Be(LinkLoginResult.NotFound);
        }
        finally
        {
            await RemoveAsync(first);
            await RemoveAsync(second);
        }
    }

    [Fact]
    public async Task Linking_the_same_login_to_the_same_person_again_is_not_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var subject = NewSubject();

        try
        {
            await repository.AddAsync(APerson(id, NewSurname()), cancellationToken);
            await repository.LinkLoginAsync(id, subject, cancellationToken);

            (await repository.LinkLoginAsync(id, subject, cancellationToken)).Should().Be(LinkLoginResult.Linked);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Erasing_a_volunteer_removes_the_link_with_their_personal_data()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = Guid.NewGuid();
        var subject = NewSubject();
        var vin = "IT" + Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
        await repository.AddAsync(APerson(id, NewSurname(), isDriver: true), cancellationToken);
        await repository.LinkLoginAsync(id, subject, cancellationToken);
        var convoyId = await ACrewedConvoyAsync(vin, id, arrived: true);

        try
        {
            (await repository.DeleteAsync(id, cancellationToken)).Should().Be(DeletePersonResult.Deleted);

            (await IdentityRowsAsync(id)).Should().Be(1);
            (await repository.FindBySubjectAsync(subject, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveConvoyAsync(convoyId, vin);
            await RemoveAsync(id);
        }
    }
}
