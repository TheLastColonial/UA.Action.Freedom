using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Donations;

/// <summary>
/// The donor status report (O6): produced for the donor by a user, because the donor has no login.
/// </summary>
/// <remarks>
/// High level on purpose. Every type here is built only from what a donor may be told: what was given, what it was
/// worth and how far it has got. There is no receiver, region, route, location or address to carry, and no other
/// donor, so redaction is the shape of the report rather than a rule someone has to remember.
/// </remarks>
public sealed record DonorReport(
    Guid DonorId,
    string DonorName,
    int ItemCount,
    decimal TotalValueGbp,
    IReadOnlyList<DonorReportCategory> ByCategory,
    IReadOnlyList<DonorReportDonation> Donations);

public sealed record DonorReportCategory(string CategoryNameEn, int Items, decimal ValueGbp);

public sealed record DonorReportDonation(int DonationId, DateOnly ReceivedOn, IReadOnlyList<DonorReportLine> Items);

public sealed record DonorReportLine(string CategoryNameEn, int Quantity, decimal? ValueGbp, DonorItemStatus Status);

/// <summary>How far an item has got, in terms a donor can be told. It says nothing of where the goods are bound.</summary>
public enum DonorItemStatus
{
    BeingPacked,
    PackedAndChecked
}

public sealed record GetDonorReportQuery(Guid DonorId);

public sealed class GetDonorReportHandler(IDonationRepository donations)
    : IQueryHandler<GetDonorReportQuery, DonorReport?>
{
    public async Task<DonorReport?> HandleAsync(GetDonorReportQuery query, CancellationToken cancellationToken)
    {
        var name = await donations.DonorNameAsync(query.DonorId, cancellationToken);

        if (name is null)
        {
            return null;
        }

        var items = await donations.ReportItemsAsync(query.DonorId, cancellationToken);

        return new DonorReport(
            query.DonorId,
            name,
            items.Sum(item => item.Quantity),
            items.Sum(item => item.ValueGbp ?? 0m),
            items
                .GroupBy(item => item.CategoryNameEn)
                .Select(group => new DonorReportCategory(
                    group.Key, group.Sum(item => item.Quantity), group.Sum(item => item.ValueGbp ?? 0m)))
                .ToList(),
            items
                .GroupBy(item => (item.DonationId, item.ReceivedOn))
                .Select(group => new DonorReportDonation(
                    group.Key.DonationId,
                    group.Key.ReceivedOn,
                    group.Select(item => new DonorReportLine(
                        item.CategoryNameEn,
                        item.Quantity,
                        item.ValueGbp,
                        item.BoxValidated ? DonorItemStatus.PackedAndChecked : DonorItemStatus.BeingPacked)).ToList()))
                .ToList());
    }
}
