using AwesomeAssertions;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Data.Categories;
using UA.Action.Freedom.Domain;
using static UA.Action.Freedom.Tests.Integration.SqlTestDatabase;

namespace UA.Action.Freedom.Tests.Integration.Categories;

/// <summary>
/// The Dapper <see cref="ItemCategoryRepository"/> against the real <c>dbo.ItemCategory</c> and
/// <c>dbo.CategoryCustomsCode</c>: a name is unique, a code is one row per authority and cleared by null, and
/// nothing an update can write turns a category fixed.
/// </summary>
[Trait("Category", "Integration")]
public class ItemCategoryRepositoryTests
{
    private static async Task<ItemCategoryRepository> ConnectOrSkipAsync(CancellationToken cancellationToken)
    {
        await SkipUnlessReachableAsync(
            "SELECT TOP 1 Id FROM dbo.ItemCategory; SELECT TOP 1 Code FROM dbo.CategoryCustomsCode", cancellationToken);
        return new ItemCategoryRepository(ConnectionFactory(), Unattributed);
    }

    private static string AUniqueName() => $"Integration {Guid.NewGuid():N}";

    private static ItemCategoryReadModel ANewCategory(string name, int? warnWithinDays = null) => new(
        Id: 0, name, NameUk: "", IsFixed: false, HazardClass: null, IsSensitive: false, IsNotCarried: false,
        warnWithinDays);

    private static Task RemoveAsync(int id) =>
        ExecuteAsync("DELETE FROM dbo.ItemCategory WHERE Id = @id", ("@id", id));

    [Fact]
    public async Task Round_trips_a_category_and_hands_back_the_identifier_it_assigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var name = AUniqueName();

        var id = (await repository.AddAsync(
            ANewCategory(name, warnWithinDays: 90) with { HazardClass = 3, IsNotCarried = true }, cancellationToken))!.Value;

        try
        {
            var stored = await repository.GetByIdAsync(id, cancellationToken);

            stored.Should().NotBeNull();
            stored!.NameEn.Should().Be(name);
            stored.NameUk.Should().BeEmpty();
            stored.IsFixed.Should().BeFalse();
            stored.HazardClass.Should().Be(3);
            stored.IsNotCarried.Should().BeTrue();
            stored.WarnWithinDays.Should().Be(90);
            stored.UkCode.Should().BeNull();
            (await repository.ListAsync(cancellationToken)).Should().Contain(category => category.Id == id);
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task A_name_cannot_be_used_twice_on_create_or_on_update()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var name = AUniqueName();
        var first = (await repository.AddAsync(ANewCategory(name), cancellationToken))!.Value;
        var second = (await repository.AddAsync(ANewCategory(AUniqueName()), cancellationToken))!.Value;

        try
        {
            (await repository.AddAsync(ANewCategory(name), cancellationToken)).Should().BeNull();
            (await repository.UpdateAsync(ANewCategory(name) with { Id = second }, cancellationToken)).Should().BeNull();
        }
        finally
        {
            await RemoveAsync(first);
            await RemoveAsync(second);
        }
    }

    [Fact]
    public async Task An_update_changes_the_rule_but_cannot_make_a_category_fixed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = (await repository.AddAsync(ANewCategory(AUniqueName()), cancellationToken))!.Value;

        try
        {
            var renamed = AUniqueName();

            (await repository.UpdateAsync(
                ANewCategory(renamed, warnWithinDays: 180) with { Id = id, IsFixed = true, NameUk = "Ліки" },
                cancellationToken)).Should().BeTrue();

            var stored = (await repository.GetByIdAsync(id, cancellationToken))!;
            stored.NameEn.Should().Be(renamed);
            stored.NameUk.Should().Be("Ліки");
            stored.WarnWithinDays.Should().Be(180);
            stored.IsFixed.Should().BeFalse();
            (await repository.UpdateAsync(ANewCategory("Nobody") with { Id = 0 }, cancellationToken)).Should().BeFalse();
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task A_code_is_one_row_per_authority_and_is_cleared_by_null()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);
        var id = (await repository.AddAsync(ANewCategory(AUniqueName()), cancellationToken))!.Value;

        try
        {
            (await repository.SetCodeAsync(id, CustomsAuthority.EU, "300490", cancellationToken)).Should().BeTrue();
            (await repository.SetCodeAsync(id, CustomsAuthority.EU, "30049099", cancellationToken)).Should().BeTrue();
            (await repository.SetCodeAsync(id, CustomsAuthority.UA, "3004900000", cancellationToken)).Should().BeTrue();

            var stored = (await repository.GetByIdAsync(id, cancellationToken))!;
            stored.EuCode.Should().Be("30049099");
            stored.UaCode.Should().Be("3004900000");
            stored.UkCode.Should().BeNull();
            stored.CodeFor(CustomsAuthority.EU).Should().Be("30049099");

            (await repository.SetCodeAsync(id, CustomsAuthority.EU, null, cancellationToken)).Should().BeTrue();
            (await repository.GetByIdAsync(id, cancellationToken))!.EuCode.Should().BeNull();
        }
        finally
        {
            await RemoveAsync(id);
        }
    }

    [Fact]
    public async Task Setting_a_code_for_a_category_that_does_not_exist_changes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = await ConnectOrSkipAsync(cancellationToken);

        (await repository.SetCodeAsync(0, CustomsAuthority.UK, "300490", cancellationToken)).Should().BeFalse();
    }
}
