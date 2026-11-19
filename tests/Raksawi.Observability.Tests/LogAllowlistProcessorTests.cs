using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// The log half of ADR-0003's second enforcement point. Driven through a real
/// <see cref="ILogger"/> and a real provider rather than by calling
/// <c>OnEnd</c> directly: the thing worth proving is that a structured log
/// property written the way services write them is filtered before export.
/// </summary>
public sealed class LogAllowlistProcessorTests
{
    [Fact]
    public void A_declared_class_2_key_survives_on_a_log_record()
    {
        var attributes = Log(logger =>
            logger.LogInformation("Screened {application.id}", "app-1"));

        Assert.Contains(attributes, a => a.Key == "application.id");
    }

    [Fact]
    public void An_undeclared_property_is_dropped()
    {
        // The shape a hand-rolled PII property actually takes: no family
        // covers it, no pack declared it, and before this processor existed the
        // collector was the only thing that would have stopped it.
        var attributes = Log(logger =>
            logger.LogInformation("Screening {applicantIdentifier}", "0301901234"));

        Assert.DoesNotContain(attributes, a => a.Key == "applicantIdentifier");
    }

    [Fact]
    public void A_carve_out_inside_an_allowed_family_is_dropped()
    {
        var attributes = Log(logger =>
            logger.LogInformation("Query {db.statement}", "SELECT * FROM applicants"));

        Assert.DoesNotContain(attributes, a => a.Key == "db.statement");
    }

    [Fact]
    public void The_couchdb_carve_out_does_not_apply_to_a_log_record()
    {
        // 🔒 url.full survives on a span to a known CouchDB host because
        // CouchDbUrlPolicy redacted it first. Nothing redacts a URL on a log
        // record, so the exemption has no precondition here and the key is
        // denied — the same direction the collector takes.
        var attributes = Log(logger =>
            logger.LogInformation("Fetched {url.full}", "http://couchdb:5984/kyc/app-1"));

        Assert.DoesNotContain(attributes, a => a.Key == "url.full");
    }

    [Fact]
    public void An_allowed_family_member_survives_alongside_a_dropped_key()
    {
        // Both in one record, because the processor rebuilds the attribute list
        // on the first drop — the copy has to keep what came before it.
        var attributes = Log(logger =>
            logger.LogInformation(
                "Called {http.request.method} for {applicantIdentifier}", "POST", "0301901234"));

        Assert.Contains(attributes, a => a.Key == "http.request.method");
        Assert.DoesNotContain(attributes, a => a.Key == "applicantIdentifier");
    }

    // ADR-0043: a service that has not opted into governance.

    [Fact]
    public void Without_governance_an_undeclared_property_survives()
    {
        // What circle needed: its own fields, with no vocabulary declared.
        var attributes = Log(
            logger => logger.LogInformation("Posted {EntryId} to {AccountId}", "e-1", "acc-1"),
            LogAllowlistProcessor.ForDenylist([]));

        Assert.Contains(attributes, a => a.Key == "EntryId");
        Assert.Contains(attributes, a => a.Key == "AccountId");
    }

    [Theory]
    [InlineData("To")]
    [InlineData("Body")]
    [InlineData("api_key")]
    [InlineData("Set-Cookie")]
    public void Without_governance_an_estate_denylisted_name_is_dropped(string name)
    {
        var attributes = Log(
            logger => logger.LogInformation("Sent {" + name + "} for {AccountId}", "x", "acc-1"),
            LogAllowlistProcessor.ForDenylist([]));

        Assert.DoesNotContain(attributes, a => a.Key == name);
        Assert.Contains(attributes, a => a.Key == "AccountId");
    }

    [Fact]
    public void Without_governance_the_services_own_denied_keys_are_dropped_ignoring_case_and_separators()
    {
        var attributes = Log(
            logger => logger.LogInformation("Provider said {RawStatus} for {AccountId}", "x", "acc-1"),
            LogAllowlistProcessor.ForDenylist(["raw_status"]));

        Assert.DoesNotContain(attributes, a => a.Key == "RawStatus");
        Assert.Contains(attributes, a => a.Key == "AccountId");
    }

    [Theory]
    [InlineData("db.statement")]
    [InlineData("db.query.text")]
    [InlineData("url.full")]
    [InlineData("http.request.header.authorization")]
    public void Without_governance_the_carve_outs_still_hold(string key)
    {
        var attributes = Log(
            logger => logger.LogInformation("Value {" + key + "}", "x"),
            LogAllowlistProcessor.ForDenylist([]));

        Assert.DoesNotContain(attributes, a => a.Key == key);
    }

    [Fact]
    public void Governance_required_keeps_the_allowlist()
    {
        var options = new RaksawiObservabilityOptions { GovernanceRequired = true };

        var attributes = Log(
            logger => logger.LogInformation("Posted {EntryId}", "e-1"),
            LogAllowlistProcessor.For(options));

        Assert.DoesNotContain(attributes, a => a.Key == "EntryId");
    }

    [Fact]
    public void Governance_not_required_uses_the_options_denied_keys()
    {
        var options = new RaksawiObservabilityOptions();
        options.DeniedLogKeys.Add("ProviderDetail");

        var attributes = Log(
            logger => logger.LogInformation("{ProviderDetail} for {EntryId}", "x", "e-1"),
            LogAllowlistProcessor.For(options));

        Assert.DoesNotContain(attributes, a => a.Key == "ProviderDetail");
        Assert.Contains(attributes, a => a.Key == "EntryId");
    }

    /// <summary>
    /// Writes one log record through a provider carrying the filter (the
    /// allowlist over <c>application.id</c> unless another is given), and
    /// returns the attributes as the exporter saw them.
    /// </summary>
    private static List<KeyValuePair<string, object?>> Log(
        Action<ILogger> write, LogAllowlistProcessor? filter = null)
    {
        var exported = new List<KeyValuePair<string, object?>>();

        using (var factory = LoggerFactory.Create(builder => builder.AddOpenTelemetry(options =>
        {
            options.AddProcessor(filter ?? new LogAllowlistProcessor(
                AttributeAllowlist.ForDeclaredKeys("application.id")));

            // Simple, not batching: a batched record is exported after the test
            // has already read the list.
            options.AddProcessor(new SimpleLogRecordExportProcessor(new CollectingLogExporter(exported)));
        })))
        {
            write(factory.CreateLogger("Test"));
        }

        return exported;
    }

    /// <summary>
    /// Copies out what it was handed. <see cref="LogRecord"/> instances are
    /// pooled and reused after export, so holding one and reading it later
    /// reads whatever the pool did next.
    /// </summary>
    private sealed class CollectingLogExporter(List<KeyValuePair<string, object?>> exported)
        : BaseExporter<LogRecord>
    {
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (var record in batch)
            {
                if (record.Attributes is null)
                {
                    continue;
                }

                exported.AddRange(record.Attributes);
            }

            return ExportResult.Success;
        }
    }
}
