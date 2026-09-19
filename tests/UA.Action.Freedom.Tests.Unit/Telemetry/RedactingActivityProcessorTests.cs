using System.Diagnostics;
using AwesomeAssertions;
using UA.Action.Freedom.Telemetry;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

public sealed class RedactingActivityProcessorTests : IDisposable
{
    private readonly ActivitySource _source = new("UA.Action.Freedom.Tests.Redaction");
    private readonly ActivityListener _listener;
    private readonly RedactingActivityProcessor _processor = new();

    public RedactingActivityProcessorTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == _source.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    private Activity Start(ActivityKind kind, params (string Key, object Value)[] tags)
    {
        var activity = _source.StartActivity("test", kind)!;

        foreach (var (key, value) in tags)
        {
            activity.SetTag(key, value);
        }

        return activity;
    }

    [Fact]
    public void A_server_span_keeps_the_route_template_and_loses_the_concrete_path()
    {
        using var activity = Start(
            ActivityKind.Server,
            ("http.route", "/boxes/scan/{token}"),
            ("url.path", "/boxes/scan/9f1c-a-bearer-like-token"));

        _processor.OnEnd(activity);

        activity.GetTagItem("url.path").Should().Be("/boxes/scan/{token}");
    }

    [Fact]
    public void A_server_span_never_carries_the_query_string()
    {
        using var activity = Start(
            ActivityKind.Server,
            ("http.route", "/receivers/{receiverRef}/detail"),
            ("url.path", "/receivers/6c1f/detail"),
            ("url.query", "reason=delivering to Dr Petrenko in Kharkiv"));

        _processor.OnEnd(activity);

        activity.GetTagItem("url.query").Should().BeNull();
        activity.TagObjects.Select(t => t.Value?.ToString()).Should().NotContain(v => v!.Contains("Petrenko"));
    }

    [Fact]
    public void A_server_span_with_no_matched_route_does_not_leak_the_raw_path()
    {
        using var activity = Start(ActivityKind.Server, ("url.path", "/no/such/thing/9f1c"));

        _processor.OnEnd(activity);

        activity.GetTagItem("url.path").Should().Be("(unmatched)");
    }

    [Fact]
    public void A_client_span_is_reduced_to_the_peer_it_talked_to()
    {
        using var activity = Start(
            ActivityKind.Client,
            ("url.full", "http://hmrc.local:8080/movements/GMRA00000001?status=PENDING"),
            ("server.address", "hmrc.local"));

        _processor.OnEnd(activity);

        activity.GetTagItem("url.full").Should().Be("http://hmrc.local:8080");
        activity.GetTagItem("server.address").Should().Be("hmrc.local");
    }

    [Fact]
    public void A_client_span_with_the_older_http_url_tag_is_reduced_too()
    {
        using var activity = Start(
            ActivityKind.Client,
            ("http.url", "http://azurite:10001/devstoreaccount1/manifests/M-0001.txt"));

        _processor.OnEnd(activity);

        activity.GetTagItem("http.url").Should().Be("http://azurite:10001");
    }

    [Fact]
    public void An_ordinary_sql_statement_is_kept_because_it_is_how_a_slow_query_is_found()
    {
        using var activity = Start(
            ActivityKind.Client,
            ("db.query.text", "SELECT Id, Vin FROM dbo.Manifest WHERE Id = @id"),
            ("db.collection.name", "dbo.Manifest"));

        _processor.OnEnd(activity);

        activity.GetTagItem("db.query.text").Should().Be("SELECT Id, Vin FROM dbo.Manifest WHERE Id = @id");
        activity.GetTagItem("db.collection.name").Should().Be("dbo.Manifest");
    }

    [Theory]
    [InlineData("SELECT ContactName, AddressLine1 FROM sensitive.ReceiverDetail WHERE ReceiverRef = @ref")]
    [InlineData("select * from [sensitive].[ReceiverDetailAccessLog]")]
    [InlineData("INSERT INTO SENSITIVE.ReceiverDetail (ReceiverRef) VALUES (@ref)")]
    public void A_statement_on_the_sensitive_schema_is_not_recorded_at_all(string statement)
    {
        // The statement holds no values — it is parameterised — but its column names describe the
        // shape of the most protected data in the system, and traces are read by many more people
        // than the schema is granted to.
        using var activity = Start(
            ActivityKind.Client,
            ("db.query.text", statement),
            ("db.collection.name", "sensitive.ReceiverDetail"),
            ("db.query.summary", "SELECT sensitive.ReceiverDetail"));
        activity.DisplayName = "SELECT sensitive.ReceiverDetail";

        _processor.OnEnd(activity);

        activity.GetTagItem("db.query.text").Should().Be("(statement on the sensitive schema not recorded)");
        activity.GetTagItem("db.collection.name").Should().BeNull();
        activity.GetTagItem("db.query.summary").Should().BeNull();
        activity.DisplayName.Should().Be("SQL (sensitive schema)");
    }

    [Fact]
    public void Spans_that_are_neither_server_nor_client_are_left_alone()
    {
        using var activity = Start(ActivityKind.Internal, ("url.path", "/whatever"), ("url.query", "a=b"));

        _processor.OnEnd(activity);

        activity.GetTagItem("url.path").Should().Be("/whatever");
        activity.GetTagItem("url.query").Should().Be("a=b");
    }
}
