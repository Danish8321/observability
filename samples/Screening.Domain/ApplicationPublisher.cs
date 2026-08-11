using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using Raksawi.Observability.Kyc;

namespace Screening.Domain;

/// <summary>
/// Publishes work onto NATS.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here injects <c>traceparent</c>.</b> NATS.Net ≥ 3.0.1 writes trace
/// context into message headers itself, and hand-injection produces duplicate
/// or conflicting headers. If you find propagation code in a service, delete it
/// and check the client version.
/// </para>
/// <para>
/// 🔒 The subject is <c>kyc.applications.submitted</c> — a routing name with no
/// identity in it. A subject such as <c>kyc.applications.{applicationId}</c>
/// would leak into NATS monitoring endpoints, connection logs, and metric
/// dimensions, none of which the collector redaction reaches.
/// </para>
/// </remarks>
public sealed class ApplicationPublisher(
    INatsConnection nats,
    ILogger<ApplicationPublisher> logger)
{
    public const string Subject = "kyc.applications.submitted";

    public async Task PublishAsync(
        string applicationId, string correlationId, CancellationToken cancellationToken)
    {
        // PRODUCER kind, not Internal. The span kind is what lets a store draw
        // the producer/consumer relationship rather than two unrelated spans.
        using var activity = ScreeningTelemetry.Source.StartActivity(
            $"{Subject} publish", ActivityKind.Producer);

        var messageId = Guid.NewGuid().ToString("n");

        KycTelemetry.SetApplicationId(applicationId);
        KycTelemetry.SetCorrelationId(correlationId);

        // Semantic convention names, so a store's messaging views work without
        // per-service configuration.
        activity?.SetTag("messaging.system", "nats");
        activity?.SetTag("messaging.destination.name", Subject);
        activity?.SetTag("messaging.message.id", messageId);

        var message = new ApplicationSubmitted(applicationId, correlationId, messageId);

        await nats.PublishAsync(Subject, message, cancellationToken: cancellationToken);

        logger.LogInformation(
            "Published {ApplicationId} as message {MessageId}", applicationId, messageId);
    }
}
