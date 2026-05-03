// =============================================================================
// Cena Platform — PerTargetDiagnosticEngine (prr-228)
//
// Orchestrates the per-ExamTarget diagnostic block: converts the set of
// <see cref="DiagnosticBlockResponse"/> for a single target into:
//
//   1. Per-skill BKT priors, upserted via <see cref="IBktStateTracker"/> so
//      the mastery projection key (StudentId, ExamTargetCode, SkillCode)
//      lands populated per wave1c's schema.
//   2. A `TargetBlockSummary` with completion state, so the onboarding
//      flow can decide whether to advance to the next target or surface
//      a skip outcome.
//
// The engine is NOT a transport layer — the REST endpoint layer in
// DiagnosticEndpoints calls into it after wiring up the request body.
//
// Design constraints:
//   - Per ADR-0003 the engine does NOT persist misconception data — only
//     BKT priors are emitted, and only the skill-keyed mastery row.
//   - A skip is NOT a wrong answer: skipped responses feed TopicFeeling
//     (`New` / `Anxious`) capture and contribute to the block cap, but do
//     NOT drive the posterior downward. The selector test pins this.
//   - Provenance is threaded through for telemetry but the actual
//     ADR-0043 enforcement happens at item CONSTRUCTION time
//     (DiagnosticBlockItem.Create throws on MinistryBagrut) and at
//     service layer where items are built from question documents.
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PerTarget;

/// <summary>
/// Summary of a completed per-target diagnostic block.
/// </summary>
/// <param name="ExamTargetCode">The target this block calibrated.</param>
/// <param name="ItemsServed">Items served (answered + skipped).</param>
/// <param name="ItemsAnswered">Items the student answered (not skipped).</param>
/// <param name="ItemsSkipped">Items the student skipped.</param>
/// <param name="StopReason">Why the block ended.</param>
/// <param name="SkillPriors">Per-skill BKT prior posterior. One entry per
/// distinct skill observed in the block.</param>
public sealed record TargetBlockSummary(
    ExamTargetCode ExamTargetCode,
    int ItemsServed,
    int ItemsAnswered,
    int ItemsSkipped,
    AdaptiveStopDecision StopReason,
    IReadOnlyDictionary<SkillCode, double> SkillPriors);

/// <summary>
/// Per-target diagnostic engine.
/// </summary>
public sealed class PerTargetDiagnosticEngine
{
    private readonly IBktStateTracker _bkt;
    private readonly ILogger<PerTargetDiagnosticEngine> _logger;

    public PerTargetDiagnosticEngine(
        IBktStateTracker bkt,
        ILogger<PerTargetDiagnosticEngine> logger)
    {
        _bkt = bkt ?? throw new ArgumentNullException(nameof(bkt));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Process the set of responses for ONE exam target and emit per-skill
    /// mastery priors. Returns a <see cref="TargetBlockSummary"/>. Does
    /// NOT process multiple targets at once — the endpoint layer calls
    /// this once per target in a request.
    /// </summary>
    public async Task<TargetBlockSummary> ProcessBlockAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        IReadOnlyList<DiagnosticBlockResponse> responses,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        if (responses is null || responses.Count == 0)
        {
            throw new ArgumentException(
                "responses must be non-empty — a block always runs at least one item.",
                nameof(responses));
        }

        // Group responses by skill so we update one BKT row per skill.
        var bySkill = responses
            .GroupBy(r => r.SkillCode)
            .ToList();

        var skillPriors = new Dictionary<SkillCode, double>();

        foreach (var group in bySkill)
        {
            var skillCode = group.Key;
            var skillResponses = group.ToList();

            // Running posterior: seed at 0.5 (uninformed), fold each
            // answered response through the BKT step. Skips are no-ops.
            var posterior = 0.5;
            foreach (var r in skillResponses)
            {
                posterior = DiagnosticBlockSelector.UpdatePosterior(posterior, r);
            }

            skillPriors[skillCode] = posterior;

            // Persist via the authoritative BKT tracker so the mastery
            // projection key (StudentId, ExamTargetCode, SkillCode)
            // lands with the priors. Each answered response is fed
            // through individually so the tracker's internal logic owns
            // the persistence invariants; skips are still ignored.
            foreach (var r in skillResponses)
            {
                if (r.Action == DiagnosticResponseAction.Skipped)
                {
                    continue;
                }

                await _bkt.UpdateAsync(
                    studentAnonId: studentAnonId,
                    examTargetCode: examTargetCode,
                    skillCode: skillCode,
                    isCorrect: r.Correct,
                    occurredAt: DateTimeOffset.UtcNow,
                    ct: ct).ConfigureAwait(false);
            }
        }

        var itemsAnswered = responses.Count(r => r.Action == DiagnosticResponseAction.Answered);
        var itemsSkipped = responses.Count - itemsAnswered;

        // The stop reason reflects why the BLOCK ended — it's recomputed
        // from the final response count and the dominant skill's posterior.
        var dominantPosterior = skillPriors.Count > 0
            ? skillPriors.Values.Max(p => Math.Abs(p - 0.5)) + 0.5
            : 0.5;
        var stopReason = DiagnosticBlockSelector.Decide(
            responses.Count,
            dominantPosterior);

        // A legitimate "Continue" fall-through means the block was
        // short-circuited by the frontend (e.g. user closed the tab after
        // 3 answers). We still process what we have; the log line
        // captures the shape so ops can spot abandonment trends.
        if (stopReason == AdaptiveStopDecision.Continue)
        {
            _logger.LogInformation(
                "PerTargetDiagnostic block for target={ExamTargetCode} stopped "
                + "with fewer than floor-cap items ({Served}/{Floor}). Likely "
                + "user abandonment, not convergence.",
                examTargetCode.Value,
                responses.Count,
                DiagnosticBlockThresholds.FloorCap);
        }

        _logger.LogInformation(
            "PerTargetDiagnostic block done: target={ExamTargetCode} "
            + "served={Served} answered={Answered} skipped={Skipped} stop={StopReason} "
            + "skillsCalibrated={Skills}",
            examTargetCode.Value,
            responses.Count,
            itemsAnswered,
            itemsSkipped,
            stopReason,
            skillPriors.Count);

        return new TargetBlockSummary(
            ExamTargetCode: examTargetCode,
            ItemsServed: responses.Count,
            ItemsAnswered: itemsAnswered,
            ItemsSkipped: itemsSkipped,
            StopReason: stopReason,
            SkillPriors: skillPriors);
    }
}
