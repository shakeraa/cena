// =============================================================================
// Cena Platform — Concept Item Publication Counter (ADR-0062 Phase 2 gate)
//
// The 002 multi-persona deep research surfaced one tightening over the
// 001 design synthesis: supporting-concept `MasterySignalEmitted_V1`
// nudges should NOT fire on a leaf until ≥10 published items exist
// with that SkillCode attached. The threshold matches the published
// BKT identifiability floor (van de Sande 2013, Beck & Chang 2007) —
// below it, posteriors are too noisy to defend.
//
// This file declares the gate interface. The default in-memory
// implementation is the placeholder host composition uses while the
// projection is being built — it always reports 0, so the Phase 2
// channel stays closed by default. A Marten-backed implementation that
// counts published QuestionDocument rows per SkillCode lands when
// Phase 2 fires for real.
//
// Why an interface, not a static method:
//   * Keeps the gate testable without touching Marten.
//   * Lets tests inject a `Stub` that reports any threshold, so the
//     "≥10" rule can be verified end-to-end without seeding 10 real
//     questions per skill.
//   * Lets a future production backend swap to a cached / distributed
//     counter without touching the rest of the pipeline.
//
// What the gate does NOT do:
//   * Decide whether the primary concept BKT update fires. BKT is
//     unchanged per ADR-0039; the primary concept always updates.
//   * Drive question selection (CAT / PSI). The scheduler keeps using
//     LearningObjectiveDocument linkage for that.
//
// Contract:
//   * `GetPublishedItemCountAsync(skill, ct)` — total questions
//     published with `skill` in `ConceptIds` (any role). Counts the
//     calibration corpus too — an item with curator-confirmed concepts
//     is "published" for this purpose; gate-pending items don't count.
//   * `IsAboveStabilityFloorAsync(skill, ct)` — convenience: returns
//     true when count ≥ <see cref="StabilityFloor"/> (default 10). The
//     default is a memory-rule constant per ADR-0062 §Phase 2; future
//     telemetry might justify raising it on a per-track basis.
//   * Gate is FAIL-CLOSED: missing data → `false`. A bug in the counter
//     can only suppress a Phase 2 nudge; it can never let one through
//     prematurely.
// =============================================================================

namespace Cena.Actors.Mastery.Extraction;

public interface IConceptItemPublicationCounter
{
    /// <summary>The published-count threshold below which Phase 2 nudges stay silent. ADR-0062.</summary>
    int StabilityFloor { get; }

    /// <summary>Number of published questions with this SkillCode in their concept set.</summary>
    Task<int> GetPublishedItemCountAsync(SkillCode skill, CancellationToken ct = default);

    /// <summary>True iff the count meets <see cref="StabilityFloor"/>. Fail-closed on any error.</summary>
    Task<bool> IsAboveStabilityFloorAsync(SkillCode skill, CancellationToken ct = default);
}

/// <summary>
/// Default placeholder. Always reports 0 → gate stays CLOSED in every
/// host composition that doesn't bind a real backend. Phase 2 cannot
/// flip on accidentally; a host has to wire a concrete counter (Marten
/// projection-backed) to enable supporting-concept nudges.
/// </summary>
public sealed class NullConceptItemPublicationCounter : IConceptItemPublicationCounter
{
    public int StabilityFloor => 10;

    public Task<int> GetPublishedItemCountAsync(SkillCode skill, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<bool> IsAboveStabilityFloorAsync(SkillCode skill, CancellationToken ct = default)
        => Task.FromResult(false);
}
