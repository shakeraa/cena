// =============================================================================
// Cena Platform — ArchivedExamTargetMartenRegistration (prr-229 prod binding)
//
// Bounded-context Marten document registration for the archived-target
// shred ledger. Mirrors the pattern used by ExamTargetRetentionMarten-
// Registration (prr-229 extension) and keeps cross-cutting
// MartenConfiguration.cs under its 500-LOC ratchet (ADR-0012).
// =============================================================================

using Marten;

namespace Cena.Actors.Retention;

/// <summary>Marten registration for the archived-target shred ledger.</summary>
public static class ArchivedExamTargetMartenRegistration
{
    /// <summary>
    /// Register the <see cref="ExamTargetShredLedgerDocument"/> schema on
    /// Marten. Id is a compound string
    /// (<c>{studentAnonId}|{examTargetCode}</c>) so we declare it
    /// explicitly rather than letting Marten infer a Guid default.
    /// </summary>
    public static void RegisterArchivedExamTargetContext(this StoreOptions opts)
    {
        opts.Schema
            .For<ExamTargetShredLedgerDocument>()
            .Identity(d => d.Id);
    }
}
