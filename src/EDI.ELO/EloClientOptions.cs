namespace EDI.ELO;

/// <summary>
/// Configuration for the French customs ELO (Enveloppe Logistique Obligatoire) EDI client.
/// </summary>
public sealed class EloClientOptions
{
    /// <summary>
    /// Base URL the client issues requests against. The ELO OpenAPI spec has no
    /// <c>servers:</c> section, so — unlike <c>HMRC.GVMS</c>/<c>HMRC.PushPullNotifications</c>,
    /// which default to HMRC's production host — there is no known real endpoint to default
    /// to here. This must always be set explicitly by the caller.
    /// </summary>
    public Uri? BaseUrl { get; set; }
}
