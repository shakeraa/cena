// =============================================================================
// Cena Platform -- ML Training Exclusion Attribute
// RDY-006 / ADR-0003 Decision 3: Misconception data must be excluded from
// any corpus used for LLM fine-tuning, RLHF, embedding training, or
// recommendation model training ("Affected Work Product" per Edmodo decree).
//
// Usage:
//   [MlExcluded("ADR-0003: session-scoped misconception data")]
//   public record MisconceptionDetected_V1(...);
// =============================================================================

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Marks an event type as excluded from all ML training pipelines.
/// Any event type decorated with this attribute MUST NOT appear in:
/// <list type="bullet">
///   <item>LLM fine-tuning or RLHF corpora</item>
///   <item>Embedding model training datasets</item>
///   <item>Recommendation model training datasets</item>
///   <item>Any model that could constitute "Affected Work Product" (FTC v. Edmodo)</item>
/// </list>
/// Enforced at compile-time by <c>MlExclusionEnforcementTests</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class MlExcludedAttribute : Attribute
{
    /// <summary>ADR or compliance rationale for the exclusion.</summary>
    public string Reason { get; }

    /// <param name="reason">
    /// Why this type is excluded — must reference an ADR or legal basis
    /// (e.g. "ADR-0003: session-scoped misconception data").
    /// </param>
    public MlExcludedAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }
}
