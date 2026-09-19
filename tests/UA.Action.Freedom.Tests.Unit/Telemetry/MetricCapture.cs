using System.Diagnostics.Metrics;
using System.Globalization;

namespace UA.Action.Freedom.Tests.Unit.Telemetry;

public sealed record CapturedMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

/// <summary>
/// Listens to one <see cref="Meter"/> instance only, so tests running in parallel cannot see each
/// other's measurements the way they would through a listener keyed on the meter's name.
/// </summary>
public sealed class MetricCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<CapturedMeasurement> _measurements = [];
    private readonly object _gate = new();

    public MetricCapture(Meter meter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<int>((i, v, t, _) => Record(i, v, t));
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        _listener.Start();
    }

    public IReadOnlyList<CapturedMeasurement> Measurements
    {
        get
        {
            lock (_gate)
            {
                return [.. _measurements];
            }
        }
    }

    /// <summary>Pulls the current value of every observable gauge, once.</summary>
    public MetricCapture Observe()
    {
        _listener.RecordObservableInstruments();
        return this;
    }

    public IReadOnlyList<CapturedMeasurement> Of(string instrument) =>
        [.. Measurements.Where(m => m.Instrument == instrument)];

    public double Sum(string instrument, params (string Key, object Value)[] tags) =>
        Measurements
            .Where(m => m.Instrument == instrument && tags.All(t => Equals(m.Tags.GetValueOrDefault(t.Key), t.Value)))
            .Sum(m => m.Value);

    public void Dispose() => _listener.Dispose();

    private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var captured = new CapturedMeasurement(
            instrument.Name,
            Convert.ToDouble(value, CultureInfo.InvariantCulture),
            tags.ToArray().ToDictionary(t => t.Key, t => t.Value));

        lock (_gate)
        {
            _measurements.Add(captured);
        }
    }
}
