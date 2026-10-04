using System.Security.Cryptography;
using System.Text;
using UA.Action.Freedom.Application.Abstractions;
using UA.Action.Freedom.Application.Categories;
using UA.Action.Freedom.Application.People;

namespace UA.Action.Freedom.Application.Boxes;

/// <summary>
/// One line of a box's label: what kind of thing, how many, and until when.
/// </summary>
/// <remarks>
/// The whole of what an item may say on a label (D2, docs/security/0011-label-review.md). There is no description,
/// no properties, no value, no donor and no receiver here, because the label travels across borders and a free-text
/// field is the one place a Loader could type a destination. The Ukrainian name is the category's <c>NameUk</c>,
/// which an Administrator maintains; where it is empty the English name stands in. Machine translation is a seam
/// that is not wired (docs/spikes/0011-offline-translation.md).
/// </remarks>
public sealed record LabelLine(string CategoryEn, string CategoryUk, int Quantity, DateOnly? ExpiresOn);

/// <summary>Who signed the box: a code that is stable and reveals nothing, and a first name with a last initial.</summary>
public sealed record LabelSigner(string Code, string Name);

/// <summary>
/// Everything the label renderer is given about a box's contents, and nothing else. The renderer takes this type and
/// never a box or an item read model, so a receiver, a region or an address has no way to reach a label.
/// </summary>
public sealed record BoxLabelContent(IReadOnlyList<LabelLine> Lines, LabelSigner? Signer)
{
    /// <summary>A label with nothing on it but the box number: no items listed and nobody signed.</summary>
    public static BoxLabelContent None { get; } = new([], null);
}

/// <summary>
/// The code that stands for a volunteer on a label. A one-way hash of their anonymous identifier, so it needs no
/// column, prints the same on every reprint, cannot be guessed back to a person, and survives an erasure because the
/// anonymous identity is kept. It is a pseudonym: it can be resolved only inside Freedom.
/// </summary>
public static class SignerCode
{
    public static string For(Guid personId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"freedom-label-signer:{personId:D}"));
        var hex = Convert.ToHexString(hash, 0, 4);

        return $"V-{hex[..4]}-{hex[4..]}";
    }
}

/// <summary>What a label shows of a box's contents and its signer.</summary>
public sealed record GetBoxLabelContentQuery(int BoxId);

public sealed class GetBoxLabelContentHandler(
    IBoxRepository boxes,
    IItemCategoryRepository categories,
    IPersonRepository people)
    : IQueryHandler<GetBoxLabelContentQuery, BoxLabelContent?>
{
    private const string FormerVolunteer = "Former volunteer";

    public async Task<BoxLabelContent?> HandleAsync(GetBoxLabelContentQuery query, CancellationToken cancellationToken)
    {
        var box = await boxes.GetByIdAsync(query.BoxId, cancellationToken);

        if (box is null)
        {
            return null;
        }

        var items = await boxes.ListItemsAsync(query.BoxId, cancellationToken);
        var byId = (await categories.ListAsync(cancellationToken)).ToDictionary(category => category.Id);

        var lines = items
            .Select(item => ToLine(item, byId.GetValueOrDefault(item.CategoryId)))
            .OrderBy(line => line.CategoryEn, StringComparer.Ordinal)
            .ThenBy(line => line.ExpiresOn)
            .ToList();

        return new BoxLabelContent(lines, await SignerOf(box, cancellationToken));
    }

    private static LabelLine ToLine(BoxItemReadModel item, ItemCategoryReadModel? category)
    {
        var english = category?.NameEn ?? string.Empty;
        var ukrainian = string.IsNullOrWhiteSpace(category?.NameUk) ? english : category.NameUk;

        return new LabelLine(english, ukrainian, item.Quantity ?? 1, item.ExpiresOn);
    }

    private async Task<LabelSigner?> SignerOf(BoxReadModel box, CancellationToken cancellationToken)
    {
        if (box.ValidatedByPersonId is not { } personId)
        {
            return null;
        }

        // An erased volunteer has no personal data left to read, so they read as a former volunteer; the code stays.
        var person = await people.GetByIdAsync(personId, cancellationToken);

        return new LabelSigner(SignerCode.For(personId), person is null ? FormerVolunteer : Display(person));
    }

    private static string Display(PersonReadModel person)
    {
        var initial = person.LastName.Trim() is { Length: > 0 } last ? $" {char.ToUpperInvariant(last[0])}." : string.Empty;

        return $"{person.FirstName.Trim()}{initial}";
    }
}
