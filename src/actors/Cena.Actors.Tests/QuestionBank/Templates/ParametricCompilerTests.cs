// =============================================================================
// Cena Platform — ParametricCompiler tests (prr-200)
//
// Covers the DoD test matrix:
//   1. Determinism — (template, seed) → identical output across runs.
//   2. Zero-divisor rejection.
//   3. Accept-shapes=[integer] rejects rational-result combos.
//   4. Dedupe — near-duplicates are pruned.
//   5. Insufficient slot space throws with populated fields.
//   6. Drop reasons are surfaced on the report.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.QuestionBank.Templates;

public sealed class ParametricCompilerTests
{
    private static ParametricTemplate LinearEqTemplate(AcceptShape shapes = AcceptShape.Integer) => new()
    {
        Id = "linear_eq_two_step",
        Version = 1,
        Subject = "math",
        Topic = "algebra.linear_equations",
        Track = TemplateTrack.FourUnit,
        Difficulty = TemplateDifficulty.Medium,
        Methodology = TemplateMethodology.Halabi,
        StemTemplate = "Solve for x: {a}x + {b} = {c}",
        SolutionExpr = "(c - b) / a",
        VariableName = "x",
        AcceptShapes = shapes,
        Slots = new[]
        {
            new ParametricSlot
            {
                Name = "a", Kind = ParametricSlotKind.Integer,
                IntegerMin = 2, IntegerMax = 9, IntegerExclude = new[] { 0 }
            },
            new ParametricSlot
            {
                Name = "b", Kind = ParametricSlotKind.Integer,
                IntegerMin = -20, IntegerMax = 20
            },
            new ParametricSlot
            {
                Name = "c", Kind = ParametricSlotKind.Integer,
                IntegerMin = -50, IntegerMax = 50
            },
        },
        Constraints = new[] { new SlotConstraint("a nonzero", "a != 0") }
    };

    private static ParametricCompiler NewCompiler() =>
        new(new FakeParametricRenderer(), NullLogger<ParametricCompiler>.Instance);

    // ── 1. Determinism ───────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_IsDeterministic_AcrossRuns()
    {
        var template = LinearEqTemplate();

        var r1 = await NewCompiler().CompileAsync(template, baseSeed: 42, count: 10);
        var r2 = await NewCompiler().CompileAsync(template, baseSeed: 42, count: 10);
        var r3 = await NewCompiler().CompileAsync(template, baseSeed: 42, count: 10);

        var s1 = r1.Variants.Select(v => (v.Seed, v.RenderedStem, v.CanonicalAnswer)).ToArray();
        var s2 = r2.Variants.Select(v => (v.Seed, v.RenderedStem, v.CanonicalAnswer)).ToArray();
        var s3 = r3.Variants.Select(v => (v.Seed, v.RenderedStem, v.CanonicalAnswer)).ToArray();

        Assert.Equal(s1, s2);
        Assert.Equal(s1, s3);
    }

    [Fact]
    public async Task CompileAsync_DifferentBaseSeeds_ProduceDifferentVariants()
    {
        var template = LinearEqTemplate();
        var r1 = await NewCompiler().CompileAsync(template, baseSeed: 1, count: 5);
        var r2 = await NewCompiler().CompileAsync(template, baseSeed: 9999, count: 5);
        Assert.NotEqual(
            r1.Variants.Select(v => v.CanonicalAnswer).ToArray(),
            r2.Variants.Select(v => v.CanonicalAnswer).ToArray());
    }

    // ── 2. Zero-divisor rejection ───────────────────────────────────────

    [Fact]
    public async Task CompileAsync_RejectsLiteralDivideByZero()
    {
        // Template whose solution is 1/a with a's range allowing a==0.
        var template = new ParametricTemplate
        {
            Id = "zero_div_probe",
            Version = 1,
            Subject = "math",
            Topic = "algebra",
            Track = TemplateTrack.FourUnit,
            Difficulty = TemplateDifficulty.Easy,
            Methodology = TemplateMethodology.Halabi,
            StemTemplate = "What is 1/{a}?",
            SolutionExpr = "1 / a",
            AcceptShapes = AcceptShape.Any,
            Slots = new[]
            {
                // 0 explicitly in range; no exclude, so the renderer sees /0.
                new ParametricSlot
                {
                    Name = "a", Kind = ParametricSlotKind.Integer,
                    IntegerMin = 0, IntegerMax = 0
                }
            }
        };

        var compiler = NewCompiler();
        await Assert.ThrowsAsync<InsufficientSlotSpaceException>(async () =>
            await compiler.CompileAsync(template, baseSeed: 7, count: 3));
    }

    // ── 3. Accept-shapes integer-only rejects fractions ─────────────────

    [Fact]
    public async Task CompileAsync_IntegerOnly_RejectsRationalResults()
    {
        // Narrow the slot space so MANY combos produce non-integer x.
        // Solution = (c - b) / a with a ∈ [2,3], b=0, c ∈ [1,7].
        // Of 2×1×7 = 14 combos, a substantial share are non-integer.
        var template = new ParametricTemplate
        {
            Id = "integer_only_probe",
            Version = 1,
            Subject = "math",
            Topic = "algebra",
            Track = TemplateTrack.FourUnit,
            Difficulty = TemplateDifficulty.Easy,
            Methodology = TemplateMethodology.Halabi,
            StemTemplate = "Solve: {a}x + {b} = {c}",
            SolutionExpr = "(c - b) / a",
            AcceptShapes = AcceptShape.Integer,
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer, IntegerMin = 2, IntegerMax = 3 },
                new ParametricSlot { Name = "b", Kind = ParametricSlotKind.Integer, IntegerMin = 0, IntegerMax = 0 },
                new ParametricSlot { Name = "c", Kind = ParametricSlotKind.Integer, IntegerMin = 1, IntegerMax = 7 },
            }
        };

        var compiler = NewCompiler();
        var report = await compiler.CompileAsync(template, baseSeed: 1, count: 3);

        Assert.Equal(3, report.AcceptedCount);
        // Every drop cited "DisallowedShape" (from integer-only rejection).
        Assert.Contains(report.Drops, d => d.Kind == ParametricDropKind.DisallowedShape);
    }

    // ── 4. Dedupe ───────────────────────────────────────────────────────

    [Fact]
    public async Task CompileAsync_DropsDuplicateCanonicalForms()
    {
        // Slot space intentionally collapses to few canonical stems by
        // declaring a one-value slot and drawing repeatedly.
        var template = new ParametricTemplate
        {
            Id = "dedupe_probe",
            Version = 1,
            Subject = "math",
            Topic = "algebra",
            Track = TemplateTrack.FourUnit,
            Difficulty = TemplateDifficulty.Easy,
            Methodology = TemplateMethodology.Halabi,
            StemTemplate = "Compute {a} + {b}",
            SolutionExpr = "a + b",
            AcceptShapes = AcceptShape.Integer,
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer, IntegerMin = 1, IntegerMax = 2 },
                new ParametricSlot { Name = "b", Kind = ParametricSlotKind.Integer, IntegerMin = 1, IntegerMax = 2 }
            }
        };

        // 2 × 2 = 4 raw combos. Asking for 4 should always succeed; asking
        // for 5 must raise InsufficientSlotSpace (dedupe bites).
        var c1 = NewCompiler();
        var r = await c1.CompileAsync(template, baseSeed: 11, count: 4);
        Assert.Equal(4, r.AcceptedCount);
        Assert.Equal(4, r.Variants.Select(v => v.RenderedStem).Distinct().Count());

        var c2 = NewCompiler();
        await Assert.ThrowsAsync<InsufficientSlotSpaceException>(async () =>
            await c2.CompileAsync(template, baseSeed: 11, count: 5));
    }

    // ── 5. Insufficient slot space throws with populated fields ─────────

    [Fact]
    public async Task CompileAsync_InsufficientSpace_ThrowsWithFields()
    {
        var template = new ParametricTemplate
        {
            Id = "tiny",
            Version = 1,
            Subject = "math",
            Topic = "algebra",
            Track = TemplateTrack.FourUnit,
            Difficulty = TemplateDifficulty.Easy,
            Methodology = TemplateMethodology.Halabi,
            StemTemplate = "x + {a} = ?",
            SolutionExpr = "a",
            AcceptShapes = AcceptShape.Integer,
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer, IntegerMin = 0, IntegerMax = 2 }
            }
        };

        var ex = await Assert.ThrowsAsync<InsufficientSlotSpaceException>(async () =>
            await NewCompiler().CompileAsync(template, baseSeed: 0, count: 100));

        Assert.Equal("tiny", ex.TemplateId);
        Assert.Equal(100, ex.Requested);
        Assert.True(ex.Produced < 100);
        Assert.True(ex.SlotSpaceUpperBound > 0);
    }

    // ── 6. Drop reasons flow through to report ──────────────────────────

    [Fact]
    public async Task CompileAsync_ReportsDropReasons()
    {
        var template = new ParametricTemplate
        {
            Id = "drops_probe",
            Version = 1,
            Subject = "math",
            Topic = "algebra",
            Track = TemplateTrack.FourUnit,
            Difficulty = TemplateDifficulty.Easy,
            Methodology = TemplateMethodology.Halabi,
            StemTemplate = "{a} / {b}",
            SolutionExpr = "a / b",
            AcceptShapes = AcceptShape.Integer,
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer, IntegerMin = 1, IntegerMax = 5 },
                new ParametricSlot { Name = "b", Kind = ParametricSlotKind.Integer, IntegerMin = 1, IntegerMax = 5 }
            }
        };

        var report = await NewCompiler().CompileAsync(template, baseSeed: 4, count: 3);
        Assert.Equal(3, report.AcceptedCount);
        // Many non-multiples of b got dropped as DisallowedShape.
        Assert.True(report.Drops.Count > 0);
        Assert.All(report.Drops, d => Assert.False(string.IsNullOrWhiteSpace(d.Detail)));
    }
}
