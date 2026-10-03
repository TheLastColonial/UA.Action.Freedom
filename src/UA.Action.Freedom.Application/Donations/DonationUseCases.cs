using UA.Action.Freedom.Application.Abstractions;

namespace UA.Action.Freedom.Application.Donations;

/// <summary>Record a drop-off by a donor. Entered by a Dispatcher or Loader, optionally before the box arrives (O22).</summary>
public sealed record CreateDonationCommand(Guid DonorId, DateOnly ReceivedOn, string? Notes);

public enum CreateDonationOutcome
{
    Created,
    DonorNotFound
}

public sealed record CreateDonationResult(CreateDonationOutcome Outcome, int Id = 0);

public sealed class CreateDonationHandler(IDonationRepository donations, IDonorRepository donors)
    : ICommandHandler<CreateDonationCommand, CreateDonationResult>
{
    public async Task<CreateDonationResult> HandleAsync(CreateDonationCommand command, CancellationToken cancellationToken)
    {
        // An erased donor reads as not found, so a new donation cannot be recorded against one.
        if (await donors.GetByIdAsync(command.DonorId, cancellationToken) is null)
        {
            return new CreateDonationResult(CreateDonationOutcome.DonorNotFound);
        }

        var id = await donations.AddAsync(command.DonorId, command.ReceivedOn, command.Notes, cancellationToken);

        return new CreateDonationResult(CreateDonationOutcome.Created, id);
    }
}

/// <summary>Correct the date or notes of a donation. Its donor is its identity and does not change.</summary>
public sealed record UpdateDonationCommand(int Id, DateOnly ReceivedOn, string? Notes);

public enum UpdateDonationOutcome
{
    Updated,
    NotFound
}

public sealed class UpdateDonationHandler(IDonationRepository donations)
    : ICommandHandler<UpdateDonationCommand, UpdateDonationOutcome>
{
    public async Task<UpdateDonationOutcome> HandleAsync(UpdateDonationCommand command, CancellationToken cancellationToken) =>
        await donations.UpdateAsync(command.Id, command.ReceivedOn, command.Notes, cancellationToken)
            ? UpdateDonationOutcome.Updated
            : UpdateDonationOutcome.NotFound;
}

public sealed record DeleteDonationCommand(int Id);

public enum DeleteDonationOutcome
{
    Deleted,
    NotFound,
    StillReferenced
}

public sealed class DeleteDonationHandler(IDonationRepository donations)
    : ICommandHandler<DeleteDonationCommand, DeleteDonationOutcome>
{
    public async Task<DeleteDonationOutcome> HandleAsync(DeleteDonationCommand command, CancellationToken cancellationToken) =>
        await donations.DeleteAsync(command.Id, cancellationToken) switch
        {
            DeleteDonationResult.Deleted => DeleteDonationOutcome.Deleted,
            DeleteDonationResult.StillReferenced => DeleteDonationOutcome.StillReferenced,
            _ => DeleteDonationOutcome.NotFound,
        };
}

public sealed record GetDonationByIdQuery(int Id);

public sealed class GetDonationByIdHandler(IDonationRepository donations)
    : IQueryHandler<GetDonationByIdQuery, DonationReadModel?>
{
    public Task<DonationReadModel?> HandleAsync(GetDonationByIdQuery query, CancellationToken cancellationToken) =>
        donations.GetByIdAsync(query.Id, cancellationToken);
}

/// <summary>A page of donations, newest first, optionally one donor's. Page size is clamped to 1..200.</summary>
public sealed record ListDonationsQuery(Guid? DonorId, int Page, int PageSize);

public sealed class ListDonationsHandler(IDonationRepository donations)
    : IQueryHandler<ListDonationsQuery, IReadOnlyList<DonationReadModel>>
{
    public Task<IReadOnlyList<DonationReadModel>> HandleAsync(ListDonationsQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Clamp(query.Page, query.PageSize);
        return donations.ListAsync(query.DonorId, page, pageSize, cancellationToken);
    }
}

/// <summary>One donor's donations, or <c>null</c> if there never was such a donor. An erased donor still has theirs.</summary>
public sealed record ListDonorDonationsQuery(Guid DonorId, int Page, int PageSize);

public sealed class ListDonorDonationsHandler(IDonationRepository donations)
    : IQueryHandler<ListDonorDonationsQuery, IReadOnlyList<DonationReadModel>?>
{
    public async Task<IReadOnlyList<DonationReadModel>?> HandleAsync(
        ListDonorDonationsQuery query, CancellationToken cancellationToken)
    {
        if (await donations.DonorNameAsync(query.DonorId, cancellationToken) is null)
        {
            return null;
        }

        var (page, pageSize) = Paging.Clamp(query.Page, query.PageSize);
        return await donations.ListAsync(query.DonorId, page, pageSize, cancellationToken);
    }
}
