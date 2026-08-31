using System.Globalization;
using System.Text;

namespace UA.Action.Freedom.ManifestWorker.Documents;

/// <summary>
/// The message the Freedom Application puts on the manifest document queue when a manifest is
/// approved, and everything the printed document is allowed to contain.
/// </summary>
/// <remarks>
/// <strong>This type is the redaction.</strong> It has nowhere to put a street address, a
/// contact name or a phone number, so the worker cannot print one — not by oversight, not by a
/// later change that forgets the rule. The document travels in a vehicle across several borders
/// where it may be inspected, photographed or seized, and one listing precise Ukrainian delivery
/// addresses is a targeting document (docs/domain/key-concepts.md § Data Sensitivity,
/// recommendations §4.4.2). Region is as precise as it gets; full detail is released to the
/// driver at the point of delivery, not at load time.
///
/// The Freedom Application composes this, and its database identity is <c>DENY</c>'d on the
/// <c>sensitive</c> schema — so the address is unreachable at both ends of the queue rather than
/// merely omitted at one.
/// </remarks>
/// <param name="ManifestId">The manifest reference, printed at the top of the document.</param>
/// <param name="VehicleRegistration">The plate a border officer reads off the vehicle.</param>
/// <param name="Lines">One line per box: what is being carried, and roughly where to.</param>
public sealed record ManifestDocumentRequest(
    string ManifestId,
    string? VehicleRegistration,
    int VehicleWeightKg,
    int CargoKg,
    int CrewAndBagsKg,
    int FuelKg,
    int TotalKg,
    IReadOnlyList<ManifestDocumentLine> Lines);

/// <param name="ReceiverRegion">Region-level destination. Never a street or a city address.</param>
public sealed record ManifestDocumentLine(
    int BoxId,
    int WeightKg,
    int ItemCount,
    string? ReceiverOrganisation,
    string? ReceiverRegion);

/// <summary>
/// Renders a <see cref="ManifestDocumentRequest"/> into the document that travels with the
/// vehicle.
/// </summary>
/// <remarks>
/// Plain text, deliberately: it is deterministic, diffable and testable, and a border officer
/// reads it the same way whatever produced it. A PDF with the charity's letterhead is a
/// presentation concern that can wrap this later without changing what is on it — which is the
/// part that matters.
/// </remarks>
public static class ManifestDocumentRenderer
{
    public static string Render(ManifestDocumentRequest manifest)
    {
        var document = new StringBuilder();

        document.AppendLine("UKRAINIAN ACTION — VEHICLE MANIFEST");
        document.AppendLine("===================================");
        document.AppendLine();
        document.AppendLine($"Manifest reference : {manifest.ManifestId}");
        document.AppendLine($"Vehicle            : {manifest.VehicleRegistration ?? "not assigned"}");
        document.AppendLine();

        document.AppendLine("CARGO");
        document.AppendLine("-----");

        if (manifest.Lines.Count == 0)
        {
            document.AppendLine("No boxes are loaded against this manifest.");
        }
        else
        {
            document.AppendLine("Box      Items   Weight     Consignee / region");

            foreach (var line in manifest.Lines)
            {
                var consignee = string.Join(
                    ", ",
                    new[] { line.ReceiverOrganisation, line.ReceiverRegion }
                        .Where(part => !string.IsNullOrWhiteSpace(part)));

                document.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-8} {1,-7} {2,-10} {3}",
                    line.BoxId,
                    line.ItemCount,
                    $"{line.WeightKg} kg",
                    consignee.Length == 0 ? "not assigned" : consignee));
            }
        }

        document.AppendLine();
        document.AppendLine("WEIGHT FOR BORDER CHECK");
        document.AppendLine("-----------------------");
        document.AppendLine($"Vehicle (kerb)     : {manifest.VehicleWeightKg} kg");
        document.AppendLine($"Cargo              : {manifest.CargoKg} kg");
        document.AppendLine($"Crew and bags      : {manifest.CrewAndBagsKg} kg");
        document.AppendLine($"Fuel               : {manifest.FuelKg} kg");
        document.AppendLine($"TOTAL              : {manifest.TotalKg} kg");
        document.AppendLine();

        // Says out loud what the document does not contain, so nobody carrying it thinks it is
        // incomplete and goes looking for the address to write on it by hand.
        document.AppendLine(
            "Delivery addresses and receiver contacts are deliberately not shown on this document.");
        document.AppendLine(
            "They are released to the driver at the point of delivery by the Ground Officer.");

        return document.ToString();
    }
}
