// =============================================================================
// Cena Platform — Offline Parametric Renderer (prr-200, CLI-ONLY)
//
// A no-sidecar stand-in for IParametricRenderer used by Cena.Tools.QuestionGen
// to count distinct variants per template without spinning up the SymPy NATS
// sidecar. Pedagogically this is NOT a correctness oracle — the production
// path routes through SymPyParametricRenderer + ICasRouterService. The CLI's
// job is coverage accounting, not CAS verification.
//
// Behaviour:
//   * Substitutes slots into stem & solution expression (shared helpers).
//   * Uses IntegerArithmeticEvaluator for pure-integer solution expressions;
//     falls back to "symbolic" shape when the solution can't be reduced to an
//     integer rational.
//   * Rejects literal /0, runtime /0, and disallowed shapes.
//
// No LLM import. This file is scanned by NoLlmInParametricPipelineTest.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Tools.QuestionGen;

internal sealed class OfflineParametricRenderer : IParametricRenderer
{
    public Task<RendererResult> RenderAsync(
        ParametricTemplate template,
        long seed,
        IReadOnlyList<ParametricSlotValue> slotValues,
        CancellationToken ct = default)
    {
        var start = Environment.TickCount64;
        try
        {
            var slotMap = slotValues.ToDictionary(v => v.Name, StringComparer.Ordinal);

            string substSolution;
            try
            {
                substSolution = ParametricRenderHelpers.SubstituteSlots(template.SolutionExpr, slotMap);
            }
            catch (Exception ex)
            {
                return Task.FromResult(new RendererResult(
                    RendererVerdict.RejectedRenderError, null, ex.Message,
                    Environment.TickCount64 - start));
            }

            if (ParametricRenderHelpers.ContainsLiteralDivideByZero(substSolution))
                return Task.FromResult(new RendererResult(
                    RendererVerdict.RejectedZeroDivisor, null,
                    $"literal /0 in '{substSolution}'",
                    Environment.TickCount64 - start));

            string canonical;
            ParametricRenderHelpers.AnswerShape shape;
            if (IntegerArithmeticEvaluator.TryEvaluate(substSolution, out var num, out var den))
            {
                if (den == 0)
                    return Task.FromResult(new RendererResult(
                        RendererVerdict.RejectedZeroDivisor, null,
                        $"runtime /0 after substitution: {substSolution}",
                        Environment.TickCount64 - start));
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

            if (shape == ParametricRenderHelpers.AnswerShape.NonFinite)
                return Task.FromResult(new RendererResult(
                    RendererVerdict.RejectedNonFinite, null, "non-finite literal",
                    Environment.TickCount64 - start));

            if (!ParametricRenderHelpers.IsShapeAccepted(shape, template.AcceptShapes))
                return Task.FromResult(new RendererResult(
                    RendererVerdict.RejectedDisallowedShape, null,
                    $"shape {shape} not in {template.AcceptShapes}",
                    Environment.TickCount64 - start));

            string stem;
            try
            {
                stem = ParametricRenderHelpers.SubstituteStem(template.StemTemplate, slotMap);
            }
            catch (Exception ex)
            {
                return Task.FromResult(new RendererResult(
                    RendererVerdict.RejectedRenderError, null, ex.Message,
                    Environment.TickCount64 - start));
            }

            var distractors = new List<ParametricDistractor>();
            foreach (var rule in template.DistractorRules)
            {
                string substD;
                try
                {
                    substD = ParametricRenderHelpers.SubstituteSlots(rule.FormulaExpr, slotMap);
                }
                catch
                {
                    continue;
                }
                if (ParametricRenderHelpers.ContainsLiteralDivideByZero(substD)) continue;

                // Evaluate distractor too so the canonical form matches the answer
                // format (e.g. "2" vs "(4/2)").
                string distCanonical;
                if (IntegerArithmeticEvaluator.TryEvaluate(substD, out var dn, out var dd) && dd != 0)
                {
                    var g = Gcd(Math.Abs(dn), Math.Abs(dd));
                    if (g > 0) { dn /= g; dd /= g; }
                    if (dd < 0) { dn = -dn; dd = -dd; }
                    distCanonical = dd == 1 ? dn.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{dn}/{dd}";
                }
                else
                {
                    distCanonical = substD;
                }

                if (!string.Equals(distCanonical, canonical, StringComparison.Ordinal))
                    distractors.Add(new ParametricDistractor(rule.MisconceptionId, distCanonical, rule.LabelHint));
            }

            var variant = new ParametricVariant(
                TemplateId: template.Id,
                TemplateVersion: template.Version,
                Seed: seed,
                SlotValues: slotValues,
                RenderedStem: stem,
                CanonicalAnswer: canonical,
                Distractors: distractors);

            return Task.FromResult(new RendererResult(
                RendererVerdict.Accepted, variant, null, Environment.TickCount64 - start));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedRenderError, null, ex.Message,
                Environment.TickCount64 - start));
        }
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
}
