Status: closed — 2026-09-07

# RKS003 matches on method name alone — false positives on any similarly-named method

`src/Raksawi.Observability.Analyzers/TelemetryGovernanceAnalyzer.cs:77-84`:

```csharp
if (ExporterMethods.Contains(method.Name))
{
    context.ReportDiagnostic(Diagnostic.Create(
        Diagnostics.ExporterConfiguredDirectly, invocation.Syntax.GetLocation(), method.Name));
    return;
}
```

`ExporterMethods` is `{ "AddOtlpExporter", "AddConsoleExporter", "AddInMemoryExporter" }`
(line 34). No check on `method.ContainingType`, `ContainingNamespace`, or
receiver type.

Contrast the RKS001 path on line 91, which correctly gates on
`IsActivity(method.ContainingType)`, and the RKS002 path on line 86, which gates
on `IsMetricInstrument`. RKS003 is the odd one out.

## Impact

Any method in a consuming codebase named `AddOtlpExporter` /
`AddConsoleExporter` / `AddInMemoryExporter` — a service's own wrapper
extension, a test helper, a builder on an unrelated type — is reported. The
early `return` also means such a call is never considered for the other two
rules.

The analyzer's own class doc explains why this matters: it is deliberately
literal-only "because guessing produces false positives, and a false positive
on a governance rule teaches people to suppress governance rules"
(`TelemetryGovernanceAnalyzer.cs:17-21`). The same reasoning applies here and is
not applied.

## Fix

Gate on the declaring assembly or namespace:

```csharp
if (ExporterMethods.Contains(method.Name)
    && method.ContainingNamespace?.ToDisplayString().StartsWith("OpenTelemetry") == true)
```

Note the SDK spreads these across `OpenTelemetry.Trace`, `OpenTelemetry.Metrics`
and `OpenTelemetry.Logs`, so a namespace prefix is the right granularity rather
than an exact type name.

## Verification required

Compilation test with a locally-declared `AddOtlpExporter` extension asserting
RKS003 does **not** fire, alongside the existing positive case in
`TelemetryGovernanceAnalyzerTests.cs`.

## Fixed (2026-09-07)

`TelemetryGovernanceAnalyzer.IsExporterExtension` gates the RKS003 branch on
the declaring namespace, matching how the Activity and metric branches were
already gated:

```csharp
if (ExporterMethods.Contains(method.Name) && IsExporterExtension(method))
```

Prefix match on `OpenTelemetry` rather than an exact type name, because the SDK
spreads these extensions across `OpenTelemetry.Trace`, `OpenTelemetry.Metrics`
and `OpenTelemetry.Logs`. A non-matching call now falls through to the RKS001
and RKS002 checks instead of hitting the early `return`.

## Verified

The bug was load-bearing in the suite: `Hand_configured_exporter_raises_RKS003`
asserted on `Pipeline.AddOtlpExporter()` — a method declared *in the snippet
itself*, i.e. the false positive was the only thing the rule was ever tested
against. Gating the analyzer made that test fail, correctly.

Both cases now covered:

- `Hand_configured_exporter_raises_RKS003` — real SDK extension resolved
  against the real assembly
  (`OpenTelemetry.Sdk.CreateTracerProviderBuilder().AddOtlpExporter()`), still
  raises RKS003. Required adding the OTel core and OTLP exporter assemblies to
  the snippet compilation's references and `using OpenTelemetry.Trace;` to its
  template.
- `A_local_method_sharing_an_exporter_name_raises_nothing` — the old fixture,
  now asserting silence.

`check.sh` clean. `test-fast.sh` 140/140.

## Comments
