// =============================================================================
// Cena Platform — prr-237 InterleavingPolicy unit tests
//
// Three pure-policy cases per the task body:
//   (a) 2 targets with 4:2 deficit-split + enough candidates → 3 + 2 plan.
//   (b) exam-week lock → short-circuit, single-target preserved.
//   (c) 1 target → short-circuit, no interleaving.
//
// + guard tests for the audit event tag contract and the "only one target
// has candidates" short-circuit.
//
// Pure unit tests — no DI, no LLM, no I/O.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;
using Cena.Actors.Sessions;
using Cena.Actors.Sessions.Events;
using Cena.Actors.StudentPlan;

namespace Cena.Actors.Tests.Sessions;

public sealed class InterleavingPolicyTests
{
    // ── (a) 2 targets + 4:2 deficit → 3 + 2 plan ──────────────────────────

    [Fact]
    public void TwoTargets_FourToTwoDeficitSplit_ProducesThreePlusTwoPlan()
    {
        // Same weekly hours, deficits 0.8 vs 0.4 → weight ratio 2:1.
        // With totalSlots = 5 (sum of 3+3 candidates, clamped by caller),
        // Hamilton allocation gives floor(5*2/3)=3 and floor(5*1/3)=1,
        // remainder 1 goes to the larger bucket → 4+1. Per-target cap
        // MaxSlotsPerTarget=3 trims the larger to 3; Redistribute donates
        // the leftover 1 to the smaller bucket → 3+2. That's exactly the
        // task-body expectation.
        var mathInput = NewInput(
            "et-math",
            candidates: Candidates("derivatives", "integrals", "probability"),
            weeklyHours: 5,
            deficit: 0.8);
        var physInput = NewInput(
            "et-phys",
            candidates: Candidates("kinematics", "waves", "thermo"),
            weeklyHours: 5,
            deficit: 0.4);

        var result = InterleavingPolicy.Plan(
            targets: new[] { mathInput, physInput },
            lockedForExamWeek: false,
            slotCeiling: 5);

        Assert.False(result.Disabled);
        Assert.Equal(InterleavingDisabledReason.NotDisabled, result.DisabledReason);

        // Allocation: 3 slots to math, 2 to physics.
        Assert.Equal(2, result.Allocations.Length);
        var mathAlloc = result.Allocations.Single(a => a.TargetId.Value == "et-math");
        var physAlloc = result.Allocations.Single(a => a.TargetId.Value == "et-phys");
        Assert.Equal(3, mathAlloc.Slots);
        Assert.Equal(2, physAlloc.Slots);

        // Output length = Σ slots = 5.
        Assert.Equal(5, result.Entries.Length);

        // Verify per-target counts in the interleaved stream.
        var mathCount = result.Entries.Count(e => e.TargetId.Value == "et-math");
        var physCount = result.Entries.Count(e => e.TargetId.Value == "et-phys");
        Assert.Equal(3, mathCount);
        Assert.Equal(2, physCount);

        // Discrimination-practice requires ADJACENT items to come from
        // different targets at least once — the round-robin packer must
        // produce at least one target-switch in a 3+2 plan (in fact it
        // should alternate: M P M P M).
        var switches = 0;
        for (var i = 1; i < result.Entries.Length; i++)
        {
            if (result.Entries[i].TargetId != result.Entries[i - 1].TargetId) switches++;
        }
        Assert.True(switches >= 2,
            "round-robin must interleave — Rohrer's effect requires adjacent items " +
            "from different skill families; got " + switches + " target-switches.");

        // Each entry within a target must come from that target's candidate
        // list (attribution integrity per PRR-222 — mastery routing).
        var mathTopics = result.Entries
            .Where(e => e.TargetId.Value == "et-math")
            .Select(e => e.Entry.TopicSlug)
            .ToHashSet();
        Assert.Subset(new HashSet<string> { "derivatives", "integrals", "probability" }, mathTopics);

        var physTopics = result.Entries
            .Where(e => e.TargetId.Value == "et-phys")
            .Select(e => e.Entry.TopicSlug)
            .ToHashSet();
        Assert.Subset(new HashSet<string> { "kinematics", "waves", "thermo" }, physTopics);
    }

    // ── (b) exam-week lock → single-target preserved ──────────────────────

    [Fact]
    public void ExamWeekLock_ShortCircuits_Disabled_ExamWeekLock()
    {
        // Same two targets as (a); the lock flag alone must disable
        // interleaving. This is the wave-2 preservation case — when
        // ActiveExamTargetPolicy returns LockedForExamWeek=true the
        // scheduler stays single-target.
        var mathInput = NewInput(
            "et-math",
            candidates: Candidates("derivatives", "integrals", "probability"),
            weeklyHours: 5,
            deficit: 0.8);
        var physInput = NewInput(
            "et-phys",
            candidates: Candidates("kinematics", "waves", "thermo"),
            weeklyHours: 5,
            deficit: 0.4);

        var result = InterleavingPolicy.Plan(
            targets: new[] { mathInput, physInput },
            lockedForExamWeek: true,
            slotCeiling: 5);

        Assert.True(result.Disabled);
        Assert.Equal(InterleavingDisabledReason.ExamWeekLock, result.DisabledReason);
        Assert.Empty(result.Entries);
        Assert.Empty(result.Allocations);

        // Event-tag mapping is the wire-stable contract.
        Assert.Equal(
            SessionInterleavingPlanned_V1.ReasonTags.ExamWeekLock,
            SessionInterleavingPlanned_V1.TagFromReason(result.DisabledReason));
    }

    // ── (c) 1 target → no interleaving ────────────────────────────────────

    [Fact]
    public void SingleTarget_ShortCircuits_Disabled_SingleOrZeroTargets()
    {
        var single = NewInput(
            "et-only",
            candidates: Candidates("derivatives", "integrals", "probability"),
            weeklyHours: 5,
            deficit: 0.6);

        var result = InterleavingPolicy.Plan(
            targets: new[] { single },
            lockedForExamWeek: false);

        Assert.True(result.Disabled);
        Assert.Equal(InterleavingDisabledReason.SingleOrZeroTargets, result.DisabledReason);
        Assert.Empty(result.Entries);
        Assert.Empty(result.Allocations);
    }

    [Fact]
    public void ZeroTargets_ShortCircuits_Disabled_SingleOrZeroTargets()
    {
        var result = InterleavingPolicy.Plan(
            targets: Array.Empty<InterleavingTargetInput>(),
            lockedForExamWeek: false);

        Assert.True(result.Disabled);
        Assert.Equal(InterleavingDisabledReason.SingleOrZeroTargets, result.DisabledReason);
    }

    [Fact]
    public void TwoTargets_OneHasNoCandidates_ShortCircuits()
    {
        // The 2nd target exists on the plan but has produced zero
        // scheduler candidates this session (e.g. all its topics are
        // already mastered). We short-circuit rather than run a
        // degenerate 1-bucket interleave.
        var withCandidates = NewInput(
            "et-has",
            candidates: Candidates("derivatives", "integrals"),
            weeklyHours: 5,
            deficit: 0.5);
        var empty = NewInput(
            "et-empty",
            candidates: ImmutableArray<PlanEntry>.Empty,
            weeklyHours: 5,
            deficit: 0.5);

        var result = InterleavingPolicy.Plan(
            targets: new[] { withCandidates, empty },
            lockedForExamWeek: false);

        Assert.True(result.Disabled);
        Assert.Equal(InterleavingDisabledReason.OnlyOneTargetHasCandidates, result.DisabledReason);
    }

    [Fact]
    public void PerTargetCap_Is_Min3AndCandidateCount()
    {
        // Target A has many candidates and full weight → would get 6
        // but the cap min(3, candidates)=3 must bite. Target B has only
        // 1 candidate → cap bites at 1.
        var big = NewInput(
            "et-big",
            candidates: Candidates("a1", "a2", "a3", "a4", "a5", "a6"),
            weeklyHours: 10,
            deficit: 0.9);
        var small = NewInput(
            "et-small",
            candidates: Candidates("b1"),
            weeklyHours: 10,
            deficit: 0.9);

        var result = InterleavingPolicy.Plan(
            targets: new[] { big, small },
            lockedForExamWeek: false,
            slotCeiling: 10);

        Assert.False(result.Disabled);
        var bigAlloc = result.Allocations.Single(a => a.TargetId.Value == "et-big");
        var smallAlloc = result.Allocations.Single(a => a.TargetId.Value == "et-small");
        Assert.Equal(3, bigAlloc.Slots);           // cap at 3
        Assert.Equal(1, smallAlloc.Slots);         // cap at 1 candidate
        Assert.Equal(4, result.Entries.Length);
    }

    [Fact]
    public void Determinism_SameInputs_SameOutput()
    {
        var a = NewInput("et-a", Candidates("x", "y", "z"), 5, 0.7);
        var b = NewInput("et-b", Candidates("m", "n", "o"), 5, 0.3);
        var r1 = InterleavingPolicy.Plan(new[] { a, b }, false, 5);
        var r2 = InterleavingPolicy.Plan(new[] { a, b }, false, 5);

        Assert.Equal(r1.Entries.Length, r2.Entries.Length);
        for (var i = 0; i < r1.Entries.Length; i++)
        {
            Assert.Equal(r1.Entries[i].TargetId, r2.Entries[i].TargetId);
            Assert.Equal(r1.Entries[i].Entry.TopicSlug, r2.Entries[i].Entry.TopicSlug);
        }
    }

    [Fact]
    public void AuditEventTagContract_CoversAllReasons()
    {
        // Every enum value must map to a non-empty wire tag; that is the
        // contract SessionInterleavingPlanned_V1.ReasonTags locks down.
        foreach (InterleavingDisabledReason r in Enum.GetValues<InterleavingDisabledReason>())
        {
            var tag = SessionInterleavingPlanned_V1.TagFromReason(r);
            Assert.False(string.IsNullOrWhiteSpace(tag));
            Assert.DoesNotContain(' ', tag);   // tags are slugs, not prose
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static InterleavingTargetInput NewInput(
        string id, ImmutableArray<PlanEntry> candidates, int weeklyHours, double deficit)
        => new(
            TargetId: new ExamTargetId(id),
            Candidates: candidates,
            WeeklyHours: weeklyHours,
            MasteryDeficit: deficit);

    private static ImmutableArray<PlanEntry> Candidates(params string[] topics)
        => topics.Select(NewPlanEntry).ToImmutableArray();

    private static PlanEntry NewPlanEntry(string topicSlug)
        => new(
            TopicSlug: topicSlug,
            WeekIndex: 0,
            AllocatedTime: TimeSpan.Zero,
            PriorityScore: 1.0,
            WeaknessComponent: 0.5,
            TopicWeightComponent: 0.1,
            PrerequisiteComponent: 1.0,
            Rationale: $"practice {topicSlug}");
}
