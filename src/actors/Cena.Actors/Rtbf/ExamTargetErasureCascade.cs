// =============================================================================
// Cena Platform — ExamTarget + skill-keyed mastery RTBF cascade (prr-223)
//
// IErasureProjectionCascade that covers the projections introduced by
// prr-218 (StudentPlan + ExamTargets) and prr-222 (SkillKeyedMastery) so
// a full-student RTBF (RightToErasureService) cascades through them
// deterministically.
//
// Strategy per category:
//
//   • StudentPlan event stream
//       PRESERVE via ADR-0038 crypto-shred. The event stream is
//       append-only and the subject-key tombstone renders every
//       StudentAnonId / ExamTargetCode payload undecryptable when
//       RightToErasureService.ProcessErasureAsync flips the key.
//
//   • ExamTarget* event stream (ADR-0050 §6, prr-223)
//       PRESERVE via ADR-0038 crypto-shred.
//
//   • SkillKeyedMastery projection (prr-222)
//       DELETE. Mastery rows carry no free-text PII but ARE keyed on
//       StudentAnonId and join to ExamTargetCode — per ADR-0050 §4 a
//       student preparing for multiple targets has distinct rows per
//       target, which the cascade must enumerate explicitly. Going with
//       DELETE instead of crypto-shred here is intentional: these rows
//       are a READ-MODEL projection, not an append-only stream, and
//       Marten-style row delete is clean.
//
//   • ExamTargetRetentionExtension opt-in (prr-229)
//       DELETE. Per-student profile-bit; no audit requirement.
//
// Idempotent: calling EraseForStudentAsync twice for the same student
// returns Count=0 on the second call and does not fail the cascade.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Retention;
using Cena.Infrastructure.Compliance;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Rtbf;

/// <summary>
/// Cascade that erases prr-218/222/229 per-student projections. Registered
/// via DI alongside the other <see cref="IErasureProjectionCascade"/>
/// implementations in <c>AddCenaComplianceServices</c>.
/// </summary>
public sealed class ExamTargetErasureCascade : IErasureProjectionCascade
{
    /// <summary>
    /// Stable name recorded in the manifest audit trail. Mirrors the
    /// "ExamTargetProjections" bucket in the arch test
    /// <c>ErasureCascadeCoversExamTargetProjectionsTest</c>.
    /// </summary>
    public const string StableName = "ExamTargetProjections";

    private readonly ISkillKeyedMasteryStore _masteryStore;
    private readonly IExamTargetRetentionExtensionStore _extensionStore;
    private readonly ILogger<ExamTargetErasureCascade> _logger;

    public ExamTargetErasureCascade(
        ISkillKeyedMasteryStore masteryStore,
        IExamTargetRetentionExtensionStore extensionStore,
        ILogger<ExamTargetErasureCascade> logger)
    {
        _masteryStore = masteryStore
            ?? throw new ArgumentNullException(nameof(masteryStore));
        _extensionStore = extensionStore
            ?? throw new ArgumentNullException(nameof(extensionStore));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProjectionName => StableName;

    /// <inheritdoc />
    public async Task<ErasureManifestItem> EraseForStudentAsync(
        string studentId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        // Erasure target: SkillKeyedMasteryRow (keyed by StudentAnonId,
        // ExamTargetCode, SkillCode per prr-222). Deletion routes through
        // ISkillKeyedMasteryStore; the row type name appears here so the
        // architecture ratchet ErasureCascadeCoversExamTargetProjectionsTest
        // can grep-verify coverage without reflection.
        var masteryDeleted = await _masteryStore
            .DeleteByStudentAsync(studentId, ct)
            .ConfigureAwait(false);
        var extensionDeleted = await _extensionStore
            .DeleteAsync(studentId, ct)
            .ConfigureAwait(false);

        var totalDeleted = masteryDeleted + (extensionDeleted ? 1 : 0);

        _logger.LogInformation(
            "[SIEM] ExamTargetErasureCascade: student={StudentId} "
            + "masteryRowsDeleted={MasteryRows} "
            + "retentionExtensionsDeleted={ExtensionRows} "
            + "(append-only ExamTarget* + StudentPlan streams preserved "
            + "via ADR-0038 crypto-shred).",
            studentId,
            masteryDeleted,
            extensionDeleted ? 1 : 0);

        // The cascade records BOTH the deleted rows (for the mastery +
        // retention projections) AND attests that the append-only event
        // streams are covered by the ADR-0038 crypto-shred. We represent
        // that via the `details` string so the manifest auditor can see
        // the complete coverage at a glance.
        return new ErasureManifestItem(
            store: StableName,
            action: ErasureAction.Deleted,
            count: totalDeleted,
            details:
                "SkillKeyedMastery rows + ExamTargetRetentionExtension rows "
                + "hard-deleted. StudentPlan + ExamTarget* append-only event "
                + "streams covered via ADR-0038 subject-key tombstone "
                + "crypto-shred (fired by RightToErasureService).");
    }
}
