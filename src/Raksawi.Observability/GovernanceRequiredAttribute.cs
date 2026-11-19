namespace Raksawi.Observability;

/// <summary>
/// Marks a policy pack whose presence puts the service on the log allowlist
/// rather than the denylist (ADR-0043), as if it had set
/// <see cref="RaksawiObservabilityOptions.GovernanceRequired"/>.
/// </summary>
/// <remarks>
/// A regulated service that forgets the flag fails open, so a pack for
/// regulated data sets it on the service's behalf, and code cannot unset it.
/// Unlike <see cref="AllowedAttributeKeyAttribute"/> this carries no provenance
/// check: it can only make filtering stricter, so anyone may apply it.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class GovernanceRequiredAttribute : Attribute
{
}
