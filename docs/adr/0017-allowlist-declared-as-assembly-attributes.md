# 17. The allowlist is declared as assembly attributes, not shipped as a manifest

Date: 2026-08-10

## Status

Accepted. Supersedes the manifest mechanism in
[ADR-0002](./0002-closed-allowlist-provenance.md); that ADR's closed-set
decision stands unchanged.

## Context

ADR-0002 decided that the set of allowlist sources is closed, and described the
enforcement as "manifests keyed to a known package identity, or a signed marker".
That is a statement of intent. The mechanism was never designed, and the closed
set is worth nothing without one.

Roslyn gives an analyzer `AdditionalFiles` as a path and its text. Per-item
MSBuild metadata is readable through `AnalyzerConfigOptionsProvider` when the
providing package declares `CompilerVisibleItemMetadata`. Any consuming project
can write the same metadata into its own project file, so a metadata marker
establishes nothing — it is a speed bump.

Signing the manifest and verifying it against a public key embedded in the
analyzer does work. It also introduces key management and rotation into a
repository operated at roughly 0.25 FTE, for a threat model that is mostly
someone taking a shortcut.

## Decision

Drop `AdditionalFiles` manifests. Policy packs declare their allowlist as
assembly-level attributes:

```csharp
[assembly: AllowedAttributeKey("application.id", DataClass.Two)]
```

The analyzer resolves those attributes from the referenced assembly symbol via
the compilation, and confirms provenance by checking the strong-name public key
token of the declaring assembly.

Both packages are strong-named. The same attribute declarations are the source
for the runtime allowlist.

### Implemented 2026-08-25

Signing landed with `raksawi.snk`, committed to the repository. A strong-name
key is an identity marker rather than a secret: it stops accidental
substitution, which is exactly the bar this ADR sets. Keeping the private half
out of the repository would put key management and rotation into a repository
run at roughly 0.25 FTE — the cost this ADR already declined to pay for signing
manifest content, buying nothing against the stated threat model.

Until that date the provenance check existed but compared empty tokens, so it
passed vacuously at run time, and the analyzer did not check provenance at all.
Both now compare against the mechanism assembly's public key. Both fail *open*
when there is nothing to compare — an unsigned build accepts every declaration
rather than silently emptying the allowlist — so a build that loses its signing
configuration loses provenance quietly, and a test asserts the assembly is
signed for that reason.

## Consequences

- **There is no manifest, so there is nothing to drift.** ADR-0002 required a
  test asserting that the manifest and the compiled allowlist agree; that test
  and the failure mode behind it both disappear. One declaration, read at
  compile time by the analyzer and at run time by the library.
- The drift test required by
  [ADR-0009](./0009-governing-agent-instrumented-services.md) — collector policy
  against the allowlist — still applies. Agent-instrumented services have no
  assembly to read, so that seam remains genuinely two-sided.
- Strong-naming both packages adds a key file to manage. This is standard .NET
  practice and materially less machinery than signing and verifying manifest
  content.
- Threat model, stated plainly: this defends against accident and casual bypass,
  not against a determined developer, who can fork the package or emit raw OTLP
  regardless. That is the correct bar. Rev 3 **D2.6** holds that a bypassed
  abstraction is worse than none, and the analyzer diagnostic is a review
  trigger rather than a wall.
- Adding an allowlist key now requires a package release rather than a file
  edit. This is a feature: it puts the change through the same review and
  versioning path as any other schema change under **D2.7**.
