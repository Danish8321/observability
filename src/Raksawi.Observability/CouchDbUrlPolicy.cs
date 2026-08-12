using System.Text;

namespace Raksawi.Observability;

/// <summary>
/// CouchDB is HTTP, so it arrives as ordinary client spans with no db.*
/// attributes. That makes it cheap to instrument and moves the entire database
/// risk into the URL — see ADR-0023.
/// </summary>
internal static class CouchDbUrlPolicy
{
    /// <summary>
    /// The continuous changes feed is a long-poll held open for minutes. Traced
    /// as an ordinary client span it produces spans of arbitrary duration that
    /// corrupt every latency percentile computed from span data.
    /// </summary>
    public static bool IsChangesFeed(Uri uri) =>
        uri.AbsolutePath.EndsWith("/_changes", StringComparison.Ordinal);

    /// <summary>
    /// Replaces document identifiers and view keys with placeholders, keeping
    /// the shape of the request — which is the diagnostically useful part —
    /// and discarding the values, which are where identity can appear.
    /// </summary>
    /// <remarks>
    /// <c>GET /kyc/abc-123</c> becomes <c>GET /kyc/{docid}</c>.
    /// <c>GET /kyc/_design/d/_view/v?key="x"</c> becomes
    /// <c>GET /kyc/_design/d/_view/v?key={value}</c>.
    /// </remarks>
    public static string Redact(Uri uri)
    {
        var builder = new StringBuilder();

        // GetLeftPart(Authority) includes userinfo verbatim (scheme://user:pass@host).
        // Rebuild without it rather than trust callers never put credentials in
        // the URL — HTTP allows it, and this policy is the only backstop.
        builder.Append(uri.Scheme);
        builder.Append("://");
        builder.Append(uri.Authority);
        builder.Append(RedactPath(uri.AbsolutePath));

        if (!string.IsNullOrEmpty(uri.Query))
        {
            builder.Append(RedactQuery(uri.Query));
        }

        return builder.ToString();
    }

    private static string RedactPath(string path)
    {
        var segments = path.Split('/');
        var result = new StringBuilder();

        // Segment 0 is empty (leading slash), segment 1 is the database name,
        // which is structural and safe. Anything after that is either a CouchDB
        // API endpoint (leading underscore) or a document identifier.
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];

            if (i > 0)
            {
                result.Append('/');
            }

            if (i <= 1 || segment.Length == 0 || segment[0] == '_')
            {
                result.Append(segment);
                continue;
            }

            // A design document name follows _design and a view name follows
            // _view. Both are code identifiers, not data.
            var previous = segments[i - 1];
            if (previous is "_design" or "_view")
            {
                result.Append(segment);
                continue;
            }

            result.Append("{docid}");
        }

        return result.ToString();
    }

    private static string RedactQuery(string query)
    {
        var result = new StringBuilder("?");
        var pairs = query.TrimStart('?').Split('&');

        for (var i = 0; i < pairs.Length; i++)
        {
            if (i > 0)
            {
                result.Append('&');
            }

            var separator = pairs[i].IndexOf('=');
            if (separator < 0)
            {
                result.Append(pairs[i]);
                continue;
            }

            var name = pairs[i].Substring(0, separator);
            result.Append(name);
            result.Append('=');

            // Paging and shaping parameters are structural. Everything else is
            // treated as a lookup value, because a view key very often is the
            // thing being looked up by.
            result.Append(IsStructural(name) ? pairs[i].Substring(separator + 1) : "{value}");
        }

        return result.ToString();
    }

    private static bool IsStructural(string name) => name is
        "limit" or "skip" or "descending" or "include_docs" or
        "reduce" or "group" or "group_level" or "stale" or "update_seq" or "conflicts";
}
