using AwesomeAssertions;
using UA.Action.Freedom.Application.Boxes;
using UA.Action.Freedom.Application.Convoys;
using UA.Action.Freedom.Data.Boxes;
using UA.Action.Freedom.Data.Convoys;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.Convoys.ConvoyFixtures;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Boxes;

/// <summary>
/// Replacing an attested box (ADR 0011) against the real database: one transaction that voids the old box, revokes
/// its label, copies its items, and moves its cargo. Skips itself when the local stack is not up.
/// </summary>
[Trait("Category", "Integration")]
public class BoxReplacementRepositoryTests
{
    private static async Task<(BoxRepository Boxes, ConvoyRepository Convoys, ConvoyVehicleRepository TruckList)>
        ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(Probe + "; SELECT COUNT(1) FROM dbo.BoxQrCode;", cancellationToken);
        await EnsureRecorderAsync();
        return (
            new BoxRepository(ConnectionFactory(), Unattributed),
            new ConvoyRepository(ConnectionFactory(), Unattributed),
            new ConvoyVehicleRepository(ConnectionFactory(), Unattributed));
    }

    private static Task<int> NewBoxAsync(BoxRepository boxes, CancellationToken cancellationToken) =>
        boxes.AddAsync(new BoxReadModel(0, 0, null, null, null, null, null, null, null), cancellationToken);

    private static BoxItemReadModel AnItem(int categoryId) => new(
        Guid.NewGuid(), "Blankets", new Dictionary<string, string> { ["size"] = "double" }, categoryId,
        Quantity: 4, ValueGbp: 12.5m, ValueSource: ValueSource.Donor, ExpiresOn: new DateOnly(2030, 1, 1));

    private static async Task CleanUpAsync(IEnumerable<int> boxIds, Guid volunteer)
    {
        foreach (var boxId in boxIds.OrderByDescending(id => id))
        {
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", boxId));
        }

        await ExecuteAsync("DELETE FROM dbo.Person WHERE Id = @id", ("@id", volunteer));
    }

    [Fact]
    public async Task Replacing_voids_the_old_box_and_creates_an_unattested_one_that_names_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (boxes, _, _) = await ConnectOrSkipAsync(cancellationToken);
        var volunteer = await AddVolunteerAsync();
        var categoryId = await AddCategoryAsync();
        var oldId = await NewBoxAsync(boxes, cancellationToken);
        int? newId = null;

        try
        {
            await boxes.AddItemAsync(oldId, AnItem(categoryId), cancellationToken);
            await boxes.ValidateAsync(oldId, volunteer, 24, null, null, null, DateTime.UtcNow, cancellationToken);

            newId = await boxes.ReplaceAsync(oldId, cancellationToken);

            newId.Should().NotBeNull();
            var old = await boxes.GetByIdAsync(oldId, cancellationToken);
            old!.Voided.Should().BeTrue();
            old.Validated.Should().BeTrue();
            old.ReplacedByBoxId.Should().Be(newId);
            var replacement = await boxes.GetByIdAsync(newId!.Value, cancellationToken);
            replacement!.Validated.Should().BeFalse();
            replacement.WeightKg.Should().Be(0);
            replacement.ReplacesBoxId.Should().Be(oldId);
            var copied = (await boxes.ListItemsAsync(newId.Value, cancellationToken)).Should().ContainSingle().Subject;
            copied.CategoryId.Should().Be(categoryId);
            copied.Quantity.Should().Be(4);
            copied.ValueGbp.Should().Be(12.5m);
            copied.ExpiresOn.Should().Be(new DateOnly(2030, 1, 1));
        }
        finally
        {
            await CleanUpAsync(newId is { } created ? [oldId, created] : [oldId], volunteer);
            await RemoveCategoryAsync(categoryId);
        }
    }

    [Fact]
    public async Task The_old_token_no_longer_resolves()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (boxes, _, _) = await ConnectOrSkipAsync(cancellationToken);
        var volunteer = await AddVolunteerAsync();
        var oldId = await NewBoxAsync(boxes, cancellationToken);
        int? newId = null;

        try
        {
            var token = Guid.NewGuid();
            await boxes.IssueQrCodeAsync(oldId, token, DateTime.UtcNow, cancellationToken);
            await boxes.ValidateAsync(oldId, volunteer, 24, null, null, null, DateTime.UtcNow, cancellationToken);

            newId = await boxes.ReplaceAsync(oldId, cancellationToken);

            (await boxes.ResolveActiveQrCodeAsync(token, cancellationToken)).Should().BeNull();
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.BoxQrCode WHERE BoxId = @id", ("@id", oldId)))
                .Should().Be(1, "revoked rows are kept");
        }
        finally
        {
            await CleanUpAsync(newId is { } created ? [oldId, created] : [oldId], volunteer);
        }
    }

    [Fact]
    public async Task A_box_nobody_has_attested_is_not_replaced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (boxes, _, _) = await ConnectOrSkipAsync(cancellationToken);
        var boxId = await NewBoxAsync(boxes, cancellationToken);

        try
        {
            (await boxes.ReplaceAsync(boxId, cancellationToken)).Should().BeNull();
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.Box WHERE ReplacesBoxId = @id", ("@id", boxId))).Should().Be(0);
        }
        finally
        {
            await ExecuteAsync("DELETE FROM dbo.Box WHERE Id = @id", ("@id", boxId));
        }
    }

    [Fact]
    public async Task A_box_is_replaced_only_once_even_when_asked_twice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (boxes, _, _) = await ConnectOrSkipAsync(cancellationToken);
        var volunteer = await AddVolunteerAsync();
        var oldId = await NewBoxAsync(boxes, cancellationToken);
        int? newId = null;

        try
        {
            await boxes.ValidateAsync(oldId, volunteer, 24, null, null, null, DateTime.UtcNow, cancellationToken);
            newId = await boxes.ReplaceAsync(oldId, cancellationToken);

            (await boxes.ReplaceAsync(oldId, cancellationToken)).Should().BeNull();
            (await ScalarAsync("SELECT COUNT(1) FROM dbo.Box WHERE ReplacesBoxId = @id", ("@id", oldId))).Should().Be(1);
        }
        finally
        {
            await CleanUpAsync(newId is { } created ? [oldId, created] : [oldId], volunteer);
        }
    }

    [Fact]
    public async Task The_cargo_allocation_moves_to_the_replacement_and_the_voided_box_carries_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (boxes, convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var volunteer = await AddVolunteerAsync();
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var oldId = await NewBoxAsync(boxes, cancellationToken);
        int? newId = null;

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);
            await truckList.AllocateBoxAsync(convoyId, vin, oldId, cancellationToken);
            await boxes.ValidateAsync(oldId, volunteer, 24, null, null, null, DateTime.UtcNow, cancellationToken);

            newId = await boxes.ReplaceAsync(oldId, cancellationToken);

            (await truckList.GetBoxAllocationAsync(oldId, cancellationToken)).Should().BeNull();
            (await truckList.GetBoxAllocationAsync(newId!.Value, cancellationToken))!.Vin.Should().Be(vin);
            (await truckList.ListBoxesAsync(convoyId, vin, cancellationToken))!.Select(b => b.BoxId)
                .Should().Equal(newId.Value);
        }
        finally
        {
            await CleanUpAsync(newId is { } created ? [oldId, created] : [oldId], volunteer);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }

    [Fact]
    public async Task A_voided_box_cannot_be_put_on_a_vehicle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (boxes, convoys, truckList) = await ConnectOrSkipAsync(cancellationToken);
        var volunteer = await AddVolunteerAsync();
        var convoyId = await convoys.AddAsync(Start, ExpectedEnd, cancellationToken);
        var vin = NewVin();
        var oldId = await NewBoxAsync(boxes, cancellationToken);
        int? newId = null;

        try
        {
            await AddVehicleAsync(vin);
            await truckList.AddAsync(convoyId, vin, cancellationToken);
            await boxes.ValidateAsync(oldId, volunteer, 24, null, null, null, DateTime.UtcNow, cancellationToken);
            newId = await boxes.ReplaceAsync(oldId, cancellationToken);

            (await truckList.AllocateBoxAsync(convoyId, vin, oldId, cancellationToken))
                .Should().Be(AllocateBoxResult.BoxVoided);
        }
        finally
        {
            await CleanUpAsync(newId is { } created ? [oldId, created] : [oldId], volunteer);
            await RemoveVehicleAsync(vin);
            await RemoveConvoyAsync(convoyId);
        }
    }
}
