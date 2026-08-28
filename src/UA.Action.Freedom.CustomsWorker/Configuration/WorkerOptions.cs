namespace UA.Action.Freedom.CustomsWorker.Configuration;

/// <summary>How often the worker wakes up.</summary>
/// <remarks>
/// In Azure these are a queue trigger and a timer trigger; the shape of the schedule is the
/// same either way. Both intervals are short here because a developer waiting on a local
/// round trip is not paying for vCPU-seconds — in Azure the outcome poll runs only while a
/// GMR is in flight and falls idle otherwise (<c>docs/recommendations.md</c> §4.1).
/// </remarks>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>Seconds between checks of the customs work queue when it is empty.</summary>
    public int QueuePollSeconds { get; set; } = 5;

    /// <summary>Seconds between polls of the HMRC notification box.</summary>
    public int OutcomePollSeconds { get; set; } = 15;
}

/// <summary>The HMRC endpoints and credentials the worker uses.</summary>
/// <remarks>
/// In Azure the client credentials come from Key Vault by managed identity (§4.2); locally
/// they come from <c>.env</c> and WireMock does not check them.
/// </remarks>
public sealed class HmrcOptions
{
    public const string SectionName = "Hmrc";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public EndpointOptions Gvms { get; set; } = new();

    public PpnsOptions Ppns { get; set; } = new();

    public class EndpointOptions
    {
        public string? BaseUrl { get; set; }
    }

    public sealed class PpnsOptions : EndpointOptions
    {
        /// <summary>The notification box HMRC publishes GMR outcomes to.</summary>
        public string? BoxId { get; set; }
    }
}
