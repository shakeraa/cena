// =============================================================================
// Cena Platform — ExamTargetErasureCascade tests (prr-223)
//
// Covers:
//   - The cascade deletes per-student mastery rows across ALL their targets.
//   - The cascade deletes the retention-extension opt-in row.
//   - The cascade's ProjectionName matches the arch-test allow-list entry.
//   - The cascade is idempotent — a second call for the same student
//     returns Count=0 and does not throw.
//   - The cascade rejects empty studentId.
//   - One student's data is untouched by another student's erasure.
// =============================================================================

using Cena.Actors.ExamTargets;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Events;
using Cena.Actors.Retention;
using Cena.Actors.Rtbf;
using Cena.Infrastructure.Compliance;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Rtbf;

public sealed class ExamTargetErasureCascadeTests
{
    private static ExamTargetErasureCascade NewCascade(
        out InMemorySkillKeyedMasteryStore mastery,
        out InMemoryExamTargetRetentionExtensionStore extension)
    {
        mastery = new InMemorySkillKeyedMasteryStore();
        extension = new InMemoryExamTargetRetentionExtensionStore();
        return new ExamTargetErasureCascade(
            mastery,
            extension,
            NullLogger<ExamTargetErasureCascade>.Instance);
    }

    [Fact]
    public void ProjectionName_matches_stable_constant()
    {
        var c = NewCascade(out _, out _);
        Assert.Equal(
            ExamTargetErasureCascade.StableName,
            c.ProjectionName);
        Assert.Equal("ExamTargetProjections", c.ProjectionName);
    }

    [Fact]
    public async Task Cascade_removes_mastery_rows_across_all_targets()
    {
        var cascade = NewCascade(out var mastery, out _);
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        await mastery.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-1", "bagrut-math-5yu", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));
        await mastery.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-1", "sat-math", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));
        await mastery.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-2", "bagrut-math-5yu", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));

        var result = await cascade.EraseForStudentAsync("stu-1", CancellationToken.None);

        Assert.Equal(ErasureAction.Deleted, result.Action);
        Assert.True(result.Count >= 2);
        Assert.Empty(await mastery.ListByStudentAsync("stu-1"));
        Assert.Single(await mastery.ListByStudentAsync("stu-2"));
    }

    [Fact]
    public async Task Cascade_removes_retention_extension_opt_in()
    {
        var cascade = NewCascade(out _, out var ext);
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        await ext.SetAsync(new ExamTargetRetentionExtension(
            "stu-1", t, t.AddYears(5)));

        await cascade.EraseForStudentAsync("stu-1", CancellationToken.None);

        Assert.Null(await ext.TryGetAsync("stu-1"));
    }

    [Fact]
    public async Task Cascade_is_idempotent()
    {
        var cascade = NewCascade(out var mastery, out _);
        var t = DateTimeOffset.Parse("2026-04-20T09:00:00Z");
        await mastery.UpsertAsync(new SkillKeyedMasteryRow(
            MasteryKey.From("stu-1", "sat-math", "math.a.b"),
            0.4f, 1, t, MasteryEventSource.Native));

        var first = await cascade.EraseForStudentAsync("stu-1", CancellationToken.None);
        var second = await cascade.EraseForStudentAsync("stu-1", CancellationToken.None);

        Assert.Equal(1, first.Count);
        Assert.Equal(0, second.Count);
        // Idempotent re-invocation does not throw.
    }

    [Fact]
    public async Task Cascade_rejects_empty_student_id()
    {
        var cascade = NewCascade(out _, out _);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            cascade.EraseForStudentAsync("", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            cascade.EraseForStudentAsync("   ", CancellationToken.None));
    }

    [Fact]
    public async Task Cascade_details_mentions_append_only_crypto_shred_coverage()
    {
        var cascade = NewCascade(out _, out _);
        var result = await cascade.EraseForStudentAsync("stu-1", CancellationToken.None);

        Assert.NotNull(result.Details);
        // The manifest reader relies on the details string to prove the
        // append-only ExamTarget* stream is covered by ADR-0038 crypto-shred.
        Assert.Contains("ADR-0038", result.Details!, StringComparison.Ordinal);
    }
}
