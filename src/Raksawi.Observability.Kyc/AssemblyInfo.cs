using Raksawi.Observability;

// The vocabulary this policy pack owns (ADR-0017). Declared with provenance —
// the analyzer reads these at compile time and AttributeAllowlist reads them at
// run time, so there is no manifest to drift.

// Class 2. Opaque business identifier: permitted on spans and logs, and never
// as a metric dimension (Rev 3 D2.1 rule 1).
[assembly: AllowedAttributeKey("application.id", DataClass.OpaqueBusinessIdentifier)]

// Class 0. The KYC domain's own outcome attributes. Not identifiers and not
// unbounded — a boolean and a small closed set of reasons.
//
// Declared individually rather than by allowing a "screening." family: a family
// allow would let any future key under that prefix through all three
// enforcement points without review, which is the default-deny the allowlist
// exists to keep. The cost is a package release per key, and ADR-0017 treats
// that as a feature — it puts a vocabulary change through the same path as any
// other schema change (Rev 3 D2.7).
//
// Found by the analyzer running over the Screening reference service, which had
// been emitting both keys since before any allowlist existed. Until this
// declaration they were dropped before export, so the one signal that tells
// "nothing was submitted" apart from "something was submitted and silently
// never finished" reached no store.
[assembly: AllowedAttributeKey("screening.abandoned", DataClass.Infrastructure)]
[assembly: AllowedAttributeKey("screening.abandon_reason", DataClass.Infrastructure)]
[assembly: AllowedAttributeKey("screening.outcome", DataClass.Infrastructure)]
[assembly: AllowedAttributeKey("screening.provider", DataClass.Infrastructure)]

// Class 1. Technical identifiers naming infrastructure, not people: which
// CouchDB database was addressed, and whether the write lost a revision race.
// db.* would not do — these say more than the semantic-convention keys and are
// what the runbook actually reads.
[assembly: AllowedAttributeKey("couchdb.database", DataClass.TechnicalIdentifier)]
[assembly: AllowedAttributeKey("couchdb.conflict", DataClass.TechnicalIdentifier)]

// Class 0. Outcome of a lookup and state of an application. Bounded: a boolean
// and a closed set of statuses. Note these sit under the "application." prefix
// but are NOT identifiers — application.id is the Class 2 key above, and the
// difference is why the allowlist matches declared keys exactly and never by
// prefix (ADR-0018).
[assembly: AllowedAttributeKey("application.found", DataClass.Infrastructure)]
[assembly: AllowedAttributeKey("application.status", DataClass.Infrastructure)]
