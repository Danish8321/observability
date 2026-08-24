namespace Raksawi.Observability;

/// <summary>
/// Rev 3 D2.1 classification. Every attribute this estate emits has one, and
/// the class decides where it may appear — not whether it is "sensitive" in
/// some general sense.
/// </summary>
/// <remarks>
/// This lives in the mechanism layer rather than a policy pack because the
/// classification is estate vocabulary, not domain vocabulary (the ADR-0011
/// test): a non-KYC service classifies its attributes by the same four bands.
/// The mechanism layer declares its own Class 2 keys through
/// <see cref="AllowedAttributeKeyAttribute"/>, which it could not do if the
/// enum sat in a package it is forbidden to depend on.
/// </remarks>
public enum DataClass
{
    /// <summary>Infrastructure: host, port, region. Free to use anywhere.</summary>
    Infrastructure = 0,

    /// <summary>Technical identifiers: trace, span, message. Free to use anywhere.</summary>
    TechnicalIdentifier = 1,

    /// <summary>
    /// Opaque business identifiers: application, tenant, correlation. Permitted
    /// on spans and logs, and <b>never</b> as a metric dimension.
    /// </summary>
    OpaqueBusinessIdentifier = 2,

    /// <summary>🔒 Restricted personal data. CPR, MRZ, names, addresses. Never emitted.</summary>
    RestrictedPersonalData = 3,

    /// <summary>🔒 Secrets. Tokens, credentials, connection strings. Never emitted.</summary>
    Secret = 4,
}
