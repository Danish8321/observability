using Raksawi.Observability.Kyc;

namespace Screening.Api;

/// <summary>
/// Establishes the two identifiers that the trace itself cannot supply —
/// ADR-0007.
/// </summary>
/// <remarks>
/// <para>
/// Three lifetimes are in play and they are routinely confused:
/// </para>
/// <list type="bullet">
///   <item><c>trace_id</c> — one request. Minted by the SDK, ends at the response</item>
///   <item><c>session.id</c> — one browser page-load lifetime. Minted by the browser</item>
///   <item><c>correlation.id</c> — one business workflow, which may span days,
///     several sessions, and many traces. Minted where the workflow starts</item>
/// </list>
/// <para>
/// Collapsing these into one identifier is the mistake this middleware exists
/// to prevent: a workflow correlation that restarts per request cannot correlate
/// a workflow, and a session identifier that outlives the browser is not a
/// session.
/// </para>
/// </remarks>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string SessionHeader = "X-Session-Id";
    public const string CorrelationHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        // Browser-minted. Absent for server-to-server callers, which is
        // expected rather than an error.
        if (context.Request.Headers.TryGetValue(SessionHeader, out var session))
        {
            KycTelemetry.SetSessionId(session.ToString());
        }

        // Continued if supplied, minted if this is where the workflow begins.
        var correlationId = context.Request.Headers.TryGetValue(CorrelationHeader, out var supplied)
            && !string.IsNullOrWhiteSpace(supplied.ToString())
                ? supplied.ToString()
                : KycTelemetry.NewCorrelationId();

        KycTelemetry.SetCorrelationId(correlationId);

        // Stored on the context so handlers use the same value rather than
        // reaching for the header again and disagreeing about defaults.
        context.Items[CorrelationHeader] = correlationId;

        // Echoed back so a caller can quote it in a support ticket. This is the
        // identifier a human will actually paste into a search box.
        context.Response.Headers[CorrelationHeader] = correlationId;

        await next(context);
    }
}

public static class CorrelationMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelation(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationMiddleware>();

    public static string CorrelationId(this HttpContext context) =>
        context.Items[CorrelationMiddleware.CorrelationHeader] as string ?? string.Empty;
}
