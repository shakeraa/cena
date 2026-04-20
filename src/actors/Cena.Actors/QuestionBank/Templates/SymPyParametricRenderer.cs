// =============================================================================
// Cena Platform — SymPy Parametric Renderer (prr-200, ADR-0002, ADR-0032)
//
// The only production implementation of IParametricRenderer. Calls the CAS
// router (ICasRouterService) to canonicalise every substituted solution and
// every distractor formula. Rejects slot combos whose canonicalised answer:
//   * Contains a zero divisor detectable from the raw substitution
//   * Lies outside the template's accept_shapes (e.g. 7/3 when integer-only)
//   * The CAS router flags as non-finite
//
// No LLM import. No heuristic answer-shape guessing — CAS is authoritative.
// Substitution + shape helpers live in ParametricRenderHelpers so the CLI
// (Cena.Tools.QuestionGen) can share the parsing logic without taking a
// dependency on this file's NATS wiring.
// =============================================================================

using System.Diagnostics;
using System.Text.RegularExpressions;
using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.QuestionBank.Templates;

public sealed class SymPyParametricRenderer : IParametricRenderer
{
    private readonly ICasRouterService _cas;
    private readonly ILogger<SymPyParametricRenderer> _logger;

    public SymPyParametricRenderer(
        ICasRouterService cas,
        ILogger<SymPyParametricRenderer> logger)
    {
        _cas = cas;
        _logger = logger;
    }

    public async Task<RendererResult> RenderAsync(
        ParametricTemplate template,
        long seed,
        IReadOnlyList<ParametricSlotValue> slotValues,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var slotMap = slotValues.ToDictionary(s => s.Name, StringComparer.Ordinal);

            // 1. Substitute solution expression + pre-CAS zero-divisor screen.
            string substitutedSolution;
            try
            {
                substitutedSolution = ParametricRenderHelpers.SubstituteSlots(template.SolutionExpr, slotMap);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new RendererResult(
                    RendererVerdict.RejectedRenderError, null,
                    $"Slot substitution failed: {ex.Message}", sw.Elapsed.TotalMilliseconds);
            }

            if (ParametricRenderHelpers.ContainsLiteralDivideByZero(substitutedSolution))
            {
                sw.Stop();
                return new RendererResult(
                    RendererVerdict.RejectedZeroDivisor, null,
                    $"solution_expr yields division by zero after substitution: {substitutedSolution}",
                    sw.Elapsed.TotalMilliseconds);
            }

            // 2. Render the stem.
            string renderedStem;
            try
            {
                renderedStem = ParametricRenderHelpers.SubstituteStem(template.StemTemplate, slotMap);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new RendererResult(
                    RendererVerdict.RejectedRenderError, null,
                    $"Stem substitution failed: {ex.Message}", sw.Elapsed.TotalMilliseconds);
            }

            // 3. CAS NormalForm canonicalisation on the substituted solution.
            var canonRequest = new CasVerifyRequest(
                Operation: CasOperation.NormalForm,
                ExpressionA: substitutedSolution,
                ExpressionB: null,
                Variable: template.VariableName,
                Tolerance: 1e-9);

            CasVerifyResult canonResult;
            try
            {
                canonResult = await _cas.VerifyAsync(canonRequest, ct);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex,
                    "[PARAMETRIC_RENDER_CAS_ERROR] template={Tid} seed={Seed}",
                    template.Id, seed);
                return new RendererResult(
                    RendererVerdict.RejectedCasUnavailable, null,
                    $"CAS unavailable: {ex.Message}", sw.Elapsed.TotalMilliseconds);
            }

            if (canonResult.Status == CasVerifyStatus.CircuitBreakerOpen)
            {
                sw.Stop();
                return new RendererResult(
                    RendererVerdict.RejectedCasUnavailable, null,
                    "CAS circuit-open — dropping variant (Strategy 1 requires CAS-authoritative).",
                    sw.Elapsed.TotalMilliseconds);
            }

            if (canonResult.Status != CasVerifyStatus.Ok || !canonResult.Verified)
            {
                sw.Stop();
                return new RendererResult(
                    RendererVerdict.RejectedCasContradicted, null,
                    $"CAS could not canonicalise solution: {canonResult.ErrorMessage}",
                    sw.Elapsed.TotalMilliseconds);
            }

            var canonicalAnswer = canonResult.SimplifiedA ?? substitutedSolution;

            // 4. Shape gate.
            var shape = ParametricRenderHelpers.ClassifyAnswerShape(canonicalAnswer);
            if (shape == ParametricRenderHelpers.AnswerShape.NonFinite)
            {
                sw.Stop();
                return new RendererResult(
                    RendererVerdict.RejectedNonFinite, null,
                    $"Canonical answer '{canonicalAnswer}' is non-finite (NaN / Infinity).",
                    sw.Elapsed.TotalMilliseconds);
            }
            if (!ParametricRenderHelpers.IsShapeAccepted(shape, template.AcceptShapes))
            {
                sw.Stop();
                return new RendererResult(
                    RendererVerdict.RejectedDisallowedShape, null,
                    $"Canonical answer '{canonicalAnswer}' is shape {shape} but template accepts {template.AcceptShapes}.",
                    sw.Elapsed.TotalMilliseconds);
            }

            // 5. Distractors — one pass each through the CAS router.
            var distractors = new List<ParametricDistractor>(template.DistractorRules.Count);
            foreach (var rule in template.DistractorRules)
            {
                string substDistractor;
                try
                {
                    substDistractor = ParametricRenderHelpers.SubstituteSlots(rule.FormulaExpr, slotMap);
                }
                catch
                {
                    distractors.Add(new ParametricDistractor(rule.MisconceptionId, rule.FormulaExpr, rule.LabelHint));
                    continue;
                }

                if (ParametricRenderHelpers.ContainsLiteralDivideByZero(substDistractor))
                    continue;

                var distReq = new CasVerifyRequest(
                    Operation: CasOperation.NormalForm,
                    ExpressionA: substDistractor,
                    ExpressionB: null,
                    Variable: template.VariableName,
                    Tolerance: 1e-9);

                CasVerifyResult distResult;
                try
                {
                    distResult = await _cas.VerifyAsync(distReq, ct);
                }
                catch
                {
                    distResult = CasVerifyResult.Error(CasOperation.NormalForm, "SymPy", 0, "distractor-cas-error");
                }

                var distText = distResult.Status == CasVerifyStatus.Ok && distResult.Verified
                    ? distResult.SimplifiedA ?? substDistractor
                    : substDistractor;

                if (!AreSymbolicallyEqual(distText, canonicalAnswer))
                    distractors.Add(new ParametricDistractor(rule.MisconceptionId, distText, rule.LabelHint));
            }

            sw.Stop();
            var variant = new ParametricVariant(
                TemplateId: template.Id,
                TemplateVersion: template.Version,
                Seed: seed,
                SlotValues: slotValues,
                RenderedStem: renderedStem,
                CanonicalAnswer: canonicalAnswer,
                Distractors: distractors);

            return new RendererResult(RendererVerdict.Accepted, variant, null, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "[PARAMETRIC_RENDER_UNEXPECTED] template={Tid} seed={Seed}", template.Id, seed);
            return new RendererResult(
                RendererVerdict.RejectedRenderError, null,
                ex.Message, sw.Elapsed.TotalMilliseconds);
        }
    }

    private static bool AreSymbolicallyEqual(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        static string N(string x) => Regex.Replace(x, @"\s+", "").Trim('(', ')');
        return string.Equals(N(a), N(b), StringComparison.Ordinal);
    }
}
