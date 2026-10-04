using UA.Action.Freedom.Application.Declarations;

namespace UA.Action.Freedom.Api.Configuration;

/// <summary>
/// What HMRC needs to identify a movement that is not derivable from the manifest itself.
/// </summary>
/// <remarks>
/// Environment-driven like everything else here. Both values are charity- or route-level facts
/// rather than per-manifest ones: the EORI belongs to Ukrainian Action, and the route is the
/// crossing the convoys use. If convoys ever cross by more than one route, <see cref="RouteId"/>
/// becomes a property of the convoy and moves out of configuration.
/// </remarks>
public sealed class CustomsOptions
{
    public const string SectionName = "Customs";

    /// <summary>The charity's Economic Operators Registration and Identification number.</summary>
    public string HaulierEori { get; set; } = string.Empty;

    /// <summary>HMRC's identifier for the crossing the convoys use.</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the GMR is submitted by the Customs Worker or filed by hand in the HMRC portal and recorded
    /// (ADR 0006). Manual unless an operator has proved the HMRC integration is live and says otherwise.
    /// </summary>
    public SubmissionMode GmrSubmissionMode { get; set; } = SubmissionMode.Manual;

    /// <summary>As <see cref="GmrSubmissionMode"/>, for the French logistics envelope. The ENS and the goods list are always manual.</summary>
    public SubmissionMode EloSubmissionMode { get; set; } = SubmissionMode.Manual;
}
