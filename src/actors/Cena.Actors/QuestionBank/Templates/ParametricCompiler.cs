// =============================================================================
// Cena Platform — Parametric Compiler (prr-200, ADR-0002)
//
// Deterministic Strategy 1 generator. Given a (template, baseSeed, count),
// emits exactly `count` CAS-verified, deduped variants — or throws
// InsufficientSlotSpaceException if the template's slot space cannot.
//
// Determinism contract:
//   * RNG is `new Random(DeriveSeed(template, baseSeed, attemptIndex))`.
//   * No calls to Random.Shared / Guid.NewGuid / DateTime.UtcNow.
//   * The same (template.Id, template.Version, baseSeed, count) returns the
//     same list across runs and machines.
//
// Loop:
//   while accepted < count and attempts < budget:
//     seed_i  = derive(baseSeed, attemptIndex)
//     slots_i = draw every slot with Random(seed_i)
//     if constraints_i reject → drop, continue
//     variant_i = renderer.RenderAsync(template, seed_i, slots_i)
//     if renderer-accepted AND deduper admits → accept
//     else → drop, continue
//
// The renderer does CAS verification for us; the compiler enforces dedup.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.QuestionBank.Templates;

public sealed record ParametricCompileReport(
    string TemplateId,
    int RequestedCount,
    int AcceptedCount,
    int TotalAttempts,
    IReadOnlyList<ParametricVariant> Variants,
    IReadOnlyList<ParametricDropReason> Drops);

public sealed class ParametricCompiler
{
    private readonly IParametricRenderer _renderer;
    private readonly ILogger<ParametricCompiler> _logger;

    // Attempts-per-accepted ceiling: a well-authored template accepts ~60-80%
    // of slot draws; badly-authored templates spin. We refuse to loop more
    // than this multiplier × count, and escalate via InsufficientSlotSpace.
    public const int DefaultAttemptsBudgetMultiplier = 40;

    // Metrics — Cena.Parametric meter (prr-200 DoD).
    private static readonly Meter Meter = new("Cena.Parametric", "1.0");
    private static readonly Counter<long> CompiledTotal = Meter.CreateCounter<long>(
        "cena_parametric_compiled_total",
        description: "Parametric variants produced, bucketed by status");
    private static readonly Histogram<double> CompileDuration = Meter.CreateHistogram<double>(
        "cena_parametric_compile_duration_seconds",
        unit: "s",
        description: "End-to-end wall-clock duration of a compile call");
    private static readonly Counter<long> DropReasonTotal = Meter.CreateCounter<long>(
        "cena_parametric_drop_reason_total",
        description: "Parametric variant drops, bucketed by reason");

    public ParametricCompiler(IParametricRenderer renderer, ILogger<ParametricCompiler> logger)
    {
        _renderer = renderer;
        _logger = logger;
    }

    /// <summary>
    /// Compile up to <paramref name="count"/> variants starting from
    /// <paramref name="baseSeed"/>. Throws <see cref="InsufficientSlotSpaceException"/>
    /// when the slot space cannot support the request (no silent partial
    /// result, per TASK-PRR-200 DoD).
    /// </summary>
    public async Task<ParametricCompileReport> CompileAsync(
        ParametricTemplate template,
        long baseSeed,
        int count,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "count must be > 0");
        template.Validate();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var variants = new List<ParametricVariant>(count);
        var drops = new List<ParametricDropReason>();
        var deduper = new ParametricVariantDeduper();

        var slotSpaceUpperBound = ComputeSlotSpaceUpperBound(template);
        var attemptsBudget = Math.Max(count * DefaultAttemptsBudgetMultiplier, count + 16);
        // Cap attempts at the enumerable upper bound × a birthday-problem
        // safety factor. For a space of size N, enumerating all N values via
        // uniform sampling takes O(N·H(N)) ≈ N·ln(N) expected attempts; we
        // use 8×N which covers up to N=10_000 comfortably without spinning
        // forever on pathologically narrow slot spaces.
        if (slotSpaceUpperBound > 0 && slotSpaceUpperBound < long.MaxValue / 16)
            attemptsBudget = (int)Math.Min(Math.Max(attemptsBudget, slotSpaceUpperBound * 8),
                                           slotSpaceUpperBound * 16);

        var attempts = 0;
        for (var i = 0L; i < attemptsBudget && variants.Count < count; i++, attempts++)
        {
            ct.ThrowIfCancellationRequested();

            var derived = DeriveSeed(template.Id, template.Version, baseSeed, i);
            var rng = new Random(unchecked((int)(derived ^ (derived >> 32))));

            IReadOnlyList<ParametricSlotValue> slotValues;
            try
            {
                slotValues = DrawAllSlots(template, rng);
            }
            catch (Exception ex)
            {
                drops.Add(new ParametricDropReason(
                    ParametricDropKind.RenderError, template.Id, derived,
                    null, null, $"Slot draw failed: {ex.Message}", 0));
                DropReasonTotal.Add(1, new KeyValuePair<string, object?>("reason", "render_error"));
                continue;
            }

            // Cross-slot constraint pre-check.
            if (!EvaluateConstraints(template, slotValues, out var constraintDetail))
            {
                drops.Add(new ParametricDropReason(
                    ParametricDropKind.ConstraintRejected, template.Id, derived,
                    null, null, constraintDetail ?? "constraint predicate rejected", 0));
                DropReasonTotal.Add(1, new KeyValuePair<string, object?>("reason", "constraint"));
                continue;
            }

            RendererResult result;
            try
            {
                result = await _renderer.RenderAsync(template, derived, slotValues, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                drops.Add(new ParametricDropReason(
                    ParametricDropKind.RenderError, template.Id, derived,
                    null, null, $"Renderer threw: {ex.Message}", 0));
                DropReasonTotal.Add(1, new KeyValuePair<string, object?>("reason", "render_error"));
                continue;
            }

            if (result.Verdict != RendererVerdict.Accepted || result.Variant is null)
            {
                drops.Add(new ParametricDropReason(
                    MapVerdictToDropKind(result.Verdict), template.Id, derived,
                    null, null, result.FailureDetail ?? result.Verdict.ToString(), result.LatencyMs));
                DropReasonTotal.Add(1, new KeyValuePair<string, object?>(
                    "reason", MapVerdictToDropKind(result.Verdict).ToString().ToLowerInvariant()));
                continue;
            }

            if (!deduper.TryAdmit(result.Variant))
            {
                drops.Add(new ParametricDropReason(
                    ParametricDropKind.DuplicateCanonicalForm, template.Id, derived,
                    result.Variant.RenderedStem, result.Variant.CanonicalAnswer,
                    "canonical hash already seen in this batch", result.LatencyMs));
                DropReasonTotal.Add(1, new KeyValuePair<string, object?>("reason", "duplicate"));
                continue;
            }

            variants.Add(result.Variant);
            CompiledTotal.Add(1, new KeyValuePair<string, object?>("status", "accepted"));
        }

        sw.Stop();
        CompileDuration.Record(sw.Elapsed.TotalSeconds,
            new KeyValuePair<string, object?>("template_id", template.Id));

        if (variants.Count < count)
        {
            CompiledTotal.Add(1, new KeyValuePair<string, object?>("status", "insufficient"));
            _logger.LogWarning(
                "[PARAMETRIC_INSUFFICIENT] template={Tid} produced={Produced}/{Req} attempts={Att} bound≈{Bound}",
                template.Id, variants.Count, count, attempts, slotSpaceUpperBound);
            throw new InsufficientSlotSpaceException(
                template.Id, count, variants.Count, slotSpaceUpperBound,
                extra: $"attempted {attempts}, deduper sawUnique {deduper.UniqueCount}");
        }

        return new ParametricCompileReport(
            TemplateId: template.Id,
            RequestedCount: count,
            AcceptedCount: variants.Count,
            TotalAttempts: attempts,
            Variants: variants,
            Drops: drops);
    }

    /// <summary>
    /// Deterministic seed derivation. Combines (templateId, templateVersion,
    /// baseSeed, attemptIndex) into a single 64-bit value via a SHA-256
    /// prefix. SHA-256 is chosen over a plain xor-multiply hash because it's
    /// stable across .NET versions and platforms — GetHashCode is explicitly
    /// NOT stable across runtimes, which would break cross-process
    /// regeneration of a stored (template, seed) pair.
    /// </summary>
    internal static long DeriveSeed(string templateId, int templateVersion, long baseSeed, long attemptIndex)
    {
        var sb = new StringBuilder();
        sb.Append(templateId).Append('|').Append(templateVersion).Append('|')
          .Append(baseSeed).Append('|').Append(attemptIndex);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        // Take the first 8 bytes, little-endian, interpret as long.
        return BitConverter.ToInt64(hash, 0);
    }

    private static IReadOnlyList<ParametricSlotValue> DrawAllSlots(
        ParametricTemplate template, Random rng)
    {
        var values = new List<ParametricSlotValue>(template.Slots.Count);
        foreach (var slot in template.Slots)
            values.Add(slot.Draw(rng));
        return values;
    }

    /// <summary>
    /// Evaluate cross-slot predicate constraints. For prr-200 we implement a
    /// tiny safe subset: `name != 0`, `name != value`, `name > value`,
    /// `name >= value`, `name < value`, `name <= value`, `name == value`,
    /// and `name1 != name2`. Authors wanting richer predicates escalate to
    /// tightening slot ranges or waiting for prr-202's authoring UI.
    /// </summary>
    internal static bool EvaluateConstraints(
        ParametricTemplate template,
        IReadOnlyList<ParametricSlotValue> slotValues,
        out string? failureDetail)
    {
        failureDetail = null;
        if (template.Constraints.Count == 0) return true;

        var env = slotValues.ToDictionary(v => v.Name, v => v, StringComparer.Ordinal);

        foreach (var c in template.Constraints)
        {
            if (!EvaluateOne(c.PredicateExpr, env, out var detail))
            {
                failureDetail = $"Constraint '{c.Description}' rejected ({detail ?? c.PredicateExpr})";
                return false;
            }
        }
        return true;
    }

    private static bool EvaluateOne(
        string expr,
        Dictionary<string, ParametricSlotValue> env,
        out string? detail)
    {
        detail = null;
        if (string.IsNullOrWhiteSpace(expr)) return true;

        var tokens = expr.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 3)
        {
            detail = $"unsupported predicate shape: {expr}";
            return false;
        }
        var lhs = tokens[0]; var op = tokens[1]; var rhs = tokens[2];

        if (!env.TryGetValue(lhs, out var lv))
        {
            detail = $"unknown slot '{lhs}'";
            return false;
        }

        double l = ToDouble(lv);
        double r;
        if (env.TryGetValue(rhs, out var rv)) r = ToDouble(rv);
        else if (!double.TryParse(rhs, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out r))
        {
            detail = $"rhs '{rhs}' neither slot nor number";
            return false;
        }

        return op switch
        {
            "==" => l == r,
            "!=" => l != r,
            ">"  => l > r,
            ">=" => l >= r,
            "<"  => l < r,
            "<=" => l <= r,
            _    => throw new FormatException($"Unsupported operator '{op}'")
        };
    }

    private static double ToDouble(ParametricSlotValue v) =>
        v.Kind == ParametricSlotKind.Choice
            ? 0.0
            : (double)v.Numerator / (v.Denominator == 0 ? 1 : v.Denominator);

    /// <summary>
    /// Upper-bound estimate of the slot product. Used to set the attempts
    /// budget and the InsufficientSlotSpace bound field. Exposed to the CLI
    /// (Cena.Tools.QuestionGen) so it can print a sanity line before spinning.
    /// </summary>
    public static long ComputeSlotSpaceUpperBound(ParametricTemplate template)
    {
        BigInteger bound = 1;
        foreach (var slot in template.Slots)
        {
            var c = slot.CardinalityUpperBound();
            if (c <= 0) return 0;
            bound *= c;
            if (bound > long.MaxValue) return long.MaxValue;
        }
        return (long)bound;
    }

    private static ParametricDropKind MapVerdictToDropKind(RendererVerdict v) => v switch
    {
        RendererVerdict.RejectedZeroDivisor       => ParametricDropKind.ZeroDivisor,
        RendererVerdict.RejectedDisallowedShape   => ParametricDropKind.DisallowedShape,
        RendererVerdict.RejectedCasContradicted   => ParametricDropKind.CasContradicted,
        RendererVerdict.RejectedCasUnavailable    => ParametricDropKind.CasCircuitOpen,
        RendererVerdict.RejectedNonFinite         => ParametricDropKind.NonFiniteValue,
        RendererVerdict.RejectedRenderError       => ParametricDropKind.RenderError,
        _                                         => ParametricDropKind.RenderError
    };
}
