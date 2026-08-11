using Raksawi.Observability;

namespace Raksawi.Observability.Tests;

/// <summary>
/// ADR-0010: absent sampler configuration is a boot failure outside
/// development, because a silently wrong sampling rate is not detectable from
/// the data it produces.
/// </summary>
public class RaksawiObservabilityOptionsTests
{
    private static RaksawiObservabilityOptions Valid() => new()
    {
        ServiceName = "screening-api",
        ServiceNamespace = "kyc",
        SamplingRatio = 1.0,
    };

    [Fact]
    public void Absent_sampling_ratio_fails_outside_development()
    {
        var options = Valid();
        options.SamplingRatio = null;

        var error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("ADR-0010", error.Message);
    }

    [Fact]
    public void Absent_sampling_ratio_is_permitted_in_development()
    {
        var options = Valid();
        options.SamplingRatio = null;
        options.IsDevelopment = true;

        options.Validate();

        Assert.Equal(1.0, options.EffectiveSamplingRatio);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Sampling_ratio_outside_zero_to_one_is_rejected(double ratio)
    {
        var options = Valid();
        options.SamplingRatio = ratio;

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void Zero_sampling_is_permitted()
    {
        // Deliberately allowed: it is a valid operational choice, and unlike an
        // absent value it was chosen.
        var options = Valid();
        options.SamplingRatio = 0;

        options.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Service_name_is_required(string name)
    {
        var options = Valid();
        options.ServiceName = name;

        var error = Assert.Throws<InvalidOperationException>(options.Validate);
        Assert.Contains("ADR-0006", error.Message);
    }

    [Fact]
    public void Service_namespace_is_required()
    {
        var options = Valid();
        options.ServiceNamespace = "";

        Assert.Throws<InvalidOperationException>(options.Validate);
    }

    [Fact]
    public void CouchDb_url_redaction_defaults_to_on()
    {
        // Default-deny, per ADR-0003. Turning it off is a decision someone makes
        // after answering QD2, not the state you land in by not thinking.
        Assert.True(new RaksawiObservabilityOptions().RedactCouchDbUrls);
    }
}
