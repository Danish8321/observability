using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// ADR-0023: with CouchDB the database risk lives in the URL rather than in
/// statement text. A bug here is a data leak, not a diagnostic inconvenience,
/// so the cases below are written from the shapes CouchDB actually produces.
/// </summary>
public class CouchDbUrlPolicyTests
{
    [Theory]
    [InlineData("http://couch:5984/kyc/abc-123", "http://couch:5984/kyc/{docid}")]
    [InlineData("http://couch:5984/kyc/0101901234", "http://couch:5984/kyc/{docid}")]
    public void Redact_replaces_document_identifiers(string input, string expected)
    {
        Assert.Equal(expected, CouchDbUrlPolicy.Redact(new Uri(input)));
    }

    [Fact]
    public void Redact_keeps_the_database_name()
    {
        // The database is structural, and losing it would make the span useless
        // for telling one dependency from another.
        var result = CouchDbUrlPolicy.Redact(new Uri("http://couch:5984/kyc/abc-123"));

        Assert.Contains("/kyc/", result);
    }

    [Fact]
    public void Redact_keeps_design_document_and_view_names()
    {
        // These are code identifiers. Redacting them would hide which query ran,
        // which is the entire diagnostic value of the span.
        var result = CouchDbUrlPolicy.Redact(
            new Uri("http://couch:5984/kyc/_design/applications/_view/by_status"));

        Assert.Equal("http://couch:5984/kyc/_design/applications/_view/by_status", result);
    }

    [Fact]
    public void Redact_removes_view_keys()
    {
        // A view key is very often the thing being looked up by, so this is the
        // case the carve-out exists for.
        var result = CouchDbUrlPolicy.Redact(
            new Uri("http://couch:5984/kyc/_design/a/_view/v?key=%220101901234%22"));

        Assert.Equal("http://couch:5984/kyc/_design/a/_view/v?key={value}", result);
        Assert.DoesNotContain("0101901234", result);
    }

    [Fact]
    public void Redact_removes_startkey_and_endkey()
    {
        var result = CouchDbUrlPolicy.Redact(
            new Uri("http://couch:5984/kyc/_all_docs?startkey=%22cpr-1%22&endkey=%22cpr-9%22"));

        Assert.DoesNotContain("cpr-1", result);
        Assert.DoesNotContain("cpr-9", result);
    }

    [Fact]
    public void Redact_keeps_structural_query_parameters()
    {
        // Paging shape is diagnostically useful and carries no data.
        var result = CouchDbUrlPolicy.Redact(
            new Uri("http://couch:5984/kyc/_all_docs?limit=50&skip=100&include_docs=true"));

        Assert.Equal("http://couch:5984/kyc/_all_docs?limit=50&skip=100&include_docs=true", result);
    }

    [Fact]
    public void Redact_keeps_couchdb_api_endpoints()
    {
        var result = CouchDbUrlPolicy.Redact(new Uri("http://couch:5984/kyc/_find"));

        Assert.Equal("http://couch:5984/kyc/_find", result);
    }

    [Fact]
    public void Redact_handles_a_document_identifier_containing_a_slash()
    {
        // CouchDB permits slashes in document IDs when escaped, and an
        // unescaped one would otherwise be read as an extra path segment.
        var result = CouchDbUrlPolicy.Redact(new Uri("http://couch:5984/kyc/a/b"));

        Assert.DoesNotContain("/a/", result);
        Assert.DoesNotContain("/b", result);
    }

    [Theory]
    [InlineData("http://couch:5984/kyc/_changes", true)]
    [InlineData("http://couch:5984/kyc/_changes?feed=continuous", true)]
    [InlineData("http://couch:5984/kyc/_find", false)]
    [InlineData("http://couch:5984/kyc/abc-123", false)]
    public void IsChangesFeed_identifies_the_long_poll(string url, bool expected)
    {
        // Traced as an ordinary span the continuous feed produces spans of
        // arbitrary duration that corrupt every latency percentile.
        Assert.Equal(expected, CouchDbUrlPolicy.IsChangesFeed(new Uri(url)));
    }
}
