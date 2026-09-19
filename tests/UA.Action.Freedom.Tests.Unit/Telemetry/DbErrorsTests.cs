using System.Diagnostics.Metrics;
using AwesomeAssertions;
using UA.Action.Freedom.Data;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

public sealed class DbErrorsTests
{
    [Theory]
    [InlineData(547, "foreign_key")]
    [InlineData(2627, "unique")]
    [InlineData(2601, "unique")]
    [InlineData(1205, "deadlock")]
    [InlineData(-2, "timeout")]
    [InlineData(40613, "unavailable")]
    [InlineData(-1, "unavailable")]
    [InlineData(53, "unavailable")]
    [InlineData(102, "other")]
    [InlineData(0, "other")]
    public void A_sql_error_number_is_classified_into_a_small_fixed_set(int number, string expected)
    {
        DbErrors.Classify(number).Should().Be(expected);
    }

    [Fact]
    public void A_recorded_error_is_counted_under_its_class_and_nothing_more_specific()
    {
        var recorded = new List<(long Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == DbErrors.MeterName && instrument.Name == "freedom.db.errors")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var captured = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);

            if (captured.GetValueOrDefault("sql_error") is "deadlock")
            {
                lock (recorded)
                {
                    recorded.Add((value, captured));
                }
            }
        });
        listener.Start();

        DbErrors.Record(1205);

        lock (recorded)
        {
            var measurement = recorded.Should().ContainSingle().Subject;
            measurement.Value.Should().Be(1);
            measurement.Tags.Keys.Should().BeEquivalentTo(["sql_error"]);
        }
    }

    [Fact]
    public void An_exception_that_is_not_a_sql_error_is_not_counted_and_is_not_an_error_to_record()
    {
        var act = () => DbErrors.Record(new InvalidOperationException("password=hunter2"));

        act.Should().NotThrow();
    }
}
