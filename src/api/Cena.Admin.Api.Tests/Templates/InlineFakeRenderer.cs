// =============================================================================
// Cena Platform — Inline fake parametric renderer (prr-202 test support)
//
// Deterministic in-memory IParametricRenderer used by the authoring-service
// preview tests. Does NOT call a real CAS sidecar; evaluates tiny integer-
// rational arithmetic in-process via IntegerArithmeticEvaluator.
//
// Mirror of FakeParametricRenderer in Cena.Actors.Tests. Duplicated here
// because that class is `internal sealed` inside an InternalsVisibleTo scope
// that doesn't include Cena.Admin.Api.Tests. Keeping it in this project avoids
// cross-project internals sharing.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Admin.Api.Tests.Templates;

internal sealed class InlineFakeRenderer : IParametricRenderer
{
    public Task<RendererResult> RenderAsync(
        ParametricTemplate template, long seed,
        IReadOnlyList<ParametricSlotValue> slotValues, CancellationToken ct = default)
    {
        var slotMap = slotValues.ToDictionary(s => s.Name, StringComparer.Ordinal);
        string subst;
        try { subst = ParametricRenderHelpers.SubstituteSlots(template.SolutionExpr, slotMap); }
        catch (Exception ex)
        {
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedRenderError, null, ex.Message, 0));
        }
        if (ParametricRenderHelpers.ContainsLiteralDivideByZero(subst))
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedZeroDivisor, null, $"/0 in {subst}", 0));

        if (!IntegerArithmeticEvaluator.TryEvaluate(subst, out var num, out var den))
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedDisallowedShape, null, "non-numeric", 0));
        if (den == 0)
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedZeroDivisor, null, "runtime /0", 0));

        var g = Gcd(Math.Abs(num), Math.Abs(den));
        if (g > 0) { num /= g; den /= g; }
        var shape = den == 1 ? ParametricRenderHelpers.AnswerShape.Integer
                             : ParametricRenderHelpers.AnswerShape.Rational;
        if (!ParametricRenderHelpers.IsShapeAccepted(shape, template.AcceptShapes))
            return Task.FromResult(new RendererResult(
                RendererVerdict.RejectedDisallowedShape, null,
                $"shape {shape} not in {template.AcceptShapes}", 0));

        var canonical = den == 1 ? num.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                 : $"{num}/{den}";
        var stem = ParametricRenderHelpers.SubstituteStem(template.StemTemplate, slotMap);
        return Task.FromResult(new RendererResult(
            RendererVerdict.Accepted,
            new ParametricVariant(template.Id, template.Version, seed,
                slotValues, stem, canonical, Array.Empty<ParametricDistractor>()),
            null, 0));
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
}
