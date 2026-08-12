using System.Diagnostics;
using NATS.Client.Core;
using Raksawi.Observability.Kyc;
using Screening.Domain;

namespace Screening.Worker;

/// <summary>
/// Consumes submitted applications, screens them, and retries or abandons.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing reads <c>traceparent</c> here.</b> NATS.Net extracts trace context
/// from message headers and links the consumer span to the producer span. The
/// span below is a child of that link, not a new root.
/// </para>
/// <para>
/// The retry loop is in-process and bounded. Core NATS gives no redelivery, so
/// exhausting it means the work is gone — which is deliberate for the demo and
/// wrong for production. See the note in the sample README about JetStream.
/// </para>
/// </remarks>
internal sealed class ScreeningConsumer(
    INatsConnection nats,
    IServiceScopeFactory scopeFactory,
    ILogger<ScreeningConsumer> logger) : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in nats.SubscribeAsync<ApplicationSubmitted>(
            ApplicationPublisher.Subject, cancellationToken: stoppingToken))
        {
            if (message.Data is null)
            {
                continue;
            }

            await HandleAsync(message, stoppingToken);
        }
    }

    private async Task HandleAsync(NatsMsg<ApplicationSubmitted> message, CancellationToken cancellationToken)
    {
        var submitted = message.Data!;

        // CONSUMER kind, explicitly parented to the producer's span via the
        // traceparent NATS.Net wrote into the message headers. StartActivity
        // alone does not do this — it reads Activity.Current, which is only
        // live for the duration of MoveNextAsync producing this item off the
        // async-enumerable and has already ended by the time this method
        // runs. Without an explicit parent context the activity below starts
        // a disconnected new root instead of joining the producer's trace.
        var parentContext = message.Headers is null
            ? default
            : NatsMsgTelemetryExtensions.GetActivityContext(message.Headers);

        using var activity = ScreeningTelemetry.Source.StartActivity(
            $"{ApplicationPublisher.Subject} process", ActivityKind.Consumer, parentContext);

        KycTelemetry.SetApplicationId(submitted.ApplicationId);
        KycTelemetry.SetCorrelationId(submitted.CorrelationId);

        // Causation is the message that caused this work — ADR-0007. It is what
        // separates a redelivery (same message.id, new trace) from a retry
        // (same trace, new attempt). Without it those are indistinguishable and
        // duplicate-looking data gets dismissed as a bug in the telemetry.
        KycTelemetry.SetCausationId(submitted.MessageId);

        activity?.SetTag("messaging.system", "nats");
        activity?.SetTag("messaging.destination.name", ApplicationPublisher.Subject);
        activity?.SetTag("messaging.message.id", submitted.MessageId);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // Bounded: MaxAttempts values, so it is safe as a span attribute
            // and would still be wrong as a metric dimension combined with
            // anything unbounded.
            activity?.SetTag("messaging.attempt", attempt);

            try
            {
                // A scope per attempt, so each retry gets a fresh repository
                // and a fresh HttpClient from the factory's rotation.
                using var scope = scopeFactory.CreateScope();
                var screening = scope.ServiceProvider.GetRequiredService<ScreeningService>();

                await screening.ScreenAsync(submitted, cancellationToken);
                return;
            }
            catch (ApplicationNotFoundException)
            {
                // Not retryable. Retrying a missing document just delays the
                // same answer three times and triples the noise.
                Abandon(activity, submitted, "not_found");
                return;
            }
            catch (Exception ex) when (
                ex is ScreeningProviderException or WriteConflictException
                && attempt < MaxAttempts)
            {
                // Retryable. Recorded as an event rather than an error status:
                // the operation has not failed yet, and marking it red here
                // would make the error rate meaningless.
                activity?.AddEvent(new ActivityEvent(
                    "screening.retry",
                    tags: new ActivityTagsCollection
                    {
                        { "attempt", attempt },
                        { "reason", ex.GetType().Name },
                    }));

                logger.LogWarning(
                    "Screening attempt {Attempt} failed for {ApplicationId}, retrying",
                    attempt, submitted.ApplicationId);

                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);

                Abandon(activity, submitted, ex.GetType().Name);

                logger.LogError(
                    ex,
                    "Screening failed permanently for {ApplicationId}",
                    submitted.ApplicationId);

                return;
            }
        }
    }

    /// <summary>
    /// Records that work will not complete.
    /// </summary>
    /// <remarks>
    /// This counter is the demo's punchline in metric form: it is the only
    /// signal that distinguishes "nothing was submitted" from "something was
    /// submitted and silently never finished". The API already returned 202.
    /// </remarks>
    private static void Abandon(Activity? activity, ApplicationSubmitted submitted, string reason)
    {
        activity?.SetTag("screening.abandoned", true);
        activity?.SetTag("screening.abandon_reason", reason);

        // Dimensioned by reason only — bounded, and never by application.id.
        ScreeningTelemetry.Abandoned.Add(1,
            new KeyValuePair<string, object?>("reason", reason));

        _ = submitted;
    }
}
