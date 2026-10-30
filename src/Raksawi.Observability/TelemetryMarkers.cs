namespace Raksawi.Observability;

/// <summary>
/// Names of the <see cref="System.Diagnostics.Activity"/> custom properties
/// one component leaves for another to verify. Never tags: a marker is
/// evidence for an in-process check and is never exported.
/// </summary>
/// <remarks>
/// Convention <c>raksawi.marker.&lt;subject&gt;.&lt;past-participle&gt;</c>.
/// New markers only by review of this file.
/// </remarks>
internal static class TelemetryMarkers
{
    /// <summary>
    /// The CouchDB URL the HttpClient hook wrote to <c>url.full</c> — the value
    /// itself, not a flag, so a later rewrite is detectable (C5).
    /// </summary>
    internal const string UrlRedacted = "raksawi.marker.url.redacted";
}
