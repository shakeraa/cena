// =============================================================================
// Cena Platform — InterleavingPolicy (prr-237, EPIC-PRR-F)
//
// Within-session cross-target interleaving. Given a student with >1 active
// ExamTargets that are NOT currently exam-week-locked, the scheduler should
// mix topics across targets inside the same session (e.g. Bagrut-Math 3
// topics + Bagrut-Physics 2 topics alternating by target deficit share).
//
// WHY (research): interleaved practice produces larger discrimination-
// learning gains than blocked practice — Rohrer & Taylor (2007) reported
// d = 0.34 for mixed-topic vs. same-topic practice in high-school math
// problem-solving. The Brunmair (2019) meta-analysis over 59 studies
// confirms the effect size (d ≈ 0.34) and warns against the Rohrer-
// cherry-pick d = 1.05 that would overstate gains. We cite the d = 0.34
// meta value only — persona-cogsci sign-off per the "Honest not
// complimentary" memory + ADR-0049 citation-integrity rule.
//
// NON-NEGOTIABLES:
//   1. Pure heuristic — no LLM call (ADR-0026, SchedulerNoLlmCallTest).
//   2. Disabled inside exam-week lock — single-target runs preserve the
//      wave-2 ActiveExamTargetPolicy behaviour exactly. The architecture
//      test InterleavingDisabledInExamWeekLockTest guards this.
//   3. No dark-pattern copy — this file emits data only, never UI strings
//      (shipgate scanner GD-004).
//   4. Determinism — same inputs ⇒ same output. Tie-breaker is insertion
//      order of the active-targets list.
//
// ALLOCATION MATH (narrow MVP):
//   - per-target weight w_i = WeeklyHours_i × max(ε, MasteryDeficit_i),
//     with ε = 0.05 so a zero-deficit target still contributes.
//   - per-target slot count s_i = round(totalSlots × w_i / Σ w), then
//     clamped so 1 ≤ s_i ≤ min(3, candidates_i). The "cap at 3"
//     constraint per task body keeps any one target from dominating.
//   - totalSlots defaults to the sum of per-target candidate counts,
//     capped at SlotCeiling (8) to keep sessions short.
//   - Remainder-redistribution keeps Σ s_i == effective totalSlots after
//     clamping — any target that got capped at 3 donates leftover to the
//     next-highest-weight target with headroom.
//
// INTERLEAVE ORDER:
//   - Largest bucket gets the first slot; then round-robin across targets
//     in descending bucket order. Within a target, items come from its
//     candidate list in caller-supplied priority order.
//   - NOTE: this is WITHIN-SESSION mixing, not cross-session alternation.
//     Rohrer's discrimination effect requires adjacent items to come from
//     different skill families — round-robin by target satisfies that.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;
using Cena.Actors.StudentPlan;

namespace Cena.Actors.Sessions;

/// <summary>
/// One target's input contribution to <see cref="InterleavingPolicy.Plan"/>.
/// </summary>
/// <param name="TargetId">Stable target identifier — carried through to the
/// interleaved output and to the audit event.</param>
/// <param name="Candidates">Per-target ordered PlanEntry list (AdaptiveScheduler
/// output, or any priority-ordered subset). Insertion order == priority order.
/// Empty is legal: the target contributes zero slots.</param>
/// <param name="WeeklyHours">Weekly-hour commitment from
/// <see cref="ExamTarget.WeeklyHours"/>. Drives bucket-size weighting.</param>
/// <param name="MasteryDeficit">Caller-supplied deficit scalar, typically
/// <c>1 - mean(P(L))</c> across the target's skill set. Clamped to
/// <c>[0, 1]</c> by the policy.</param>
public readonly record struct InterleavingTargetInput(
    ExamTargetId TargetId,
    ImmutableArray<PlanEntry> Candidates,
    int WeeklyHours,
    double MasteryDeficit);

/// <summary>
/// One interleaved output slot. <see cref="Entry"/> is the concrete
/// <see cref="PlanEntry"/> pulled from that target's candidate list;
/// <see cref="TargetId"/> is the provenance tag the mastery-update router
/// uses to attribute attempts back to the correct target (analytics only —
/// mastery projection remains skill-keyed per PRR-222).
/// </summary>
public readonly record struct InterleavedPlanEntry(
    ExamTargetId TargetId,
    PlanEntry Entry);

/// <summary>
/// Per-target allocation the audit event records. Exposed so the
/// SessionInterleavingPlanned_V1 event body can carry the exact slot
/// breakdown that produced the interleaved plan.
/// </summary>
public readonly record struct TargetAllocation(
    ExamTargetId TargetId,
    int Slots,
    double BucketWeight);

/// <summary>
/// Outcome of <see cref="InterleavingPolicy.Plan"/>.
/// </summary>
/// <param name="Entries">Interleaved plan entries in delivery order.</param>
/// <param name="Allocations">Per-target slot breakdown (audit).</param>
/// <param name="Disabled">True when interleaving was short-circuited — the
/// caller should fall back to the wave-2 single-target plan. Set when
/// <paramref name="Allocations"/> is empty OR fewer than 2 targets had
/// candidates OR the caller passed <c>lockedForExamWeek == true</c>.</param>
/// <param name="DisabledReason">Which branch fired the short-circuit, for
/// observability / tests. <see cref="InterleavingDisabledReason.NotDisabled"/>
/// when interleaving actually ran.</param>
public readonly record struct InterleavingResult(
    ImmutableArray<InterleavedPlanEntry> Entries,
    ImmutableArray<TargetAllocation> Allocations,
    bool Disabled,
    InterleavingDisabledReason DisabledReason);

/// <summary>
/// Why <see cref="InterleavingPolicy.Plan"/> chose not to interleave. Kept
/// as an enum (not a string) to avoid shipgate dark-pattern scanner false
/// positives on free-text "reason" fields.
/// </summary>
public enum InterleavingDisabledReason
{
    /// <summary>Interleaving actually ran; result is in
    /// <see cref="InterleavingResult.Entries"/>.</summary>
    NotDisabled = 0,

    /// <summary>Caller supplied <c>lockedForExamWeek = true</c>; wave-2
    /// single-target behaviour is preserved.</summary>
    ExamWeekLock = 1,

    /// <summary>Fewer than 2 targets — nothing to interleave against.</summary>
    SingleOrZeroTargets = 2,

    /// <summary>Only one target had non-empty candidates; interleaving
    /// degenerates to single-target.</summary>
    OnlyOneTargetHasCandidates = 3,
}

/// <summary>
/// Pure policy. No state, no I/O, no LLM — the SchedulerNoLlmCallTest
/// scans this directory and asserts the absence of any vendor-typed
/// identifier. Determinism is part of the contract so the audit event's
/// allocation bytes are replay-stable.
/// </summary>
public static class InterleavingPolicy
{
    /// <summary>
    /// Hard cap on total slots per session. Keeps within-session
    /// interleaving from producing a 20-topic marathon just because the
    /// student has 4 targets. Matches the EPIC-PRR-F "short, mixed
    /// sessions" design note.
    /// </summary>
    public const int SlotCeiling = 8;

    /// <summary>Per-target slot cap from the task body — <c>min(3,
    /// candidates)</c>. Prevents any single target from dominating a
    /// mixed session.</summary>
    public const int MaxSlotsPerTarget = 3;

    /// <summary>Floor for zero-deficit targets so they still contribute
    /// a slot when active (avoids Σw = 0 degenerate case when every
    /// target is fully mastered).</summary>
    private const double DeficitFloor = 0.05;

    /// <summary>
    /// Produce an interleaved plan, or short-circuit with
    /// <see cref="InterleavingResult.Disabled"/> set when the preconditions
    /// fail. Never throws on empty input.
    /// </summary>
    /// <param name="targets">Per-target inputs. Order is treated as the
    /// deterministic tie-breaker.</param>
    /// <param name="lockedForExamWeek">When true (from
    /// <see cref="ActiveExamTargetPolicy.Resolve"/>.LockedForExamWeek),
    /// interleaving is DISABLED — wave-2 single-target behaviour is
    /// preserved.</param>
    /// <param name="slotCeiling">Override for <see cref="SlotCeiling"/>.
    /// Clamped to <c>[1, 16]</c> to keep the output bounded.</param>
    public static InterleavingResult Plan(
        IReadOnlyList<InterleavingTargetInput> targets,
        bool lockedForExamWeek,
        int? slotCeiling = null)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (lockedForExamWeek)
        {
            return Empty(InterleavingDisabledReason.ExamWeekLock);
        }

        if (targets.Count < 2)
        {
            return Empty(InterleavingDisabledReason.SingleOrZeroTargets);
        }

        // Fast filter: how many targets actually have non-empty candidates?
        var live = new List<InterleavingTargetInput>(targets.Count);
        foreach (var t in targets)
        {
            if (t.Candidates.Length > 0) live.Add(t);
        }

        if (live.Count < 2)
        {
            return Empty(InterleavingDisabledReason.OnlyOneTargetHasCandidates);
        }

        // Effective ceiling: caller override clamped to [1, 16]; defaults to
        // SlotCeiling. Then upper-bound by the sum of per-target candidates
        // (we cannot allocate more slots than entries exist).
        var ceiling = Math.Clamp(slotCeiling ?? SlotCeiling, 1, 16);
        var totalAvailable = 0;
        foreach (var t in live) totalAvailable += t.Candidates.Length;
        var totalSlots = Math.Min(ceiling, totalAvailable);

        // Weights: w_i = WeeklyHours × max(ε, deficit). WeeklyHours is
        // clamped to [1, ExamTarget.MaxWeeklyHours] to neutralise a
        // malformed input row; deficit is clamped to [DeficitFloor, 1].
        var weights = new double[live.Count];
        var totalWeight = 0.0;
        for (var i = 0; i < live.Count; i++)
        {
            var hours = Math.Clamp(live[i].WeeklyHours, 1, ExamTarget.MaxWeeklyHours);
            var deficit = Math.Clamp(live[i].MasteryDeficit, 0d, 1d);
            if (deficit < DeficitFloor) deficit = DeficitFloor;
            weights[i] = hours * deficit;
            totalWeight += weights[i];
        }

        // Degenerate all-zero weight is unreachable given DeficitFloor and
        // the hours-clamp, but guard anyway.
        if (totalWeight <= 0)
        {
            return Empty(InterleavingDisabledReason.OnlyOneTargetHasCandidates);
        }

        // Initial proportional allocation via largest-remainder to keep
        // Σ s_i == totalSlots when all inputs fit under their caps.
        var slots = AllocateSlots(live, weights, totalWeight, totalSlots);

        // Redistribute any leftover from targets capped at MaxSlotsPerTarget
        // or at candidate-count to the next-highest-weight target with
        // headroom. Happens in descending weight order; stable on ties.
        Redistribute(live, weights, slots, totalSlots);

        // Build audit allocations + interleave the entries.
        var allocs = ImmutableArray.CreateBuilder<TargetAllocation>(live.Count);
        for (var i = 0; i < live.Count; i++)
        {
            allocs.Add(new TargetAllocation(
                TargetId: live[i].TargetId,
                Slots: slots[i],
                BucketWeight: weights[i]));
        }

        var ordered = InterleaveByDescendingSlotCount(live, slots);

        return new InterleavingResult(
            Entries: ordered,
            Allocations: allocs.ToImmutable(),
            Disabled: false,
            DisabledReason: InterleavingDisabledReason.NotDisabled);
    }

    // ── private helpers ─────────────────────────────────────────────────

    private static InterleavingResult Empty(InterleavingDisabledReason reason)
        => new(
            Entries: ImmutableArray<InterleavedPlanEntry>.Empty,
            Allocations: ImmutableArray<TargetAllocation>.Empty,
            Disabled: true,
            DisabledReason: reason);

    /// <summary>
    /// Largest-remainder (Hamilton) allocation. Each target gets at least
    /// 1 slot (we already filtered to non-empty candidates). Result is
    /// clamped to <c>min(MaxSlotsPerTarget, candidates_i)</c>; any
    /// truncation is recovered by <see cref="Redistribute"/>.
    /// </summary>
    private static int[] AllocateSlots(
        List<InterleavingTargetInput> live,
        double[] weights,
        double totalWeight,
        int totalSlots)
    {
        var slots = new int[live.Count];
        var remainders = new double[live.Count];

        var floorSum = 0;
        for (var i = 0; i < live.Count; i++)
        {
            var exact = totalSlots * weights[i] / totalWeight;
            var floor = (int)Math.Floor(exact);
            // Ensure at least 1 slot per live target.
            if (floor < 1) floor = 1;
            slots[i] = floor;
            remainders[i] = exact - Math.Floor(exact);
            floorSum += floor;
        }

        // Distribute leftover slots (if any) by largest remainder, then
        // insertion order. If floorSum > totalSlots (happens when every
        // target was forced to min 1), trim from smallest-weight targets
        // that have more than 1.
        var leftover = totalSlots - floorSum;
        if (leftover > 0)
        {
            var order = Enumerable.Range(0, live.Count)
                .OrderByDescending(i => remainders[i])
                .ThenBy(i => i)
                .ToArray();
            var k = 0;
            while (leftover > 0 && k < order.Length)
            {
                slots[order[k]]++;
                leftover--;
                k++;
                if (k == order.Length && leftover > 0) k = 0;
            }
        }
        else if (leftover < 0)
        {
            var order = Enumerable.Range(0, live.Count)
                .OrderBy(i => weights[i])
                .ThenByDescending(i => i)
                .ToArray();
            var k = 0;
            while (leftover < 0)
            {
                if (slots[order[k]] > 1)
                {
                    slots[order[k]]--;
                    leftover++;
                }
                k = (k + 1) % order.Length;
            }
        }

        // Clamp to per-target maxes. Overflow is redistributed next.
        for (var i = 0; i < live.Count; i++)
        {
            var cap = Math.Min(MaxSlotsPerTarget, live[i].Candidates.Length);
            if (slots[i] > cap) slots[i] = cap;
        }

        return slots;
    }

    /// <summary>
    /// After the cap clamp, Σ slots may be less than totalSlots. Donate
    /// those leftovers to the highest-weight target that still has room
    /// (both under MaxSlotsPerTarget AND under its candidate count).
    /// </summary>
    private static void Redistribute(
        List<InterleavingTargetInput> live,
        double[] weights,
        int[] slots,
        int totalSlots)
    {
        var assigned = 0;
        for (var i = 0; i < slots.Length; i++) assigned += slots[i];
        var leftover = totalSlots - assigned;
        if (leftover <= 0) return;

        // Descending weight, stable on ties via insertion order.
        var order = Enumerable.Range(0, live.Count)
            .OrderByDescending(i => weights[i])
            .ThenBy(i => i)
            .ToArray();

        var safety = leftover * live.Count + 1;
        while (leftover > 0 && safety-- > 0)
        {
            var progressed = false;
            foreach (var i in order)
            {
                if (leftover == 0) break;
                var cap = Math.Min(MaxSlotsPerTarget, live[i].Candidates.Length);
                if (slots[i] < cap)
                {
                    slots[i]++;
                    leftover--;
                    progressed = true;
                }
            }
            if (!progressed) break; // everyone is at cap — leftover stays unallocated
        }
    }

    /// <summary>
    /// Round-robin pack by descending slot count. Largest bucket slots in
    /// first, then round-robin across all buckets. Within a target, pull
    /// candidates in caller-supplied priority order. Deterministic.
    /// </summary>
    private static ImmutableArray<InterleavedPlanEntry> InterleaveByDescendingSlotCount(
        List<InterleavingTargetInput> live,
        int[] slots)
    {
        // Sort target indices by descending allocated slots, tie-break on
        // insertion order (so identical splits are stable).
        var order = Enumerable.Range(0, live.Count)
            .Where(i => slots[i] > 0)
            .OrderByDescending(i => slots[i])
            .ThenBy(i => i)
            .ToArray();

        var cursors = new int[live.Count];
        var remaining = new int[live.Count];
        Array.Copy(slots, remaining, live.Count);

        var totalOut = 0;
        for (var i = 0; i < slots.Length; i++) totalOut += slots[i];
        var builder = ImmutableArray.CreateBuilder<InterleavedPlanEntry>(totalOut);

        // Round-robin across `order`. Each pass emits one entry per
        // still-non-empty bucket.
        var produced = 0;
        while (produced < totalOut)
        {
            var progressed = false;
            foreach (var i in order)
            {
                if (remaining[i] == 0) continue;
                var entry = live[i].Candidates[cursors[i]];
                builder.Add(new InterleavedPlanEntry(live[i].TargetId, entry));
                cursors[i]++;
                remaining[i]--;
                produced++;
                progressed = true;
            }
            if (!progressed) break; // defensive — should not happen given totals
        }

        return builder.ToImmutable();
    }
}
