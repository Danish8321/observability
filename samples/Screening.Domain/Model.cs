using System.Text.Json.Serialization;

namespace Screening.Domain;

/// <summary>What the caller submits.</summary>
/// <remarks>
/// 🔒 <see cref="Applicant"/> is Class 3. It is stored, and it is never
/// attached to telemetry — no span tag, no log property, no metric dimension.
/// The identifier is what telemetry carries.
/// </remarks>
public sealed record ApplicationRequest(
    [property: JsonPropertyName("applicationId")] string ApplicationId,
    [property: JsonPropertyName("applicant")] string Applicant);

/// <summary>The CouchDB document.</summary>
public sealed record ApplicationDocument
{
    // WhenWritingNull: CouchDB rejects a PUT whose body carries "_id": null —
    // the document id must come from the URL alone on first write.
    [JsonPropertyName("_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("_rev")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Revision { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = ApplicationStatus.Received;

    /// <summary>🔒 Class 3. Stored, never emitted.</summary>
    [JsonPropertyName("applicant")]
    public string? Applicant { get; init; }

    /// <summary>
    /// Carried on the document so that a workflow remains reconstructable after
    /// its traces have aged out of the telemetry store — ADR-0007. Retention
    /// for telemetry is short; retention for business records is not.
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; init; }

    [JsonPropertyName("attempts")]
    public int Attempts { get; init; }
}

public static class ApplicationStatus
{
    public const string Received = "received";
    public const string Screening = "screening";
    public const string Screened = "screened";
    public const string Abandoned = "abandoned";
}

/// <summary>The screening decision.</summary>
public enum ScreeningOutcome
{
    Clear,
    Referred,
}

/// <summary>
/// The message published when an application is submitted.
/// </summary>
/// <remarks>
/// <para>
/// Carries <see cref="CorrelationId"/> and <see cref="MessageId"/> explicitly.
/// Trace context travels in NATS headers and is handled by the client, but a
/// workflow correlation is business data with a longer lifetime than a trace,
/// so it travels in the payload — ADR-0007.
/// </para>
/// <para>
/// 🔒 No applicant data. A message body is telemetry-adjacent: it reaches
/// dead-letter stores, replay tooling, and debugging output.
/// </para>
/// </remarks>
public sealed record ApplicationSubmitted(
    [property: JsonPropertyName("applicationId")] string ApplicationId,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("messageId")] string MessageId);
