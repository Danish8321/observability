using Raksawi.Observability;

// The Class 2 vocabulary this policy pack owns (ADR-0017). One key, declared
// with provenance — the analyzer reads this declaration at compile time and
// AttributeAllowlist reads it at run time, so there is no manifest to drift.
[assembly: AllowedAttributeKey("application.id", DataClass.OpaqueBusinessIdentifier)]
