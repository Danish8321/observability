using Microsoft.Extensions.Configuration;
using OpenTelemetry.Resources;
using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// How a service ends up on the log allowlist or the denylist (ADR-0043):
/// configuration, the KYC pack's marker, and the resource tag the collector
/// reads.
/// </summary>
public sealed class GovernanceTests
{
    private static RaksawiObservabilityOptions Configure(params (string Key, string Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();
        var options = new RaksawiObservabilityOptions();

        ObservabilityConfiguration.Apply(options, configuration);
        return options;
    }

    [Fact]
    public void Nothing_configured_is_the_denylist()
    {
        var options = Configure();

        Assert.False(options.GovernanceRequired);
        Assert.Empty(options.DeniedLogKeys);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void Governance_required_is_read_from_configuration(string value, bool required)
    {
        Assert.Equal(required, Configure(("Governance:Required", value)).GovernanceRequired);
    }

    [Fact]
    public void An_unparsable_governance_value_fails_naming_the_key()
    {
        // "yes" read as false would put a regulated service on the denylist.
        var error = Assert.Throws<InvalidOperationException>(() => Configure(("Governance:Required", "yes")));

        Assert.Contains("Governance:Required", error.Message);
    }

    [Fact]
    public void Denied_keys_are_read_from_configuration()
    {
        var options = Configure(
            ("Observability:Logs:DeniedKeys:0", "RawStatus"),
            ("Observability:Logs:DeniedKeys:1", " ProviderDetail "));

        Assert.Equal(["RawStatus", "ProviderDetail"], options.DeniedLogKeys);
    }

    [Fact]
    public void The_kyc_pack_requires_governance()
    {
        // A KYC service can't forget the flag: the pack sets it (ADR-0043).
        Assert.True(typeof(Kyc.KycTelemetry).Assembly.IsDefined(typeof(GovernanceRequiredAttribute), inherit: false));
        Assert.True(AttributeAllowlist.AnyAssemblyRequiresGovernance());
    }

    [Fact]
    public void A_governed_service_tags_its_resource()
    {
        var attributes = Resource(new RaksawiObservabilityOptions
        {
            ServiceName = "screening-api",
            ServiceNamespace = "kyc",
            GovernanceRequired = true,
        });

        Assert.Equal("required", attributes["raksawi.governance"]);
    }

    [Fact]
    public void A_service_on_the_denylist_carries_no_tag()
    {
        var attributes = Resource(new RaksawiObservabilityOptions
        {
            ServiceName = "ledger-api",
            ServiceNamespace = "treasury",
        });

        Assert.False(attributes.ContainsKey("raksawi.governance"));
    }

    private static Dictionary<string, object> Resource(RaksawiObservabilityOptions options) =>
        ServiceIdentity.BuildResource(options).Build().Attributes
            .GroupBy(a => a.Key)
            .ToDictionary(g => g.Key, g => g.Last().Value);
}
