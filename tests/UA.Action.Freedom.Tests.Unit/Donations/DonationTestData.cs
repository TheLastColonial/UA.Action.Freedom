using UA.Action.Freedom.Application.Donations;

namespace UA.Action.Freedom.Tests.Unit.Donations;

/// <summary>Factory for donor and donation test data. A test overrides only what it is about.</summary>
internal static class DonationTestData
{
    internal static readonly Guid DonorId = new("2b1f6c1e-52a1-4a47-9d0c-6f3a1b7d9c01");

    internal const int DonationId = 41;

    internal static readonly DateOnly ReceivedOn = new(2026, 9, 20);

    internal static CreateDonorCommand ACreateDonorCommand(string name = "Margaret Hollis") =>
        new(name, "margaret@example.org", "+447700900456");

    internal static UpdateDonorCommand AnUpdateDonorCommand(Guid? id = null, string name = "Margaret Hollis-Bell") =>
        new(id ?? DonorId, name, null, null);

    internal static DonorReadModel ADonor(string name = "Margaret Hollis") =>
        new(DonorId, name, "margaret@example.org", "+447700900456");

    internal static DonationReadModel ADonation(string donorName = "Margaret Hollis") =>
        new(DonationId, DonorId, donorName, ReceivedOn, "Two boxes of tins");

    internal static DonorReportItem AReportItem(
        string category = "Tinned food",
        int quantity = 12,
        decimal? valueGbp = 30m,
        bool boxValidated = false,
        int donationId = DonationId) =>
        new(donationId, ReceivedOn, category, quantity, valueGbp, boxValidated);
}
