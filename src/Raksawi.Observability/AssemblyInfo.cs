using System.Runtime.CompilerServices;
using Raksawi.Observability;

// The public key is raksawi.snk's, spelled out because a signed assembly may
// only befriend a signed one. Same key for every assembly in this repository.
[assembly: InternalsVisibleTo("Raksawi.Observability.Tests, PublicKey=" +
    "0024000004800000140100000602000000240000525341310008000001000100754516c2726a1e3e" +
    "1a7bff0b3e75b6b664b5ee94ee857d053e8dcfb0138991b377d6de71a2f30eeb8454d598db430556" +
    "b81353a8689bb82ce73f0d72b66e74b583dbfa77a8af1cdbd6fef83b958da3866da94579e2699f6d" +
    "ffb4882614ed99d17d9b82aa4a51688148e8ba95b820340c07b37dd8b9ff95c5530199428e8866af" +
    "1c8afdbce64dc44f0cf85d3f7276e157c1df070ce40a0a844e2e7f3adf241d8e3d77ad328afdf24d" +
    "67fd3b77c566a5d09c5414858a6c1e1f7a4a5a0b8914b9a26e95d859c32377910f67d619ed1364b4" +
    "d874d4bc0dd978fb83de74e2b6476729045d7795c5993919d263dd4f230c39c95e8301af471ddc3d" +
    "5215ce7d3af4c6eb")]

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
