// =============================================================================
// Cena Platform — Skill-keyed mastery dedup-invariant tests (prr-222)
//
// Verifies the core dedup rule:
//   (StudentId, ExamTargetCode, SkillCode) is unique in the projection.
//
// Also covers the VO parsers (SkillCode + ExamTargetCode) since the
// projection relies on their normalisation to collapse equivalent keys.
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Events;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public sealed class SkillKeyedMasteryDedupTests
{
    [Fact]
    public async Task Upsert_same_triple_twice_replaces_row_not_appends()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var key = MasteryKey.From("stu-1", "bagrut-math-5yu",
            "math.algebra.quadratic-equations");
        var t1 = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        var t2 = DateTimeOffset.Parse("2026-04-21T09:00:00Z");

        await store.UpsertAsync(new SkillKeyedMasteryRow(
            key, 0.4f, 1, t1, MasteryEventSource.Native));
        await store.UpsertAsync(new SkillKeyedMasteryRow(
            key, 0.55f, 2, t2, MasteryEventSource.Native));

        var rows = await store.ListByStudentAsync("stu-1");

        Assert.Single(rows);
        var row = rows.Single();
        Assert.Equal(0.55f, row.MasteryProbability);
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal(t2, row.UpdatedAt);
    }

    [Fact]
    public async Task Two_targets_same_student_same_skill_produce_two_rows()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        var key4u = MasteryKey.From("stu-1", "bagrut-math-4yu",
            "math.algebra.quadratic-equations");
        var key5u = MasteryKey.From("stu-1", "bagrut-math-5yu",
            "math.algebra.quadratic-equations");

        await store.UpsertAsync(new SkillKeyedMasteryRow(
            key4u, 0.4f, 1, t, MasteryEventSource.Native));
        await store.UpsertAsync(new SkillKeyedMasteryRow(
            key5u, 0.3f, 1, t, MasteryEventSource.Native));

        var rows = await store.ListByStudentAsync("stu-1");
        Assert.Equal(2, rows.Count);

        var fourU = await store.ListByTargetAsync(
            "stu-1", ExamTargetCode.Parse("bagrut-math-4yu"));
        Assert.Single(fourU);

        var fiveU = await store.ListByTargetAsync(
            "stu-1", ExamTargetCode.Parse("bagrut-math-5yu"));
        Assert.Single(fiveU);
    }

    [Fact]
    public async Task Upsert_rejects_probability_outside_clamp()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var key = MasteryKey.From("stu-1", "sat-math", "math.algebra.linear");
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.UpsertAsync(new SkillKeyedMasteryRow(
                key, 0.0f, 1, t, MasteryEventSource.Native)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.UpsertAsync(new SkillKeyedMasteryRow(
                key, 1.0f, 1, t, MasteryEventSource.Native)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.UpsertAsync(new SkillKeyedMasteryRow(
                key, 0.5f, -1, t, MasteryEventSource.Native)));
    }

    [Fact]
    public async Task DeleteByStudent_removes_all_and_is_idempotent()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        await store.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-1", "bagrut-math-5yu", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));
        await store.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-1", "sat-math", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));
        await store.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-2", "bagrut-math-5yu", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));

        var deleted = await store.DeleteByStudentAsync("stu-1");
        Assert.Equal(2, deleted);

        var againDeleted = await store.DeleteByStudentAsync("stu-1");
        Assert.Equal(0, againDeleted);

        var otherStudent = await store.ListByStudentAsync("stu-2");
        Assert.Single(otherStudent);
    }

    [Fact]
    public async Task DeleteByTarget_scope_is_per_target_not_per_student()
    {
        var store = new InMemorySkillKeyedMasteryStore();
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        await store.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-1", "bagrut-math-4yu", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));
        await store.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-1", "bagrut-math-5yu", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));

        var deleted = await store.DeleteByTargetAsync(
            "stu-1", ExamTargetCode.Parse("bagrut-math-4yu"));

        Assert.Equal(1, deleted);
        var remaining = await store.ListByStudentAsync("stu-1");
        Assert.Single(remaining);
        Assert.Equal("bagrut-math-5yu",
            remaining.Single().Key.ExamTargetCode.Value);
    }

    [Fact]
    public void SkillCode_parser_normalises_case_and_rejects_malformed()
    {
        Assert.Equal("math.algebra.quadratic",
            SkillCode.Parse("Math.Algebra.Quadratic").Value);
        Assert.Throws<ArgumentException>(() => SkillCode.Parse(""));
        Assert.Throws<ArgumentException>(() => SkillCode.Parse(".a.b"));
        Assert.Throws<ArgumentException>(() => SkillCode.Parse("a..b"));
        Assert.Throws<ArgumentException>(() => SkillCode.Parse("a/b"));
    }

    [Fact]
    public void ExamTargetCode_parser_normalises_case_and_rejects_malformed()
    {
        Assert.Equal("bagrut-math-5yu",
            ExamTargetCode.Parse("Bagrut-Math-5YU").Value);
        Assert.Throws<ArgumentException>(() => ExamTargetCode.Parse(""));
        Assert.Throws<ArgumentException>(() => ExamTargetCode.Parse("bagrut math"));
        Assert.Throws<ArgumentException>(() => ExamTargetCode.Parse("bagrut.math"));
    }
}
