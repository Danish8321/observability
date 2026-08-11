using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Screening.Domain;

/// <summary>
/// CouchDB access. Plain <see cref="HttpClient"/> — CouchDB is HTTP/JSON, so
/// the standard HTTP instrumentation already produces client spans and no
/// database instrumentation package is involved (ADR-0023).
/// </summary>
/// <remarks>
/// 🔒 That convenience is also the risk. <c>GET /kyc/{docid}</c> puts the
/// document identifier in <c>url.full</c>, so the redaction configured through
/// <c>CouchDbHosts</c> is what stands between an identifier and the telemetry
/// store. Verify it on a real span rather than assuming it.
/// </remarks>
public sealed class ApplicationRepository(
    HttpClient client,
    ILogger<ApplicationRepository> logger)
{
    private const string Database = "kyc";

    public async Task<ApplicationDocument?> GetAsync(string id, CancellationToken cancellationToken)
    {
        // A manual span around a call that is already instrumented, because the
        // HTTP span says "a GET happened" and this one says what it was for.
        // The distinction matters when a route makes six CouchDB calls.
        using var activity = ScreeningTelemetry.Source.StartActivity(
            "application.load", ActivityKind.Internal);

        activity?.SetTag("couchdb.database", Database);

        var response = await client.GetAsync(DocumentPath(id), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Not an error. Recording it as one trains people to ignore errors.
            activity?.SetTag("application.found", false);
            return null;
        }

        response.EnsureSuccessStatusCode();
        activity?.SetTag("application.found", true);

        return await response.Content.ReadFromJsonAsync<ApplicationDocument>(cancellationToken);
    }

    /// <summary>
    /// Writes a document, tolerating the conflict that concurrent writers cause.
    /// </summary>
    /// <returns>False when the write lost a conflict and should be retried.</returns>
    public async Task<bool> SaveAsync(
        string id, ApplicationDocument document, CancellationToken cancellationToken)
    {
        using var activity = ScreeningTelemetry.Source.StartActivity(
            "application.save", ActivityKind.Internal);

        activity?.SetTag("couchdb.database", Database);
        activity?.SetTag("application.status", document.Status);

        var response = await client.PutAsJsonAsync(
            DocumentPath(id), document, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // A CouchDB conflict is an expected outcome under concurrency, not
            // a fault. Marked on the span so it is countable, and deliberately
            // not thrown — the caller decides whether to retry.
            activity?.SetTag("couchdb.conflict", true);

            // Structured, not interpolated. ADR-0004 bans interpolation because
            // an interpolated string cannot be scanned or filtered once it has
            // become one opaque line.
            logger.LogWarning(
                "CouchDB write conflict for {ApplicationId}", id);

            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// Escaped, because a document identifier is user-influenced and an
    /// unescaped one changes the shape of the URL.
    /// </summary>
    private static string DocumentPath(string id) =>
        $"/{Database}/{Uri.EscapeDataString(id)}";
}
