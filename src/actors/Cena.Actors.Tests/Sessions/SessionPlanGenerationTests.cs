// =============================================================================
// Cena Platform — prr-149 SessionPlanGenerator + session-scope tests
//
// Covers the three prr-149 DoD points:
//   1. New session → a plan is generated via AdaptiveScheduler; snapshot
//      contains ordered PlanEntry list (highest priority first).
//   2. Plan respects DeadlineUtc supplied via IStudentPlanConfigService.
//   3. Architecturally, SessionPlanSnapshot does not leak onto any
//      persistent student-keyed surface (StudentState, StudentProfileSnapshot,
//      or other *ProfileSnapshot/*State types).
// =============================================================================

using System.Collections.Immutable;
using System.Reflection;
using Cena.Actors.Mastery;
using Cena.Actors.Sessions;

namespace Cena.Actors.Tests.Sessions;

public sealed class SessionPlanGenerationTests
{
    private const string StudentAnon = "anon-stu-prr149-01";
    private const string SessionId = "sess-prr149-01";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);

    // ── Test 1 — plan is generated and ordered correctly ─────────────────

    [Fact]
    public async Task GenerateAsync_NewSession_ProducesPriorityOrderedPlan()
    {
        // Arrange: student is weak on derivatives (high priority), strong
        // on sequences (low priority). AdaptiveScheduler should surface
        // derivatives first.
        var abilities = new Dictionary<string, AbilityEstimate>
        {
            ["derivatives"] = NewEstimate("derivatives", theta: -0.8, samples: 12),
            ["sequences"]   = NewEstimate("sequences",   theta:  0.6, samples: 20),
            ["probability"] = NewEstimate("probability", theta: -0.1, samples: 15),
        };

        var generator = new SessionPlanGenerator(
            planConfig: new InMemoryStudentPlanConfigService(() => FixedNow),
            abilityProvider: new StubAbilityProvider(abilities),
            graphProvider: EmptyTopicPrerequisiteGraphProvider.Instance);

        // Act
        var result = await generator.GenerateAsync(StudentAnon, SessionId, FixedNow);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(StudentAnon, result.Snapshot.StudentAnonId);
        Assert.Equal(SessionId, result.Snapshot.SessionId);
        Assert.Equal(FixedNow, result.Snapshot.GeneratedAtUtc);
        Assert.Equal(MotivationProfile.Neutral, result.Snapshot.MotivationProfile);

        // Every topic that has both an estimate AND a Bagrut weight should
        // appear; ordering must be descending priority score.
        Assert.NotEmpty(result.Snapshot.PriorityOrdered);
        for (var i = 1; i < result.Snapshot.PriorityOrdered.Length; i++)
        {
            Assert.True(
                result.Snapshot.PriorityOrdered[i - 1].PriorityScore
                >= result.Snapshot.PriorityOrdered[i].PriorityScore,
                "PlanEntry list must be priority-ordered descending.");
        }

        // Derivatives (most negative θ → highest weakness) should be ahead of
        // sequences (positive θ → weakness clamped to 0, priority collapses).
        var derivativesIdx = IndexOf(result.Snapshot.PriorityOrdered, "derivatives");
        var sequencesIdx = IndexOf(result.Snapshot.PriorityOrdered, "sequences");
        Assert.True(derivativesIdx >= 0, "derivatives should be scheduled");
        // sequences has weakness = 0 so its priority score is 0; the
        // scheduler keeps it in the output but at the bottom.
        Assert.True(derivativesIdx < sequencesIdx || sequencesIdx < 0,
            "derivatives must be prioritised above sequences.");
    }

    // ── Test 2 — plan respects DeadlineUtc from config ───────────────────

    [Fact]
    public async Task GenerateAsync_SuppliedDeadline_IsReflectedInSnapshot()
    {
        var studentDeadline = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var config = new StubPlanConfigService(new StudentPlanConfig(
            DeadlineUtc: studentDeadline,
            WeeklyBudget: TimeSpan.FromHours(8),
            MotivationProfile: MotivationProfile.Confident));

        var generator = new SessionPlanGenerator(
            planConfig: config,
            abilityProvider: new StubAbilityProvider(new Dictionary<string, AbilityEstimate>
            {
                ["derivatives"] = NewEstimate("derivatives", theta: -0.5, samples: 10),
            }),
            graphProvider: EmptyTopicPrerequisiteGraphProvider.Instance);

        var result = await generator.GenerateAsync(StudentAnon, SessionId, FixedNow);

        Assert.Equal(studentDeadline, result.Snapshot.DeadlineUtc);
        Assert.Equal(MotivationProfile.Confident, result.Snapshot.MotivationProfile);
        Assert.Equal(8 * 60, result.Snapshot.WeeklyBudgetMinutes);
        // Not the fallback — the bridge should have resolved real inputs.
        Assert.Equal("student-plan-config", result.InputsSource);
    }

    [Fact]
    public async Task GenerateAsync_FallbackConfig_TagsInputsSourceAsDefault()
    {
        // The in-memory fallback always returns canonical defaults — the
        // generator must tag those with "default-fallback" so ops can
        // measure prr-148 adoption.
        var generator = new SessionPlanGenerator(
            planConfig: new InMemoryStudentPlanConfigService(() => FixedNow),
            abilityProvider: new StubAbilityProvider(new Dictionary<string, AbilityEstimate>
            {
                ["probability"] = NewEstimate("probability", theta: -0.3, samples: 8),
            }),
            graphProvider: EmptyTopicPrerequisiteGraphProvider.Instance);

        var result = await generator.GenerateAsync(StudentAnon, SessionId, FixedNow);

        Assert.Equal("default-fallback", result.InputsSource);
        Assert.NotNull(result.Snapshot.DeadlineUtc);
        // Fallback deadline is 12 weeks from "now".
        Assert.Equal(FixedNow + TimeSpan.FromDays(7 * 12), result.Snapshot.DeadlineUtc);
    }

    // ── Test 3 — session-scope architecture guard ────────────────────────

    [Fact]
    public void SessionPlanSnapshot_DoesNotAppearOnAnyPersistentStudentSurface()
    {
        // Scan every type in the Cena.Actors assembly whose simple name
        // looks like a persistent student-keyed aggregate/snapshot
        // (*ProfileSnapshot, *State on StudentActor-track types, etc.).
        // None of them may declare a property of type SessionPlanSnapshot
        // or a string/object field that names "SessionPlanSnapshot" in
        // its signature.
        var actorsAssembly = typeof(SessionPlanSnapshot).Assembly;

        var persistentTypes = actorsAssembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(IsPersistentStudentSurface)
            .ToList();

        Assert.NotEmpty(persistentTypes); // sanity — we did find some surfaces

        var violations = new List<string>();
        foreach (var type in persistentTypes)
        {
            foreach (var prop in type.GetProperties(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (IsSessionPlanType(prop.PropertyType))
                    violations.Add(
                        $"{type.FullName}.{prop.Name} — property type " +
                        $"{prop.PropertyType.FullName} is session-scoped " +
                        "(SessionPlanSnapshot / SessionPlanDocument / " +
                        "SessionPlanComputed_V1) and MUST NOT live on " +
                        "persistent student-keyed state. See prr-149 + ADR-0003.");
            }

            foreach (var field in type.GetFields(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (IsSessionPlanType(field.FieldType))
                    violations.Add(
                        $"{type.FullName}.{field.Name} — field type " +
                        $"{field.FieldType.FullName} is session-scoped.");
            }
        }

        if (violations.Count > 0)
            Assert.Fail(
                "prr-149 session-scope violation: SessionPlanSnapshot leaked " +
                "onto a persistent student-keyed surface." + Environment.NewLine +
                string.Join(Environment.NewLine, violations));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static AbilityEstimate NewEstimate(string topic, double theta, int samples) =>
        new(
            StudentAnonId: StudentAnon,
            TopicSlug: topic,
            Theta: theta,
            StandardError: 0.2,
            SampleSize: samples,
            ComputedAtUtc: FixedNow,
            ObservationWindowWeeks: 4);

    private static int IndexOf(ImmutableArray<PlanEntry> entries, string topicSlug)
    {
        for (var i = 0; i < entries.Length; i++)
            if (entries[i].TopicSlug == topicSlug) return i;
        return -1;
    }

    private static bool IsPersistentStudentSurface(Type t)
    {
        var name = t.Name;
        var ns = t.Namespace ?? string.Empty;

        // We care about the persistent aggregates/snapshots that live
        // under the Students bounded context + shared event DTOs.
        if (ns.StartsWith("Cena.Actors.Students", StringComparison.Ordinal))
        {
            return name.EndsWith("State", StringComparison.Ordinal)
                   || name.EndsWith("Snapshot", StringComparison.Ordinal)
                   || name.EndsWith("Profile", StringComparison.Ordinal);
        }
        if (ns.StartsWith("Cena.Actors.Events", StringComparison.Ordinal))
        {
            return name.EndsWith("Snapshot", StringComparison.Ordinal)
                   || name.EndsWith("Profile", StringComparison.Ordinal);
        }
        return false;
    }

    private static bool IsSessionPlanType(Type t)
    {
        if (t is null) return false;
        if (t == typeof(SessionPlanSnapshot)) return true;
        if (t == typeof(SessionPlanDocument)) return true;
        if (t == typeof(Cena.Actors.Sessions.Events.SessionPlanComputed_V1)) return true;
        if (t == typeof(Cena.Actors.Sessions.Events.SessionPlanTopicEntry_V1)) return true;
        if (t.IsGenericType)
        {
            foreach (var arg in t.GetGenericArguments())
                if (IsSessionPlanType(arg)) return true;
        }
        if (t.IsArray && t.GetElementType() is { } elem && IsSessionPlanType(elem))
            return true;
        return false;
    }

    // ── Stubs ────────────────────────────────────────────────────────────

    private sealed class StubAbilityProvider : ISessionAbilityEstimateProvider
    {
        private readonly IReadOnlyDictionary<string, AbilityEstimate> _map;
        public StubAbilityProvider(IReadOnlyDictionary<string, AbilityEstimate> map) => _map = map;
        public Task<IReadOnlyDictionary<string, AbilityEstimate>> GetAsync(
            string studentAnonId, CancellationToken ct = default) => Task.FromResult(_map);
    }

    private sealed class StubPlanConfigService : IStudentPlanConfigService
    {
        private readonly StudentPlanConfig _cfg;
        public StubPlanConfigService(StudentPlanConfig cfg) => _cfg = cfg;
        public Task<StudentPlanConfig> GetAsync(
            string studentAnonId, CancellationToken ct = default) => Task.FromResult(_cfg);
    }
}
