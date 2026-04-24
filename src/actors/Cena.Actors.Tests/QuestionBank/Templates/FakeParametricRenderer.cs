// =============================================================================
// Cena Platform — Test-only parametric renderer (prr-200)
//
// In-memory deterministic IParametricRenderer for unit tests. Substitutes slot
// values into stem/solution/distractor expressions and performs a tiny
// arithmetic evaluator for integer-only slot tuples so the compiler-side tests
// can exercise dedupe, determinism, and shape-gate behaviour WITHOUT standing
// up a SymPy sidecar.
//
// Evaluator scope (intentionally limited):
//   * integer literals (no decimals, no symbolic constants)
//   * binary + - * / with the standard operator precedence
//   * parentheses, unary +/-
// Any expression that does not parse under this grammar is treated as
// "symbolic" — the fake admits it only when the template declares
// AcceptShape.Symbolic or AcceptShape.Any.
//
// Production path: SymPyParametricRenderer via ICasRouterService. That seam is
// tested separately in SymPyParametricRendererTests with a stubbed router.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.Tests.QuestionBank.Templates;

internal sealed class FakeParametricRenderer : IParametricRenderer
{
    public Func<ParametricTemplate, long, IReadOnlyList<ParametricSlotValue>, Task<RendererResult>>? Override { get; set; }

    public Task<RendererResult> RenderAsync(
        ParametricTemplate template,
        long seed,
        IReadOnlyList<ParametricSlotValue> slotValues,
        CancellationToken ct = default)
    {
        if (Override is not null) return Override(template, seed, slotValues);

        var slotMap = slotValues.ToDictionary(s => s.Name, StringComparer.Ordinal);

        string substSolution;
        try
        {
            substSolution = ParametricRenderHelpers.SubstituteSlots(template.SolutionExpr, slotMap);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedRenderError, null, ex.Message, 0));
        }

        if (ParametricRenderHelpers.ContainsLiteralDivideByZero(substSolution))
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedZeroDivisor, null,
                $"/0 in {substSolution}", 0));

        string canonical;
        ParametricRenderHelpers.AnswerShape shape;
        if (IntegerArithmeticEvaluator.TryEvaluate(substSolution, out var num, out var den))
        {
            if (den == 0)
                return Task.FromResult(new RendererResult(
                    RendererVerdict.RejectedZeroDivisor, null, "runtime /0", 0));
            var g = Gcd(Math.Abs(num), Math.Abs(den));
            if (g > 0) { num /= g; den /= g; }
            if (den < 0) { num = -num; den = -den; }
            if (den == 1)
            {
                canonical = num.ToString(System.Globalization.CultureInfo.InvariantCulture);
                shape = ParametricRenderHelpers.AnswerShape.Integer;
            }
            else
            {
                canonical = $"{num}/{den}";
                shape = ParametricRenderHelpers.AnswerShape.Rational;
            }
        }
        else
        {
            canonical = substSolution;
            shape = ParametricRenderHelpers.AnswerShape.Symbolic;
        }

        if (!ParametricRenderHelpers.IsShapeAccepted(shape, template.AcceptShapes))
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedDisallowedShape, null,
                $"shape {shape} not in {template.AcceptShapes}", 0));

        var stem = ParametricRenderHelpers.SubstituteStem(template.StemTemplate, slotMap);

        var distractors = new List<ParametricDistractor>();
        foreach (var rule in template.DistractorRules)
        {
            string d;
            try { d = ParametricRenderHelpers.SubstituteSlots(rule.FormulaExpr, slotMap); }
            catch { continue; }
            if (ParametricRenderHelpers.ContainsLiteralDivideByZero(d)) continue;
            distractors.Add(new ParametricDistractor(rule.MisconceptionId, d, rule.LabelHint));
        }

        return Task.FromResult(new RendererResult(
            RendererVerdict.Accepted,
            new ParametricVariant(
                template.Id, template.Version, seed,
                slotValues, stem, canonical, distractors),
            null, 0));
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
}

// Integer-rational evaluator now lives at
// Cena.Actors.QuestionBank.Templates.IntegerArithmeticEvaluator — a single
// shared implementation used by SymPy/Offline renderers and this test fake.

