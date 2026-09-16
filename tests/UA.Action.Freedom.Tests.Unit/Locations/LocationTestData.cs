using UA.Action.Freedom.Application.Locations;

namespace UA.Action.Freedom.Tests.Unit.Locations;

/// <summary>
/// Factory for location and bay test data. Every field has a sensible default; a test
/// overrides only what it is actually about.
/// </summary>
internal static class LocationTestData
{
    internal static CreateLocationCommand ACreateLocationCommand(string name = "Coventry Depot") => new(
        name, House: "Unit 4", Street: "Cross Road", City: "Coventry", Country: "United Kingdom", Postcode: "CV1 2AB");

    internal static UpdateLocationCommand AnUpdateLocationCommand(int id = 3, string name = "Coventry Depot") => new(
        id, name, House: "Unit 4", Street: "Cross Road", City: "Coventry", Country: "United Kingdom", Postcode: "CV1 2AB");

    internal static LocationReadModel ALocation(int id = 3, string name = "Coventry Depot") => new(
        id, name, House: "Unit 4", Street: "Cross Road", City: "Coventry", Country: "United Kingdom", Postcode: "CV1 2AB");

    internal static CreateBayCommand ACreateBayCommand(int locationId = 3, string code = "A1") => new(locationId, code);

    internal static UpdateBayCommand AnUpdateBayCommand(int id = 9, int locationId = 3, string code = "A1") => new(
        id, locationId, code);

    internal static BayReadModel ABay(int id = 9, int locationId = 3, string code = "A1") => new(id, locationId, code);
}
