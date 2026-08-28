namespace UA.Action.Freedom.CustomsWorker.Customs;

/// <summary>
/// The message the Freedom Application puts on the customs work queue when a dispatcher
/// asks for a Goods Movement Reference.
/// </summary>
/// <remarks>
/// Deliberately narrow. It carries what HMRC needs to identify a movement and nothing
/// else — in particular no receiver name, contact or address. Delivery detail is the most
/// sensitive data in the system (<c>docs/recommendations.md</c> §4.4) and a queue message
/// is durable, replicated and readable by anything holding the storage credential, so it
/// is the wrong place for it. The manifest reference is enough for the worker to write the
/// outcome back against.
/// </remarks>
/// <param name="ManifestId">The manifest this movement is for.</param>
/// <param name="HaulierEori">The charity's EORI number, as registered with HMRC.</param>
/// <param name="VehicleRegistration">Registration plate of the vehicle making the crossing.</param>
/// <param name="RouteId">HMRC route identifier for the planned crossing.</param>
/// <param name="LocalDateTimeOfDeparture">Planned departure, local to the port, as <c>yyyy-MM-ddTHH:mm</c>.</param>
public sealed record GmrSubmission(
    string ManifestId,
    string HaulierEori,
    string VehicleRegistration,
    string RouteId,
    string LocalDateTimeOfDeparture);
