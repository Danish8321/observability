namespace Raksawi.Observability.Kyc;

/// <summary>
/// Rev 3 D2.1 classification. Every attribute this estate emits has one, and
/// the class decides where it may appear — not whether it is "sensitive" in
/// some general sense.
/// </summary>
public enum DataClass
{
    /// <summary>Infrastructure: host, port, region. Free to use anywhere.</summary>
    Infrastructure = 0,

    /// <summary>Technical identifiers: trace, span, message. Free to use anywhere.</summary>
    TechnicalIdentifier = 1,

    /// <summary>
    /// Opaque business identifiers: application, tenant, correlation. Permitted
    /// on spans and logs, and <b>never</b> as a metric dimension — see
    /// <see cref="KycTelemetry"/> for why that rule is absolute.
    /// </summary>
    OpaqueBusinessIdentifier = 2,

    /// <summary>🔒 Restricted personal data. CPR, MRZ, names, addresses. Never emitted.</summary>
    RestrictedPersonalData = 3,

    /// <summary>🔒 Secrets. Tokens, credentials, connection strings. Never emitted.</summary>
    Secret = 4,
}
