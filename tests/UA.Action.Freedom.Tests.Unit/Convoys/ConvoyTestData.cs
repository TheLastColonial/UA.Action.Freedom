using UA.Action.Freedom.Application.Convoys;

namespace UA.Action.Freedom.Tests.Unit.Convoys;

/// <summary>
/// Factory for convoy test data. Every field has a sensible default; a test overrides only what
/// it is actually about.
/// </summary>
internal static class ConvoyTestData
{
    internal const int Id = 42;

    internal static readonly DateTime Start = new(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime ExpectedEnd = new(2026, 9, 5, 18, 0, 0, DateTimeKind.Utc);
    internal static readonly DateTime Published = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);

    internal static CreateConvoyCommand ACreateCommand() => new(Start, ExpectedEnd);

    internal static UpdateConvoyCommand AnUpdateCommand(int id = Id) =>
        new(id, Start, ExpectedEnd.AddDays(1));

    internal static ConvoyReadModel AReadModel(int id = Id, DateTime? truckListPublishedAt = null) =>
        new(id, Start, ExpectedEnd, truckListPublishedAt);

    internal static ConvoyReadModel APublishedConvoy(int id = Id) => AReadModel(id, Published);

    internal static RouteStopReadModel AStop(int sequence, string postcode = "CV1 2AB") =>
        new(sequence, "Unit 4", "Cross Road", "Coventry", "United Kingdom", postcode);
}
