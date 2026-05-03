// =============================================================================
// Cena Platform — MasteryMartenRegistration (prr-222 production binding)
//
// Bounded-context-owned Marten registration for the skill-keyed mastery
// document schema. Mirrors StudentPlanMartenRegistration (prr-218) and
// SubscriptionMartenRegistration: the bounded context owns its own
// ConfigureMarten snippet, so cross-cutting MartenConfiguration stays
// under its 500-LOC ratchet (ADR-0012).
// =============================================================================

using Marten;

namespace Cena.Actors.Mastery;

/// <summary>Marten schema registration for the skill-keyed mastery projection.</summary>
public static class MasteryMartenRegistration
{
    /// <summary>
    /// Register the <see cref="SkillKeyedMasteryDocument"/> schema with
    /// Marten. Identity is the string <c>Id</c> property (the canonical
    /// <c>student|target|skill</c> form from <see cref="MasteryKey.ToString"/>).
    /// No inline projections — the document IS the source of truth for
    /// BKT posteriors, folded in-place by the BKT tracker on every
    /// attempt.
    /// </summary>
    public static void RegisterMasteryContext(this StoreOptions opts)
    {
        opts.Schema.For<SkillKeyedMasteryDocument>()
            .Identity(d => d.Id);
    }
}
