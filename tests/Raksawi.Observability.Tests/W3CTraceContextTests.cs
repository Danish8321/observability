using System.Diagnostics;
using Microsoft.Extensions.Logging;
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
