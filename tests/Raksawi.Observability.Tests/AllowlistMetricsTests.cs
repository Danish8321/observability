using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// The dropped-key counter is the only thing that makes an over-tight allowlist
/// visible: a family that failed to cover a real key looks exactly like
/// instrumentation that is not running. These assert it actually reaches a
/// collector, and that it cannot take the metrics store down while doing so.
/// </summary>
public class AllowlistMetricsTests : IDisposable
{
    private readonly ActivitySource _source = new(nameof(AllowlistMetricsTests));
    private readonly ActivityListener _listener;

    public AllowlistMetricsTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == nameof(AllowlistMetricsTests),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
        GC.SuppressFinalize(this);
    }

    private AllowlistProcessor CreateProcessor() =>
        new(AttributeAllowlist.ForDeclaredKeys("application.id"), []);

    private void DropOneKey(AllowlistProcessor processor, string key)
    {
        using var activity = _source.StartActivity("work")!;
        activity.SetTag(key, "value");
        processor.OnEnd(activity);
    }

    [Fact]
    public void A_dropped_key_is_counted_and_dimensioned_by_attribute_key()
    {
        var recorded = new List<KeyValuePair<string, object?>>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AllowlistDropMetric.MeterName)
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

        DropOneKey(CreateProcessor(), "applicantCpr");

        Assert.Contains(
            recorded,
            tag => tag.Key == "attribute.key" && (string?)tag.Value == "applicantCpr");

        // 🔒 One counter across spans and logs, told apart by a dimension
        // bounded at two values forever. Two counters would make "is anything
        // being dropped" two questions.
        Assert.Contains(
            recorded,
            tag => tag.Key == "telemetry.signal" && (string?)tag.Value == AllowlistDropMetric.SpanSignal);
    }

    /// <summary>
    /// 🔒 The regression this guards is the one RKS002 raises as an error.
    /// Dropped keys are the ungoverned ones, so a service building keys from
    /// data would otherwise mint one time series per value — in the component
    /// whose job is preventing exactly that.
    /// </summary>
    [Fact]
    public void The_dimension_is_bounded_when_a_service_builds_keys_from_data()
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == AllowlistDropMetric.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                _ = values.Add((string)tag.Value!);
            }
        });
        listener.Start();

        var processor = CreateProcessor();
        var distinctKeys = AllowlistDropMetric.MaxDistinctDroppedKeys * 10;

        // A prefix unique to this test: the meter is process-wide and static, so
        // a listener here also sees drops from tests running in parallel.
        var prefix = $"app.{Guid.NewGuid():n}.";

        for (var i = 0; i < distinctKeys; i++)
        {
            DropOneKey(processor, $"{prefix}{i}");
        }

        var mine = values.Where(value => value.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        // Bounded by the cap, not by how many distinct keys the service
        // invented, and everything past the cap lands in one bucket.
        Assert.True(
            mine.Count <= AllowlistDropMetric.MaxDistinctDroppedKeys,
            $"{mine.Count} distinct dimension values from {distinctKeys} distinct keys");
        Assert.Contains(AllowlistDropMetric.OverflowDimension, values);
    }

    [Fact]
    public void The_counter_reaches_a_collector_when_the_meter_is_registered()
    {
        var exporter = new CollectingExporter();

        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(AllowlistDropMetric.MeterName)
            .AddReader(new BaseExportingMetricReader(exporter))
            .Build();

        DropOneKey(CreateProcessor(), "applicantCpr");
        provider.ForceFlush();

        Assert.Contains("raksawi.telemetry.attributes.dropped", exporter.Names);
    }

    /// <summary>
    /// The failure this pair of tests exists for: a provider that does not
    /// subscribe the meter collects nothing, and nothing in the process says so.
    /// </summary>
    [Fact]
    public void The_counter_reaches_nothing_when_the_meter_is_not_registered()
    {
        var exporter = new CollectingExporter();

        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddReader(new BaseExportingMetricReader(exporter))
            .Build();

        DropOneKey(CreateProcessor(), "applicantCpr");
        provider.ForceFlush();

        Assert.DoesNotContain("raksawi.telemetry.attributes.dropped", exporter.Names);
    }

    private sealed class CollectingExporter : BaseExporter<Metric>
    {
        public List<string> Names { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                Names.Add(metric.Name);
            }

            return ExportResult.Success;
        }
    }
}
