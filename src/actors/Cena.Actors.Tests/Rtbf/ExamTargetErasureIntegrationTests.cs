// =============================================================================
// Cena Platform — ExamTarget erasure end-to-end test (prr-223)
//
// Simulates the full cascade scenario from the task body:
//   Create a student with 2 targets + mastery rows + retention opt-in →
//   erase → verify all target-adjacent rows / opt-ins are unreadable.
//
// Event-stream crypto-shred (ADR-0038) is outside this test's scope — it's
// covered by the KeyStore integration tests; here we verify the
// projection-delete cascade deterministically.
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

public sealed class ExamTargetErasureIntegrationTests
{
    [Fact]
    public async Task Student_with_two_targets_fully_erased_end_to_end()
    {
        var mastery = new InMemorySkillKeyedMasteryStore();
        var extension = new InMemoryExamTargetRetentionExtensionStore();
        var cascade = new ExamTargetErasureCascade(
            mastery,
            extension,
            NullLogger<ExamTargetErasureCascade>.Instance);
        var now = DateTimeOffset.Parse("2026-04-20T09:00:00Z");

        // Seed: student has two active exam targets (Bagrut Math 5U + PET Quant).
        // Three skills per target → six mastery rows total.
        var targets = new[]
        {
            ExamTargetCode.Parse("bagrut-math-5yu"),
            ExamTargetCode.Parse("pet-quant"),
        };
        var skills = new[]
        {
            SkillCode.Parse("math.algebra.quadratic"),
            SkillCode.Parse("math.geometry.pythagoras"),
            SkillCode.Parse("math.functions.linear"),
        };
        foreach (var tgt in targets)
        foreach (var sk in skills)
        {
            await mastery.UpsertAsync(new SkillKeyedMasteryRow(
                new MasteryKey("stu-erase-1", tgt, sk),
                MasteryProbability: 0.42f,
                AttemptCount: 5,
                UpdatedAt: now,
                Source: MasteryEventSource.Native));
        }

        // Seed retention extension + a "bystander" student whose data must survive.
        await extension.SetAsync(new ExamTargetRetentionExtension(
            "stu-erase-1", now, now.AddYears(5)));
        await mastery.UpsertAsync(new SkillKeyedMasteryRow(
            new MasteryKey(
                "stu-bystander",
                ExamTargetCode.Parse("bagrut-math-5yu"),
                SkillCode.Parse("math.algebra.quadratic")),
            0.5f, 3, now, MasteryEventSource.Native));

        Assert.Equal(6,
            (await mastery.ListByStudentAsync("stu-erase-1")).Count);

        // Act: erase
        var result = await cascade.EraseForStudentAsync(
            "stu-erase-1", CancellationToken.None);

        // Assert: erased student has no rows, extension cleared.
        Assert.Empty(await mastery.ListByStudentAsync("stu-erase-1"));
        Assert.Null(await extension.TryGetAsync("stu-erase-1"));
        Assert.Equal(ErasureAction.Deleted, result.Action);
        Assert.True(result.Count >= 6);

        // Assert: bystander survives
        var bystanderRows = await mastery.ListByStudentAsync("stu-bystander");
        Assert.Single(bystanderRows);
    }

    [Fact]
    public async Task Cascade_runs_even_when_student_has_no_rows()
    {
        var cascade = new ExamTargetErasureCascade(
            new InMemorySkillKeyedMasteryStore(),
            new InMemoryExamTargetRetentionExtensionStore(),
            NullLogger<ExamTargetErasureCascade>.Instance);

        var result = await cascade.EraseForStudentAsync(
            "stu-never-seen", CancellationToken.None);

        Assert.Equal(0, result.Count);
        Assert.Equal(ErasureAction.Deleted, result.Action);
    }
}
