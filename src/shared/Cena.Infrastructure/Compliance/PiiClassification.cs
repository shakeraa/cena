// =============================================================================
// Cena Platform -- PII Classification Attributes
// SEC-003: FERPA/GDPR data classification for student and admin data.
//
// Usage:
//   [Pii(PiiLevel.High, "contact")]
//   public string Email { get; init; }
// =============================================================================

namespace Cena.Infrastructure.Compliance;

/// <summary>PII sensitivity levels for FERPA/GDPR compliance.</summary>
public enum PiiLevel
{
    /// <summary>Not PII — safe for logs, exports, and LLM routing.</summary>
    None = 0,

    /// <summary>Low sensitivity — anonymized identifiers, internal IDs.</summary>
    Low = 1,

    /// <summary>Medium sensitivity — display names, usernames. Excluded from logs.</summary>
    Medium = 2,

    /// <summary>High sensitivity — email, phone number. Requires encryption + excluded from logs.</summary>
    High = 3,

    /// <summary>Critical — government IDs, payment info. Requires encryption + excluded from logs.</summary>
    Critical = 4
}

/// <summary>
/// Marks a property or field as containing PII at a specific level.
/// Applied to model properties so the <see cref="PiiScanner"/> can discover
/// them at runtime for audit, export, and log-sanitisation purposes.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PiiAttribute : Attribute
{
    /// <summary>Sensitivity level of this field.</summary>
    public PiiLevel Level { get; }

    /// <summary>
    /// Semantic category: "identity", "educational", "behavioral", "contact".
    /// Used in GDPR portability exports and compliance reports.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Whether this field must be encrypted at rest.
    /// Automatically true for <see cref="PiiLevel.High"/> and above.
    /// </summary>
    public bool RequiresEncryption { get; }

    /// <summary>
    /// Whether this field must be redacted in structured logs.
    /// Automatically true for <see cref="PiiLevel.Medium"/> and above.
    /// </summary>
    public bool ExcludeFromLogs { get; }

    /// <param name="level">Sensitivity level.</param>
    /// <param name="category">Semantic category ("identity", "educational", "behavioral", "contact").</param>
    /// <param name="requiresEncryption">
    /// Override to force encryption even below <see cref="PiiLevel.High"/>.
    /// </param>
    /// <param name="excludeFromLogs">
    /// Override to force log redaction even below <see cref="PiiLevel.Medium"/>.
    /// </param>
    public PiiAttribute(
        PiiLevel level,
        string category,
        bool requiresEncryption = false,
        bool excludeFromLogs = false)
    {
        Level = level;
        Category = category;
        RequiresEncryption = level >= PiiLevel.High || requiresEncryption;
        ExcludeFromLogs = level >= PiiLevel.Medium || excludeFromLogs;
    }
}
