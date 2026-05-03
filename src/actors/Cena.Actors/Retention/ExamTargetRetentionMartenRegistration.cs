// =============================================================================
// Cena Platform — ExamTargetRetentionMartenRegistration (prr-229 prod)
//
// Bounded-context-owned Marten schema registration for the retention-
// extension document. Mirrors MasteryMartenRegistration (prr-222) and
// StudentPlanMartenRegistration (prr-218) — the context owns its own
// ConfigureMarten snippet so cross-cutting MartenConfiguration stays
// under its 500-LOC ratchet (ADR-0012).
// =============================================================================

using Marten;

namespace Cena.Actors.Retention;

/// <summary>Marten schema registration for the retention-extension document.</summary>
public static class ExamTargetRetentionMartenRegistration
{
    /// <summary>
    /// Register the <see cref="ExamTargetRetentionExtensionDocument"/>
    /// schema. Identity is the string <c>Id</c> property (pseudonymous
    /// student id). No projections — the document IS the source of truth
    /// for the 60-month extension flag; it's a profile bit, not derived
    /// from events.
    /// </summary>
    public static void RegisterExamTargetRetentionContext(this StoreOptions opts)
    {
        opts.Schema.For<ExamTargetRetentionExtensionDocument>()
            .Identity(d => d.Id);
    }
}
