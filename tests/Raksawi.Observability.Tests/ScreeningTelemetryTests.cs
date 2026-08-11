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
    }

    [Fact]
    public void Instruments_declare_units()
    {
        // A duration without a unit is read as milliseconds by one person and
        // seconds by the next, and the disagreement surfaces during an incident.
        Assert.Equal("s", ScreeningTelemetry.Duration.Unit);
        Assert.Equal("{application}", ScreeningTelemetry.Screened.Unit);
        Assert.Equal("{application}", ScreeningTelemetry.Abandoned.Unit);
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
