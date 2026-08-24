using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// ADR-0003 drops any attribute whose key is not allowlisted, which makes the
/// contents load-bearing in both directions: an incomplete list silently
/// deletes legitimate telemetry, and an over-broad one defeats the control.
/// The carve-out cases below are the compliance argument (ADR-0018) and are
/// written as what must never be exported, not as coverage.
/// </summary>
public class AttributeAllowlistTests
{
    private static readonly AttributeAllowlist Allowlist =
        AttributeAllowlist.ForDeclaredKeys("correlation.id", "application.id");

    [Theory]
    [InlineData("http.request.method")]
    [InlineData("http.response.status_code")]
    [InlineData("server.address")]
    [InlineData("network.protocol.name")]
    [InlineData("messaging.system")]
    [InlineData("code.function.name")]
    [InlineData("service.name")]
    [InlineData("telemetry.sdk.name")]
    [InlineData("user_agent.original")]
    [InlineData("exception.type")]
    public void Allows_the_declared_families(string key)
    {
        Assert.True(Allowlist.IsAllowed(key, isCouchDbSpan: false));
    }

    [Theory]
    [InlineData("http.request.header.authorization")]   // 🔒 Class 4
    [InlineData("http.response.header.set-cookie")]     // 🔒 Class 4
    [InlineData("db.query.text")]                       // 🔒 Class 3
    [InlineData("db.statement")]                        // 🔒 Class 3, pre-rename form
    [InlineData("db.query.parameter.applicant_id")]     // 🔒 Class 3, row data
    public void Denies_the_carve_outs_even_though_their_family_is_allowed(string key)
    {
        // Each of these sits inside an allowed family (http.*, db.*). The
        // family allow is broad by intent; this is what makes it safe.
        Assert.False(Allowlist.IsAllowed(key, isCouchDbSpan: false));
        Assert.False(Allowlist.IsAllowed(key, isCouchDbSpan: true));
    }

    [Theory]
    [InlineData("url.full")]
    [InlineData("url.query")]
    public void Denies_url_identifiers_on_an_ordinary_span(string key)
    {
        // Class 3 where routes encode identifiers — Rev 3 D0.4b.
        Assert.False(Allowlist.IsAllowed(key, isCouchDbSpan: false));
    }

    [Theory]
    [InlineData("url.full")]
    [InlineData("url.query")]
    public void Allows_url_identifiers_on_a_CouchDb_span(string key)
    {
        // ADR-0023: with CouchDB the URL *is* the database surface, and
        // CouchDbUrlPolicy has already replaced the document ID and view key
        // with placeholders by the time this runs. Dropping it here would
        // discard the entire diagnostic value of the span.
        Assert.True(Allowlist.IsAllowed(key, isCouchDbSpan: true));
    }

    [Fact]
    public void Allows_a_declared_class_two_key()
    {
        Assert.True(Allowlist.IsAllowed("application.id", isCouchDbSpan: false));
        Assert.True(Allowlist.IsAllowed("correlation.id", isCouchDbSpan: false));
    }

    [Theory]
    [InlineData("applicantIdentifier")]
    [InlineData("customer_cpr")]
    [InlineData("tenant.id")]
    public void Denies_anything_undeclared_and_outside_every_family(string key)
    {
        // The reason ADR-0003 chose an allowlist over a deny-list: nobody
        // predicted "applicantIdentifier", and a deny-list would pass it.
        // tenant.id is here deliberately — ADR-0011 withheld it, and an
        // undeclared key is dropped rather than quietly emitted.
        Assert.False(Allowlist.IsAllowed(key, isCouchDbSpan: false));
    }

    [Fact]
    public void A_declared_key_is_matched_exactly_and_never_as_a_prefix()
    {
        // Class 2 keys are enumerated individually (ADR-0018). If a
        // declaration behaved as a prefix, declaring application.id would
        // silently allow application.id.applicant_name.
        var allowlist = AttributeAllowlist.ForDeclaredKeys("application.id");

        Assert.False(allowlist.IsAllowed("application.id.applicant_name", isCouchDbSpan: false));
    }
}
