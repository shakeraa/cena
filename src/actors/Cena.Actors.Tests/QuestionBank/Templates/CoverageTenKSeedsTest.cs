// =============================================================================
// Cena Platform — Coverage-over-10k-seeds test (prr-200 DoD bullet 8)
//
// Verifies that a reasonably-sized quadratic template can produce ≥N distinct
// variants over 10k seeds — the ship-gate's per-rung SLO floor.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.QuestionBank.Templates;

public sealed class CoverageTenKSeedsTest
{
    [Fact]
    public async Task Compile_QuadraticTemplate_ProducesAtLeast50Variants()
    {
        // Solve ax² + bx + c = 0 — we only assert the compiler can find ≥50
        // distinct stems, not that each root is real. The accept_shapes=Any
        // keeps all algebraic outcomes in scope for this coverage test.
        var template = new ParametricTemplate
        {
            Id = "quad_coverage",
            Version = 1,
            Subject = "math",
            Topic = "algebra.quadratics",
            Track = TemplateTrack.FiveUnit,
            Difficulty = TemplateDifficulty.Hard,
            Methodology = TemplateMethodology.Rabinovitch,
            StemTemplate = "Find the roots of {a}x^2 + {b}x + {c} = 0",
            SolutionExpr = "a + b + c",  // dummy CAS target — we're counting stems
            AcceptShapes = AcceptShape.Any,
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer,
                    IntegerMin = 1, IntegerMax = 5, IntegerExclude = new[] { 0 } },
                new ParametricSlot { Name = "b", Kind = ParametricSlotKind.Integer,
                    IntegerMin = -10, IntegerMax = 10 },
                new ParametricSlot { Name = "c", Kind = ParametricSlotKind.Integer,
                    IntegerMin = -10, IntegerMax = 10 },
            }
        };

        var compiler = new ParametricCompiler(
            new FakeParametricRenderer(), NullLogger<ParametricCompiler>.Instance);

        var report = await compiler.CompileAsync(template, baseSeed: 7, count: 50);

        Assert.Equal(50, report.AcceptedCount);
        Assert.Equal(50, report.Variants.Select(v => v.RenderedStem).Distinct().Count());
    }

    [Fact]
    public void SlotSpaceUpperBound_RespectsExcludes()
    {
        var template = new ParametricTemplate
        {
            Id = "ub_probe",
            Version = 1,
            Subject = "math",
            Topic = "algebra",
            Track = TemplateTrack.FourUnit,
            Difficulty = TemplateDifficulty.Easy,
            Methodology = TemplateMethodology.Halabi,
            StemTemplate = "{a}",
            SolutionExpr = "a",
            AcceptShapes = AcceptShape.Integer,
            Slots = new[]
            {
                new ParametricSlot { Name = "a", Kind = ParametricSlotKind.Integer,
                    IntegerMin = 0, IntegerMax = 9, IntegerExclude = new[] { 0, 1, 2 } }
            }
        };

        var bound = ParametricCompiler.ComputeSlotSpaceUpperBound(template);
        Assert.Equal(10 - 3, bound);
    }
}
