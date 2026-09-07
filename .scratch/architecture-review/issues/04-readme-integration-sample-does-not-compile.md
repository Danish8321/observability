Status: closed — 2026-09-07

# README's .NET 10 integration sample does not compile

`README.md:36-45`:

> **.NET 10** — one package reference, one line:
>
> ```csharp
> builder.AddRaksawiObservability();
> ```
>
> Set `OTEL_SERVICE_NAME` and `DEPLOYMENT_ENVIRONMENT`. Traces, metrics, logs,
> resource attributes, redaction, sampler defaults, and exporter safety all
> follow. The service names no database, no endpoint, and no sampling rate.

Three claims, none true against the code.

1. **No parameterless overload exists.** The only signature is
   `AddRaksawiObservability(this IHostApplicationBuilder, Action<RaksawiObservabilityOptions>)`
   (`RaksawiObservabilityExtensions.net10.cs:26-28`), with
   `ArgumentNullException.ThrowIfNull(configure)`. The snippet is a compile
   error in a consuming service.

2. **`OTEL_SERVICE_NAME` is not read.** `ServiceIdentity.ResolveInstanceId`
   reads `OTEL_SERVICE_INSTANCE_ID` (`ServiceIdentity.cs:64`) and nothing else.
   `ServiceName` and `ServiceNamespace` are required options and throw when
   absent (`RaksawiObservabilityOptions.cs:79-93`). `DEPLOYMENT_ENVIRONMENT` is
   read nowhere.

3. **"The service names no database, no endpoint, and no sampling rate" is
   backwards.** All three are required or strongly recommended per service:
   `OtlpEndpoint` defaults to localhost, `SamplingRatio` is a boot failure when
   absent outside Development (ADR-0010), and `CouchDbHosts` must be populated
   or the ADR-0023 redaction never fires. `samples/Screening.Api/Program.cs:16-29`
   sets all of them, and comments each as necessary.

"logs ... follow" is tracked separately as issue 03.

## Impact

README is the estate-facing contract — service teams in other repos read it
before touching the package. As written it promises an adoption cost (one line,
two env vars) that the library does not deliver, and the failure surfaces at
their build, not ours.

## Fix

Either bring README to the code:

```csharp
builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "screening-api";     // bare, kebab-case (ADR-0006)
    o.ServiceNamespace = "kyc";
    o.OtlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"]!);
    o.SamplingRatio = 0.1;               // required outside Development (ADR-0010)
    o.ActivitySources.Add(MyTelemetry.ActivitySourceName);
    o.CouchDbHosts.Add(couchDbHost);
});
```

or bring the code to README by adding a parameterless overload that binds from
`OTEL_*` environment variables and `IConfiguration`. The second is a real
feature with a real design question (precedence between env, config and the
delegate) and should not be smuggled in as a doc fix — raise it separately if
wanted. Correcting README is the immediate action.

## Fixed (2026-09-07)

`README.md` "What integration looks like" now shows the real signature — the
`Action<RaksawiObservabilityOptions>` form, matching
`samples/Screening.Api/Program.cs` and the onboarding guides. The env-var claim
(`OTEL_SERVICE_NAME`, `DEPLOYMENT_ENVIRONMENT`) and "the service names no
database, no endpoint, and no sampling rate" are gone, replaced by what each
line actually costs if omitted: unregistered source emits no spans,
unregistered meter is collected by nothing, missing `CouchDbHosts` means
unredacted document identifiers, absent `SamplingRatio` is a boot failure
rather than a silent one (ADR-0010).

"Traces, metrics, logs" corrected to traces and metrics, with logs called out
as not wired — see issue 03, still open.

`o.Meters.Add(...)` included, from issue 01.

## Note found while fixing

`docs/onboarding/net10-api-integration.md:41` and
`docs/onboarding/integration.md:18` already showed the correct delegate form.
So README was not merely stale, it contradicted two guides in the same
repository — and README is the surface an outside service team reads first.

A parameterless overload binding from `OTEL_*` and `IConfiguration` was
deliberately **not** added: it is a real feature with a real design question
(precedence between environment, configuration and the delegate) and should not
arrive as a side effect of a documentation fix. Raise separately if wanted.

## Verified

`check.sh` clean, `test-fast.sh` 140/140 — no code touched. The claim itself is
verified by inspection against `RaksawiObservabilityExtensions.net10.cs:26-28`
and the sample.

## Comments
