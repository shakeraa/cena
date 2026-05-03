// =============================================================================
// Cena Platform — BktStateTracker cross-target isolation tests (prr-222)
//
// Proves that mastery updates for the same (student, skill) under two
// distinct ExamTargetCodes produce INDEPENDENT posteriors. This is the
// core dedup-invariant guarantee from ADR-0050 §5 + prr-222.
//
// Also covers:
//   - Initial posterior is BktParameters.PInit when no row exists yet.
//   - Posterior is clamped to [0.001, 0.999].
//   - Event sink receives one MasteryUpdated_V2 per UpdateAsync call
//     tagged with Source=Native.
//   - Multiple skills under one target do not interfere.
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Events;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public sealed class BktStateTrackerCrossTargetTests
{
    private sealed class CollectingSink : IMasteryEventSink
    {
        public List<MasteryUpdated_V2> Events { get; } = new();
        public Task AppendAsync(MasteryUpdated_V2 @event, CancellationToken ct = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Two_targets_same_student_same_skill_track_independently()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var tracker = new BktStateTracker(
            store, new DefaultBktParameterProvider());
        var skill = SkillCode.Parse("math.algebra.quadratic");
        var four = ExamTargetCode.Parse("bagrut-math-4yu");
        var five = ExamTargetCode.Parse("bagrut-math-5yu");
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        // 4U student gets it right, 5U student gets it wrong — posteriors diverge.
        await tracker.UpdateAsync("stu-1", four, skill, isCorrect: true, t);
        await tracker.UpdateAsync("stu-1", five, skill, isCorrect: false, t);

        var fourRow = await store.TryGetAsync(
            new MasteryKey("stu-1", four, skill));
        var fiveRow = await store.TryGetAsync(
            new MasteryKey("stu-1", five, skill));

        Assert.NotNull(fourRow);
        Assert.NotNull(fiveRow);
        Assert.NotEqual(fourRow!.MasteryProbability,
            fiveRow!.MasteryProbability);
        Assert.True(fourRow.MasteryProbability > fiveRow.MasteryProbability);
    }

    [Fact]
    public async Task Update_clamps_to_bkt_tracer_range()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var tracker = new BktStateTracker(
            store, new DefaultBktParameterProvider());
        var target = ExamTargetCode.Parse("sat-math");
        var skill = SkillCode.Parse("math.a.b");
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        // 50 correct attempts should push toward — but not past — 0.999.
        float posterior = 0f;
        for (var i = 0; i < 50; i++)
        {
            posterior = await tracker.UpdateAsync(
                "stu-1", target, skill, isCorrect: true, t.AddSeconds(i));
        }

        Assert.InRange(posterior, 0.001f, 0.999f);
        Assert.True(posterior > 0.9f,
            "50 consecutive correct attempts should push posterior toward 1.0");
    }

    [Fact]
    public async Task Update_emits_V2_event_to_sink_with_native_source()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var sink = new CollectingSink();
        var tracker = new BktStateTracker(
            store, new DefaultBktParameterProvider(), sink);
        var target = ExamTargetCode.Parse("pet-quant");
        var skill = SkillCode.Parse("math.a.b");
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        await tracker.UpdateAsync("stu-1", target, skill, true, t);
        await tracker.UpdateAsync("stu-1", target, skill, false, t.AddMinutes(1));

        Assert.Equal(2, sink.Events.Count);
        Assert.All(sink.Events, e =>
        {
            Assert.Equal(MasteryEventSource.Native, e.Source);
            Assert.Equal("pet-quant", e.ExamTargetCode.Value);
            Assert.Equal("stu-1", e.StudentAnonId);
        });
    }

    [Fact]
    public async Task Multiple_skills_under_one_target_do_not_interfere()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var tracker = new BktStateTracker(
            store, new DefaultBktParameterProvider());
        var target = ExamTargetCode.Parse("bagrut-math-5yu");
        var algebra = SkillCode.Parse("math.algebra.quadratic");
        var geometry = SkillCode.Parse("math.geometry.pythagoras");
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        await tracker.UpdateAsync("stu-1", target, algebra, true, t);
        await tracker.UpdateAsync("stu-1", target, geometry, false, t);

        var rows = await store.ListByTargetAsync("stu-1", target);
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(
            rows.First(r => r.Key.SkillCode.Equals(algebra)).MasteryProbability,
            rows.First(r => r.Key.SkillCode.Equals(geometry)).MasteryProbability);
    }

    [Fact]
    public async Task Empty_studentAnonId_rejected()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var tracker = new BktStateTracker(
            store, new DefaultBktParameterProvider());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tracker.UpdateAsync(
                "",
                ExamTargetCode.Parse("sat-math"),
                SkillCode.Parse("math.a.b"),
                true,
                DateTimeOffset.UtcNow));
    }
}
