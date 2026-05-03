// =============================================================================
// Cena Platform — ExamTargetRetentionExtensionDocument (prr-229 prod binding)
//
// Marten-persisted document shape for the per-student 60-month retention
// extension opt-in. Separate from the domain VO
// (ExamTargetRetentionExtension) so Marten has a single-property string Id
// (= StudentAnonId) and the retention worker can query indexed rows
// efficiently. Round-trips to/from the VO via
// MartenExamTargetRetentionExtensionStore.
// =============================================================================

namespace Cena.Actors.Retention;

/// <summary>
/// Marten document for <see cref="ExamTargetRetentionExtension"/>. Id is
/// the pseudonymous student id — one row per student by the aggregate's
/// idempotency contract.
/// </summary>
public sealed record ExamTargetRetentionExtensionDocument
{
    /// <summary>Pseudonymous student id; primary key for Marten.</summary>
    public string Id { get; init; } = "";

    /// <summary>When the student opted in.</summary>
    public DateTimeOffset SetAtUtc { get; init; }

    /// <summary>
    /// When the extension expires — typically
    /// <c>SetAtUtc + 60 months</c> per ADR-0050 §6.
    /// </summary>
    public DateTimeOffset ExtendedUntilUtc { get; init; }
}
