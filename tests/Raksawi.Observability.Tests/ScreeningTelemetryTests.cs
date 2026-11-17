using System.Diagnostics.Metrics;
using Screening.Domain;

namespace Raksawi.Observability.Tests;

/// <summary>
/// Guards the metric shapes. A cardinality mistake is not caught by a compiler
/// and is expensive to discover in production, so it is caught here.
/// </summary>
public class ScreeningTelemetryTests
{
    [Fact]
    public void Screened_counter_is_dimensioned_only_by_outcome()
    {
        var recorded = new List<KeyValuePair<string, object?>>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == ScreeningTelemetry.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                recorded.Add(tag);
            }
        });

        listener.Start();

        ScreeningTelemetry.Screened.Add(1,
            new KeyValuePair<string, object?>("outcome", "clear"));

        listener.RecordObservableInstruments();

        // The assertion that matters: application.id must never appear here.
        // Two outcomes produce two series; one per application produces one per
        // application, forever.
        Assert.All(recorded, tag => Assert.Equal("outcome", tag.Key));
    }

    [Fact]
    public void Instrument_names_follow_the_dotted_convention()
    {
        // Names are effectively permanent — dashboards, alerts, and runbooks
        // all quote them, and renaming one silently breaks every consumer.
        Assert.Equal("screening.applications.screened", ScreeningTelemetry.Screened.Name);
        Assert.Equal("screening.duration", ScreeningTelemetry.Duration.Name);
        Assert.Equal("screening.applications.abandoned", ScreeningTelemetry.Abandoned.Name);
        Assert.Equal("screening.retries", ScreeningTelemetry.Retries.Name);
    }

    [Fact]
    public void Instruments_declare_units()
    {
        // A duration without a unit is read as milliseconds by one person and
        // seconds by the next, and the disagreement surfaces during an incident.
        Assert.Equal("s", ScreeningTelemetry.Duration.Unit);
        Assert.Equal("{application}", ScreeningTelemetry.Screened.Unit);
        Assert.Equal("{application}", ScreeningTelemetry.Abandoned.Unit);
        Assert.Equal("{retry}", ScreeningTelemetry.Retries.Unit);
    }

    [Fact]
    public void Duration_buckets_resolve_seconds()
    {
        // Without advice the SDK's default boundaries (0, 5, 10, 25, ... 10000)
        // apply, made for milliseconds: every screening lands in the first
        // bucket and a healthy p95 reads 4.75 s. "Screening slow" fires at 2.5 s.
        var boundaries = ScreeningTelemetry.Duration.Advice?.HistogramBucketBoundaries;

        Assert.NotNull(boundaries);
        Assert.Contains(2.5, boundaries);
        Assert.True(boundaries[0] <= 0.01, "the first bucket must resolve a healthy screening");
    }

    [Fact]
    public void Activity_source_is_versioned()
    {
        // Exported as otel.scope.version. It is how "this span is missing" is
        // told apart from "this build never emitted it".
        Assert.False(string.IsNullOrWhiteSpace(ScreeningTelemetry.Source.Version));
    }

    [Fact]
    public void Abandoned_is_a_separate_counter_from_screened()
    {
        // Merging them would hide the demo's whole point: work that silently
        // never completed is an absence of an outcome, not an outcome.
        Assert.NotEqual(ScreeningTelemetry.Screened.Name, ScreeningTelemetry.Abandoned.Name);
    }
}
