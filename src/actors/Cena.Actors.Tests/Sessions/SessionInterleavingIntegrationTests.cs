// =============================================================================
// Cena Platform — prr-237 SessionPlanGenerator × InterleavingPolicy
//
// Integration tests verifying the within-session cross-target interleaving
// wire-up on SessionPlanGenerator. The generator must:
//
//   1. Run InterleavingPolicy when >1 active targets, NOT locked, AND an
//      ISessionInterleavingInputsProvider is wired → Result.Interleaving
//      has entries, allocations, Disabled=false; audit sink receives an
//      enabled SessionInterleavingPlanned_V1 event.
//
//   2. Short-circuit (Disabled=true, reason=ExamWeekLock) when
//      ActiveExamTargetPolicy.Resolve returned LockedForExamWeek=true,
//      regardless of provider wire-up. Wave-2 single-target snapshot is
//      preserved on the PriorityOrdered field.
//
//   3. Short-circuit silently (Disabled=true, reason=SingleOrZeroTargets,
//      no audit event) when no provider is wired — back-compat path.
//
// Together with InterleavingDisabledInExamWeekLockTest (architecture test)
// this locks the exam-week-lock behaviour against regression.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.Mastery;
using Cena.Actors.Sessions;
using Cena.Actors.Sessions.Events;

namespace Cena.Actors.Tests.Sessions;

public sealed class SessionInterleavingIntegrationTests
{
    private const string StudentAnon = "anon-stu-prr237-01";
    private const string SessionId = "sess-prr237-01";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    // ── Case 1 — two targets, no lock, provider wired → interleaved ──────

    [Fact]
    public async Task SessionStart_TwoActiveTargets_NotLocked_RunsInterleaving()
    {
        var summerA = new Cena.Actors.StudentPlan.SittingCode(
            "תשפ״ו",
            Cena.Actors.StudentPlan.SittingSeason.Summer,
            Cena.Actors.StudentPlan.SittingMoed.A);
        var summerB = new Cena.Actors.StudentPlan.SittingCode(
            "תשפ״ו",
            Cena.Actors.StudentPlan.SittingSeason.Summer,
            Cena.Actors.StudentPlan.SittingMoed.B);

        // Far-future sittings so neither target trips the 14-day lock.
        var resolver = new InMemorySittingCanonicalDateResolver(
            new Dictionary<Cena.Actors.StudentPlan.SittingCode, DateTimeOffset>
            {
                [summerA] = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),   // 123d
                [summerB] = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),  // 153d
            });

        var targets = new List<Cena.Actors.StudentPlan.ExamTarget>
        {
            NewTarget("et-math", "BAGRUT_MATH_5U", summerA, weeklyHours: 5),
            NewTarget("et-phys", "BAGRUT_PHYS_5U", summerB, weeklyHours: 5),
        };

        var provider = new StubInterleavingInputsProvider(
            perTarget: new Dictionary<string, (double deficit, string[] topics)>
            {
                ["et-math"] = (0.8, new[] { "derivatives", "integrals", "probability" }),
                ["et-phys"] = (0.4, new[] { "kinematics", "waves", "thermo" }),
            });

        var audit = new CollectingAuditSink();

        var generator = new SessionPlanGenerator(
            planConfig: new InMemoryStudentPlanConfigService(() => FixedNow),
            abilityProvider: EmptySessionAbilityEstimateProvider.Instance,
            graphProvider: EmptyTopicPrerequisiteGraphProvider.Instance,
            overrideBridge: null,
            planReader: new InMemoryStubPlanReader(targets),
            sittingResolver: resolver,
            overrideReader: NullExamTargetOverrideReader.Instance,
            interleavingInputs: provider,
            interleavingAudit: audit);

        var result = await generator.GenerateAsync(StudentAnon, SessionId, FixedNow);

        Assert.False(result.Interleaving.Disabled);
        Assert.Equal(InterleavingDisabledReason.NotDisabled, result.Interleaving.DisabledReason);
        Assert.Equal(2, result.Interleaving.Allocations.Length);
        Assert.NotEmpty(result.Interleaving.Entries);

        // Entries must come from BOTH targets (that's the whole point of
        // the feature). If only one TargetId appears, interleaving is
        // broken.
        var distinctTargets = result.Interleaving.Entries
            .Select(e => e.TargetId.Value)
            .Distinct()
            .ToArray();
        Assert.Equal(2, distinctTargets.Length);

        // Exactly one audit event emitted with Enabled=true.
        Assert.Single(audit.Events);
        var evt = audit.Events[0];
        Assert.True(evt.Enabled);
        Assert.Equal(SessionInterleavingPlanned_V1.ReasonTags.NotDisabled, evt.DisabledReasonTag);
        Assert.Equal(SessionId, evt.SessionId);
        Assert.Equal(StudentAnon, evt.StudentAnonId);
        Assert.Equal(2, evt.Allocations.Count);
        Assert.Equal(result.Interleaving.Entries.Length, evt.TotalSlots);

        // Allocation ExamCode labels carried through (ops/Prometheus).
        Assert.Contains(evt.Allocations, a => a.ExamCode == "BAGRUT_MATH_5U");
        Assert.Contains(evt.Allocations, a => a.ExamCode == "BAGRUT_PHYS_5U");
    }

    // ── Case 2 — exam-week lock overrides provider wiring ────────────────

    [Fact]
    public async Task SessionStart_ExamWeekLock_InterleavingDisabled_EvenWhenProviderWired()
    {
        var summerA = new Cena.Actors.StudentPlan.SittingCode(
            "תשפ״ו",
            Cena.Actors.StudentPlan.SittingSeason.Summer,
            Cena.Actors.StudentPlan.SittingMoed.A);
        var winterA = new Cena.Actors.StudentPlan.SittingCode(
            "תשפ״ז",
            Cena.Actors.StudentPlan.SittingSeason.Winter,
            Cena.Actors.StudentPlan.SittingMoed.A);

        // et-near sits 5 days from FixedNow → inside the 14-day lock.
        var resolver = new InMemorySittingCanonicalDateResolver(
            new Dictionary<Cena.Actors.StudentPlan.SittingCode, DateTimeOffset>
            {
                [summerA] = new(2026, 5, 6, 9, 0, 0, TimeSpan.Zero),   // 5d
                [winterA] = new(2027, 1, 15, 9, 0, 0, TimeSpan.Zero),
            });

        var targets = new List<Cena.Actors.StudentPlan.ExamTarget>
        {
            NewTarget("et-near", "BAGRUT_ENG", summerA, weeklyHours: 5),
            NewTarget("et-far", "BAGRUT_MATH_5U", winterA, weeklyHours: 5),
        };

        var provider = new StubInterleavingInputsProvider(
            perTarget: new Dictionary<string, (double deficit, string[] topics)>
            {
                ["et-near"] = (0.6, new[] { "topic-a", "topic-b" }),
                ["et-far"]  = (0.7, new[] { "topic-c", "topic-d" }),
            });
        var audit = new CollectingAuditSink();

        var generator = new SessionPlanGenerator(
            planConfig: new InMemoryStudentPlanConfigService(() => FixedNow),
            abilityProvider: EmptySessionAbilityEstimateProvider.Instance,
            graphProvider: EmptyTopicPrerequisiteGraphProvider.Instance,
            overrideBridge: null,
            planReader: new InMemoryStubPlanReader(targets),
            sittingResolver: resolver,
            overrideReader: NullExamTargetOverrideReader.Instance,
            interleavingInputs: provider,
            interleavingAudit: audit);

        var result = await generator.GenerateAsync(StudentAnon, SessionId, FixedNow);

        // Exam-week lock MUST win — interleaving disabled, wave-2 single-
        // target plan preserved on the snapshot.
        Assert.True(result.Interleaving.Disabled);
        Assert.Equal(InterleavingDisabledReason.ExamWeekLock, result.Interleaving.DisabledReason);
        Assert.Empty(result.Interleaving.Entries);

        // Provider MUST NOT be called when the lock short-circuits — this
        // is the wave-2 preservation guarantee.
        Assert.Equal(0, provider.CallCount);

        // Audit event emitted with Enabled=false + ExamWeekLock tag, for
        // ops observability.
        Assert.Single(audit.Events);
        Assert.False(audit.Events[0].Enabled);
        Assert.Equal(
            SessionInterleavingPlanned_V1.ReasonTags.ExamWeekLock,
            audit.Events[0].DisabledReasonTag);
    }

    // ── Case 3 — no provider wired → silent back-compat ──────────────────

    [Fact]
    public async Task SessionStart_NoProvider_SilentlyPreservesWave2Behaviour()
    {
        // Prr-226 ctor overload (no interleaving seam) → generator must
        // not emit any SessionInterleavingPlanned_V1 event and must
        // return an Interleaving result with Disabled=true reason=
        // SingleOrZeroTargets (the "feature off" signal).
        var summerA = new Cena.Actors.StudentPlan.SittingCode(
            "תשפ״ו",
            Cena.Actors.StudentPlan.SittingSeason.Summer,
            Cena.Actors.StudentPlan.SittingMoed.A);
        var resolver = new InMemorySittingCanonicalDateResolver(
            new Dictionary<Cena.Actors.StudentPlan.SittingCode, DateTimeOffset>
            {
                [summerA] = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            });

        var targets = new List<Cena.Actors.StudentPlan.ExamTarget>
        {
            NewTarget("et-math", "BAGRUT_MATH_5U", summerA, weeklyHours: 5),
            NewTarget("et-phys", "BAGRUT_PHYS_5U", summerA, weeklyHours: 5),
        };

        var generator = new SessionPlanGenerator(
            planConfig: new InMemoryStudentPlanConfigService(() => FixedNow),
            abilityProvider: EmptySessionAbilityEstimateProvider.Instance,
            graphProvider: EmptyTopicPrerequisiteGraphProvider.Instance,
            overrideBridge: null,
            planReader: new InMemoryStubPlanReader(targets),
            sittingResolver: resolver,
            overrideReader: NullExamTargetOverrideReader.Instance);

        var result = await generator.GenerateAsync(StudentAnon, SessionId, FixedNow);

        Assert.True(result.Interleaving.Disabled);
        Assert.Equal(
            InterleavingDisabledReason.SingleOrZeroTargets,
            result.Interleaving.DisabledReason);
        // Plan snapshot still produced — wave-2 single-target shape.
        Assert.NotNull(result.Snapshot);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static Cena.Actors.StudentPlan.ExamTarget NewTarget(
        string id, string examCode, Cena.Actors.StudentPlan.SittingCode sitting, int weeklyHours)
        => new(
            Id: new Cena.Actors.StudentPlan.ExamTargetId(id),
            Source: Cena.Actors.StudentPlan.ExamTargetSource.Student,
            AssignedById: new Cena.Actors.StudentPlan.UserId(StudentAnon),
            EnrollmentId: null,
            ExamCode: new Cena.Actors.StudentPlan.ExamCode(examCode),
            Track: new Cena.Actors.StudentPlan.TrackCode("5U"),
            QuestionPaperCodes: Array.Empty<string>(),
            Sitting: sitting,
            PerPaperSittingOverride: null,
            WeeklyHours: weeklyHours,
            ReasonTag: null,
            CreatedAt: DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
            ArchivedAt: null);

    private sealed class InMemoryStubPlanReader : Cena.Actors.StudentPlan.IStudentPlanReader
    {
        private readonly IReadOnlyList<Cena.Actors.StudentPlan.ExamTarget> _targets;
        public InMemoryStubPlanReader(IReadOnlyList<Cena.Actors.StudentPlan.ExamTarget> t) => _targets = t;

        public Task<IReadOnlyList<Cena.Actors.StudentPlan.ExamTarget>> ListTargetsAsync(
            string studentAnonId, bool includeArchived = false, CancellationToken ct = default)
        {
            if (includeArchived) return Task.FromResult(_targets);
            return Task.FromResult<IReadOnlyList<Cena.Actors.StudentPlan.ExamTarget>>(
                _targets.Where(t => t.IsActive).ToList());
        }

        public Task<Cena.Actors.StudentPlan.ExamTarget?> FindTargetAsync(
            string studentAnonId,
            Cena.Actors.StudentPlan.ExamTargetId targetId,
            CancellationToken ct = default)
            => Task.FromResult(_targets.FirstOrDefault(t => t.Id == targetId));
    }

    private sealed class StubInterleavingInputsProvider : ISessionInterleavingInputsProvider
    {
        private readonly IReadOnlyDictionary<string, (double deficit, string[] topics)> _perTarget;
        public int CallCount { get; private set; }

        public StubInterleavingInputsProvider(
            IReadOnlyDictionary<string, (double deficit, string[] topics)> perTarget)
            => _perTarget = perTarget;

        public Task<IReadOnlyList<InterleavingTargetInput>> GetAsync(
            string studentAnonId,
            IReadOnlyList<Cena.Actors.StudentPlan.ExamTarget> activeTargets,
            SchedulerInputs baseInputs,
            CancellationToken ct = default)
        {
            CallCount++;
            var list = new List<InterleavingTargetInput>();
            foreach (var t in activeTargets)
            {
                if (!_perTarget.TryGetValue(t.Id.Value, out var cfg)) continue;
                var candidates = cfg.topics.Select(topicSlug => new PlanEntry(
                    TopicSlug: topicSlug,
                    WeekIndex: 0,
                    AllocatedTime: TimeSpan.Zero,
                    PriorityScore: 1.0,
                    WeaknessComponent: 0.5,
                    TopicWeightComponent: 0.1,
                    PrerequisiteComponent: 1.0,
                    Rationale: $"practice {topicSlug}")).ToImmutableArray();
                list.Add(new InterleavingTargetInput(
                    TargetId: t.Id,
                    Candidates: candidates,
                    WeeklyHours: t.WeeklyHours,
                    MasteryDeficit: cfg.deficit));
            }
            return Task.FromResult<IReadOnlyList<InterleavingTargetInput>>(list);
        }
    }

    private sealed class CollectingAuditSink : ISessionInterleavingAuditSink
    {
        public List<SessionInterleavingPlanned_V1> Events { get; } = new();

        public Task AppendAsync(SessionInterleavingPlanned_V1 @event, CancellationToken ct = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
}
