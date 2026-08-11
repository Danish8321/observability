using System.Diagnostics;
using Raksawi.Observability.Kyc;

namespace Raksawi.Observability.Tests;

/// <summary>
/// The governed helper is where the KYC vocabulary rules are enforced, so these
/// tests are about governance rather than about behaviour.
/// </summary>
public class KycTelemetryTests : IDisposable
{
    private readonly ActivitySource _source = new("Test.KycTelemetry");
    private readonly ActivityListener _listener;

    public KycTelemetryTests()
    {
        // Without a listener nothing samples, Activity.Current stays null, and
        // every assertion below would pass vacuously.
        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Test.KycTelemetry",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SetApplicationId_tags_the_current_span()
    {
        using var activity = _source.StartActivity("work");

        KycTelemetry.SetApplicationId("app-1001");

        Assert.Equal("app-1001", activity!.GetTagItem(KycTelemetry.ApplicationIdKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_identifiers_are_not_tagged(string? value)
    {
        // An empty-string tag is worse than an absent one: it looks like data
        // and matches nothing.
        using var activity = _source.StartActivity("work");

        KycTelemetry.SetApplicationId(value);

        Assert.Null(activity!.GetTagItem(KycTelemetry.ApplicationIdKey));
    }

    [Fact]
    public void Setting_an_identifier_without_a_span_does_not_throw()
    {
        // Rev 3 I3.6: telemetry never participates in business success. An
        // unsampled or uninstrumented path must be a no-op, not an exception.
        Assert.Null(Activity.Current);

        KycTelemetry.SetApplicationId("app-1001");
        KycTelemetry.SetCorrelationId("corr-1");
        KycTelemetry.SetSessionId("sess-1");
        KycTelemetry.SetCausationId("msg-1");
    }

    [Fact]
    public void All_four_identifiers_can_coexist()
    {
        // ADR-0007 deviates from Rev 3 by keeping session and correlation
        // separate. This test is the deviation, expressed as behaviour.
        using var activity = _source.StartActivity("work");

        KycTelemetry.SetApplicationId("app-1001");
        KycTelemetry.SetCorrelationId("corr-1");
        KycTelemetry.SetSessionId("sess-1");
        KycTelemetry.SetCausationId("msg-1");

        Assert.Equal("app-1001", activity!.GetTagItem(KycTelemetry.ApplicationIdKey));
        Assert.Equal("corr-1", activity.GetTagItem(KycTelemetry.CorrelationIdKey));
        Assert.Equal("sess-1", activity.GetTagItem(KycTelemetry.SessionIdKey));
        Assert.Equal("msg-1", activity.GetTagItem(KycTelemetry.CausationIdKey));
    }

    [Fact]
    public void NewCorrelationId_is_unique_and_opaque()
    {
        var first = KycTelemetry.NewCorrelationId();
        var second = KycTelemetry.NewCorrelationId();

        Assert.NotEqual(first, second);

        // Opaque by construction. A correlation identifier derived from
        // business data would be a Class 3 value wearing a Class 2 name.
        Assert.Equal(32, first.Length);
        Assert.DoesNotContain("-", first, StringComparison.Ordinal);
    }

    [Fact]
    public void The_helper_exposes_no_metric_surface()
    {
        // The hard rule from Rev 3 D2.1: a Class 2 identifier is never a metric
        // dimension. It is enforced by absence — there is no API to do it — and
        // this test fails if someone adds one.
        var methods = typeof(KycTelemetry)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name);

        Assert.DoesNotContain(methods, name =>
            name.Contains("Metric", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Counter", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dimension", StringComparison.OrdinalIgnoreCase));
    }
}
