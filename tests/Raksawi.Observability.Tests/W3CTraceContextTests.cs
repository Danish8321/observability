using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// The trace-context correction is the one .NET Framework failure that is
/// silent by default — a trace splits in two rather than erroring. Its warning
/// was computed on both runtimes and observable on neither, so these assert it
/// is produced and that something writes it.
/// </summary>
public sealed class W3CTraceContextTests
{
    [Fact]
    public void Correcting_the_format_produces_a_warning()
    {
        var original = Activity.DefaultIdFormat;
        var originalForced = Activity.ForceDefaultIdFormat;

        try
        {
            // What .NET Framework starts as, and what .NET 10 must never be.
            Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical;

            var warning = ServiceIdentity.EnsureW3CTraceContext();

            Assert.Equal(ServiceIdentity.W3CCorrectedMessage, warning);
            Assert.Equal(ActivityIdFormat.W3C, Activity.DefaultIdFormat);
            Assert.True(Activity.ForceDefaultIdFormat);
        }
        finally
        {
            Activity.DefaultIdFormat = original == ActivityIdFormat.Unknown ? ActivityIdFormat.W3C : original;
            Activity.ForceDefaultIdFormat = originalForced;
        }
    }

    [Fact]
    public void A_format_that_was_already_correct_produces_no_warning()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;

        Assert.Null(ServiceIdentity.EnsureW3CTraceContext());
    }

    [Fact]
    public void Correcting_the_format_is_recorded_as_a_metric()
    {
        // 🔒 ADR-0005 pairs the warning with a metric because a warning in a
        // log on an IIS host is not a control. The warning shipped; the metric
        // did not, so panel 4.10 had no data source and the only control on
        // the runtime the ADR is about was a log line.
        var original = Activity.DefaultIdFormat;
        var originalForced = Activity.ForceDefaultIdFormat;

        try
        {
            Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical;
            ServiceIdentity.EnsureW3CTraceContext();

            Assert.Equal(1, TraceContextMetric.Current);
        }
        finally
        {
            Activity.DefaultIdFormat = original == ActivityIdFormat.Unknown ? ActivityIdFormat.W3C : original;
            Activity.ForceDefaultIdFormat = originalForced;
        }
    }

    [Fact]
    public void A_format_that_was_already_correct_is_recorded_as_zero()
    {
        // Reported rather than absent: "the check ran and the format was fine"
        // and "nothing is reporting" are different answers, and a panel that
        // cannot tell them apart has the same blind spot in a new place.
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;

        ServiceIdentity.EnsureW3CTraceContext();

        Assert.Equal(0, TraceContextMetric.Current);
    }

    [Fact]
    public void The_trace_context_gauge_is_collected_by_a_subscribed_provider()
    {
        // The failure this whole ticket is about: an instrument nothing
        // subscribes to records in-process and reaches no store, with a green
        // build and green tests. Asserting the value is not enough — the meter
        // has to be registered on the provider.
        var original = Activity.DefaultIdFormat;
        var originalForced = Activity.ForceDefaultIdFormat;
        var exporter = new CollectingExporter();

        try
        {
            Activity.DefaultIdFormat = ActivityIdFormat.Hierarchical;
            ServiceIdentity.EnsureW3CTraceContext();

            var options = new RaksawiObservabilityOptions
            {
                ServiceName = "trace-context-test",
                ServiceNamespace = "kyc",
                SamplingRatio = 1.0,
            };

            using var provider = Sdk.CreateMeterProviderBuilder()
                .AddRaksawiIdentity(options, ServiceIdentity.BuildResource(options))
                .AddReader(new BaseExportingMetricReader(exporter))
                .Build();

            provider.ForceFlush();
        }
        finally
        {
            Activity.DefaultIdFormat = original == ActivityIdFormat.Unknown ? ActivityIdFormat.W3C : original;
            Activity.ForceDefaultIdFormat = originalForced;
        }

        // Collected more than once — ForceFlush, then again when the provider
        // is disposed. A gauge is read at collection time, so every reading has
        // to carry the state; a value that only appeared in the first would be
        // the ordering bug this instrument is shaped to avoid.
        var readings = exporter.Values
            .Where(v => v.Name == "raksawi.telemetry.trace_context.corrected")
            .Select(v => v.Value)
            .ToList();

        Assert.NotEmpty(readings);
        Assert.All(readings, value => Assert.Equal(1, value));
    }

    /// <summary>
    /// The core SDK's exporter base, rather than a package reference for
    /// <c>AddInMemoryExporter</c>: one test does not justify a new dependency.
    /// </summary>
    private sealed class CollectingExporter : BaseExporter<Metric>
    {
        public List<(string Name, long Value)> Values { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach (var metric in batch)
            {
                foreach (ref readonly var point in metric.GetMetricPoints())
                {
                    Values.Add((metric.Name, point.GetGaugeLastValueLong()));
                }
            }

            return ExportResult.Success;
        }
    }

    [Fact]
    public async Task The_warning_is_written_at_host_start()
    {
        // The regression: the .NET 10 path used to compute this warning, raise a
        // log filter with it, and never write it anywhere.
        var logger = new CapturingLogger();

        await new W3CTraceContextWarning(logger).StartAsync(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(ServiceIdentity.W3CCorrectedMessage, entry.Message);
    }

    private sealed class CapturingLogger : ILogger<W3CTraceContextWarning>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
