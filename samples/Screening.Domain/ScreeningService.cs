using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Raksawi.Observability.Kyc;

namespace Screening.Domain;

/// <summary>
/// The screening decision. This is where the demo's faults are injected, and
/// where the instrumentation patterns worth copying live.
/// </summary>
public sealed class ScreeningService(
    ApplicationRepository repository,
    ILogger<ScreeningService> logger)
{
    /// <summary>
    /// Screens one application.
    /// </summary>
    /// <remarks>
    /// The span here is the unit a human reasons about — "screening this
    /// application" — rather than a technical operation. Spans named after what
    /// the business was doing are what make a trace readable by someone who did
    /// not write the code.
    /// </remarks>
    public async Task<ScreeningOutcome> ScreenAsync(
        ApplicationSubmitted message, CancellationToken cancellationToken)
    {
        using var activity = ScreeningTelemetry.Source.StartActivity(
            "screen application", ActivityKind.Internal);

        // Class 2 identifiers, on the span only. Set through the governed
        // helper so the metric-dimension rule cannot be broken by accident.
        KycTelemetry.SetApplicationId(message.ApplicationId);
        KycTelemetry.SetCorrelationId(message.CorrelationId);
        KycTelemetry.SetCausationId(message.MessageId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var outcome = await DecideAsync(message, cancellationToken);

            activity?.SetTag("screening.outcome", outcome.ToString().ToLowerInvariant());

            // Bounded dimension. "clear" or "referred", forever.
            ScreeningTelemetry.Screened.Add(1,
                new KeyValuePair<string, object?>("outcome", outcome.ToString().ToLowerInvariant()));

            return outcome;
        }
        catch (Exception ex)
        {
            // Status first: it is what makes the span red in every store, and
            // it is what an error-rate query counts.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            // The exception is recorded. ADR-0004 keeps RecordException enabled
            // for application code — the SQL carve-out that once qualified this
            // does not apply, since there is no SQL client (ADR-0023).
            activity?.AddException(ex);

            throw;
        }
        finally
        {
            stopwatch.Stop();
            ScreeningTelemetry.Duration.Record(stopwatch.Elapsed.TotalSeconds);
        }
    }

    private async Task<ScreeningOutcome> DecideAsync(
        ApplicationSubmitted message, CancellationToken cancellationToken)
    {
        var document = await repository.GetAsync(message.ApplicationId, cancellationToken)
            ?? throw new ApplicationNotFoundException(message.ApplicationId);

        // ---- Demo fault injection (QD1b) ----------------------------------
        // Keyed off the identifier so faults fire live from a request, with no
        // redeploy. Delete this block before the code goes anywhere real.

        if (message.ApplicationId.Contains("slow", StringComparison.OrdinalIgnoreCase))
        {
            using var slow = ScreeningTelemetry.Source.StartActivity(
                "sanctions list lookup", ActivityKind.Client);

            slow?.SetTag("screening.provider", "sanctions-list");

            // Latency occurs here and is noticed three services away. Naming
            // the span is what turns "the system is slow" into "this call is".
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        if (message.ApplicationId.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScreeningProviderException(
                "Sanctions list provider returned an error");
        }

        // -------------------------------------------------------------------

        var outcome = document.Applicant?.Length % 2 == 0
            ? ScreeningOutcome.Clear
            : ScreeningOutcome.Referred;

        var updated = document with
        {
            Status = ApplicationStatus.Screened,
            Outcome = outcome.ToString().ToLowerInvariant(),
            CorrelationId = message.CorrelationId,
        };

        if (!await repository.SaveAsync(message.ApplicationId, updated, cancellationToken))
        {
            throw new WriteConflictException(message.ApplicationId);
        }

        // Structured properties. The application identifier is a property
        // rather than part of the message text, so it stays queryable and stays
        // subject to the same governance as a span attribute.
        logger.LogInformation(
            "Screened {ApplicationId} with outcome {Outcome}",
            message.ApplicationId,
            outcome);

        return outcome;
    }
}

/// <summary>Thrown when the document the message refers to does not exist.</summary>
public sealed class ApplicationNotFoundException(string applicationId)
    : Exception($"Application {applicationId} not found")
{
    public string ApplicationId { get; } = applicationId;
}

/// <summary>Thrown when the screening provider fails. Retryable.</summary>
public sealed class ScreeningProviderException(string message) : Exception(message);

/// <summary>Thrown when a CouchDB write lost a conflict. Retryable.</summary>
public sealed class WriteConflictException(string applicationId)
    : Exception($"Write conflict for {applicationId}")
{
    public string ApplicationId { get; } = applicationId;
}
