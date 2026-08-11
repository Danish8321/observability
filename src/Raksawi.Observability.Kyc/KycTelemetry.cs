using System.Diagnostics;

namespace Raksawi.Observability.Kyc;

/// <summary>
/// The governed helper for KYC vocabulary. Application code sets business
/// identifiers through this and never through <c>Activity.SetTag</c> directly.
/// </summary>
/// <remarks>
/// <para>
/// This exists so that the rules below are enforced by the only available API
/// rather than by review. ADR-0001 puts it in a separate package from the
/// mechanism layer so that non-KYC services can adopt telemetry without
/// inheriting KYC vocabulary.
/// </para>
/// <para><b>The three rules that are not negotiable:</b></para>
/// <list type="number">
///   <item>
///     A <see cref="DataClass.OpaqueBusinessIdentifier"/> is never a metric
///     dimension. Metrics are pre-aggregated per unique dimension combination,
///     so an unbounded one produces unbounded series and degrades the store for
///     everyone. This is a cost and availability rule, not a privacy one — and
///     it is why <see cref="SetApplicationId"/> touches spans only.
///   </item>
///   <item>
///     NATS subjects never encode identity. A subject is a routing name and
///     ends up in metric dimensions, connection logs, and monitoring endpoints
///     where redaction does not reach.
///   </item>
///   <item>
///     Baggage stays disabled. It propagates to every downstream system at
///     once, which makes enabling it a governance change rather than a
///     configuration one.
///   </item>
/// </list>
/// </remarks>
public static class KycTelemetry
{
    /// <summary>
    /// The application reference. Class 2: opaque, and the single most useful
    /// attribute for reconstructing an incident.
    /// </summary>
    public const string ApplicationIdKey = "application.id";

    /// <summary>
    /// Workflow correlation, minted once at workflow start and carried to the
    /// end — ADR-0007. Distinct from a trace: a workflow spans many traces, and
    /// distinct from a session, which is one browser page-load lifetime.
    /// </summary>
    public const string CorrelationIdKey = "correlation.id";

    /// <summary>Browser-minted session, seeded at page load — ADR-0007.</summary>
    public const string SessionIdKey = "session.id";

    /// <summary>The message that caused this work — ADR-0007.</summary>
    public const string CausationIdKey = "causation.id";

    /// <summary>
    /// Attaches the application reference to the current span.
    /// </summary>
    /// <remarks>
    /// Deliberately no metric equivalent exists. If you want to count
    /// applications, count them without the identifier as a dimension and use
    /// the trace store to answer "which ones".
    /// </remarks>
    public static void SetApplicationId(string? applicationId) =>
        SetOnCurrentActivity(ApplicationIdKey, applicationId);

    /// <summary>Attaches the workflow correlation to the current span.</summary>
    public static void SetCorrelationId(string? correlationId) =>
        SetOnCurrentActivity(CorrelationIdKey, correlationId);

    /// <summary>Attaches the browser session to the current span.</summary>
    public static void SetSessionId(string? sessionId) =>
        SetOnCurrentActivity(SessionIdKey, sessionId);

    /// <summary>
    /// Attaches the identifier of the message that caused this work. Set on the
    /// consuming side, so that a redelivery is distinguishable from a retry and
    /// a fan-out is distinguishable from a loop.
    /// </summary>
    public static void SetCausationId(string? causationId) =>
        SetOnCurrentActivity(CausationIdKey, causationId);

    /// <summary>
    /// Mints a workflow correlation identifier. Called once, where the workflow
    /// begins — not per request and not per message.
    /// </summary>
    public static string NewCorrelationId() => Guid.NewGuid().ToString("n");

    private static void SetOnCurrentActivity(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // No-op when nothing is sampled or nothing is listening. Telemetry must
        // never be able to fail business work (Rev 3 I3.6).
        Activity.Current?.SetTag(key, value);
    }
}
