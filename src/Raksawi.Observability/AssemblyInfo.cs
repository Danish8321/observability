using System.Runtime.CompilerServices;
using Raksawi.Observability;

[assembly: InternalsVisibleTo("Raksawi.Observability.Tests")]

// The Class 2 keys the mechanism layer owns (ADR-0007). Declared individually,
// never by prefix (ADR-0018) — these are the correlation identifiers, and each
// is a different lifetime, not four names for the same thing.
[assembly: AllowedAttributeKey("correlation.id", DataClass.OpaqueBusinessIdentifier)]
[assembly: AllowedAttributeKey("session.id", DataClass.OpaqueBusinessIdentifier)]
[assembly: AllowedAttributeKey("message.id", DataClass.OpaqueBusinessIdentifier)]
[assembly: AllowedAttributeKey("causation.id", DataClass.OpaqueBusinessIdentifier)]

// tenant.id is deliberately absent. ADR-0011 promoted it to estate vocabulary
// and withheld it pending D0.3; ADR-0024 closed D0.3 by working position rather
// than by inventory, and the position is that the estate is uniformly KYC
// today. Declaring it now would emit a dimension nothing yet distinguishes.
