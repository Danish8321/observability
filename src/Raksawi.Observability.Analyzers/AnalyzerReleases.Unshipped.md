; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
RKS001 | Raksawi.Governance | Warning | Attribute key is not allowlisted (ADR-0003, ADR-0017)
RKS002 | Raksawi.Governance | Error | Opaque business identifier used as a metric dimension (Rev 3 D2.1 rule 1)
RKS003 | Raksawi.Governance | Warning | OTLP exporter configured outside the observability library (ADR-0001)
