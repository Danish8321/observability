using Microsoft.Extensions.Configuration;

namespace Raksawi.Observability;

/// <summary>
/// The <c>IConfiguration</c> keys the package reads (ADR-0043), applied after
/// the <c>OTEL_*</c> variables and before code, so code still wins.
/// </summary>
internal static class ObservabilityConfiguration
{
    internal const string GovernanceRequiredKey = "Governance:Required";

    internal const string DeniedLogKeysKey = "Observability:Logs:DeniedKeys";

    internal static void Apply(RaksawiObservabilityOptions options, IConfiguration configuration)
    {
        var governance = configuration[GovernanceRequiredKey];

        if (!string.IsNullOrWhiteSpace(governance))
        {
            // Strict, as for the OTEL_* variables (ADR-0034): "yes" silently
            // read as false would put a regulated service on the denylist.
            options.GovernanceRequired = bool.TryParse(governance.Trim(), out var required)
                ? required
                : throw new InvalidOperationException(
                    $"{GovernanceRequiredKey} is '{governance}'; expected true or false (ADR-0043).");
        }

        foreach (var key in configuration.GetSection(DeniedLogKeysKey).GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(key.Value))
            {
                options.DeniedLogKeys.Add(key.Value.Trim());
            }
        }
    }
}
