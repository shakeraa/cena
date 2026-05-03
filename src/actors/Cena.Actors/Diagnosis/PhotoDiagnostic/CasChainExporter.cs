// =============================================================================
// Cena Platform — CasChainExporter (EPIC-PRR-J PRR-363)
//
// Serializes a StepChainVerificationResult (from IStepChainVerifier) into
// the wire DTO the teacher-facing "show my work" view renders. Per-step
// fields + locale-resolved operation labels (expand / factor / simplify /
// equate / rearrange) in en / he / ar. Pure function; no I/O; trivially
// testable.
//
// Why a dedicated exporter: StepChainVerificationResult carries the rich
// verifier trace (CasResult per transition, outcome enum, summary string)
// but the UI needs a locale-normalized, PII-free, stable-ordered shape
// the teacher can screen-read. The verifier must not know the locale,
// and the UI must not know the CAS plumbing — so the exporter is the
// boundary.
//
// Labels: v1 ships three locales with the same four operation classes
// (expand, factor, simplify, equate). Rearrangement is a fifth — it's
// the "moved terms across =" operation that we don't classify beyond
// "step transition". Unknown operations render with a neutral label
// ("step") so a SymPy output the exporter does not recognise still
// renders cleanly instead of crashing.
// =============================================================================

using Cena.Actors.Cas;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Locale tag the exporter understands.</summary>
public enum CasChainLocale
{
    /// <summary>English (LTR).</summary>
    En,
    /// <summary>Hebrew (RTL).</summary>
    He,
    /// <summary>Arabic (RTL).</summary>
    Ar,
}

/// <summary>
/// Wire-format record for a single step in the exported chain. One row
/// per canonical student step. Latex remains the source-of-truth form;
/// the UI wraps this in &lt;bdi dir="ltr"&gt; per memory "Math always LTR".
/// </summary>
public sealed record CasChainExportStep(
    int Index,
    string Latex,
    string Canonical,
    double Confidence);

/// <summary>
/// Wire-format record for a single verified transition between two steps.
/// </summary>
public sealed record CasChainExportTransition(
    int FromIndex,
    int ToIndex,
    string OutcomeCode,     // "valid" | "wrong" | "unfollowable_skip" | "low_confidence"
    string OperationLabel,  // locale-resolved e.g., "Expand", "פתיחה", "توسيع"
    string? OperationCode,  // stable machine code: "expand" | "factor" | "simplify" | ...
    string Summary,         // verifier's human-visible summary, already PII-free
    CasVerifyMethodCode? VerifyMethod,  // MathNet | SymPy | Fallback — renders as a badge
    bool Holds);            // true if CasResult confirmed equivalence, false otherwise

/// <summary>Verify-method projected into a stable wire code.</summary>
public enum CasVerifyMethodCode
{
    MathNet,
    SymPy,
    Fallback,
}

/// <summary>Top-level export envelope.</summary>
public sealed record CasChainExport(
    IReadOnlyList<CasChainExportStep> Steps,
    IReadOnlyList<CasChainExportTransition> Transitions,
    int? FirstFailureIndex,
    bool Succeeded,
    string Locale);

/// <summary>Pure exporter. No I/O, no dependencies.</summary>
public static class CasChainExporter
{
    /// <summary>
    /// Project a <see cref="StepChainVerificationResult"/> + its original
    /// step list into the wire DTO. The steps argument must be the SAME
    /// list the verifier consumed — indexes must match.
    /// </summary>
    public static CasChainExport Export(
        IReadOnlyList<ExtractedStep> steps,
        StepChainVerificationResult verification,
        CasChainLocale locale = CasChainLocale.En)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(verification);

        var exportedSteps = steps
            .OrderBy(s => s.Index)
            .Select(s => new CasChainExportStep(
                Index: s.Index,
                Latex: s.Latex,
                Canonical: s.Canonical,
                Confidence: s.Confidence))
            .ToList();

        var transitions = verification.Transitions
            .Select(t => ExportTransition(t, locale))
            .ToList();

        return new CasChainExport(
            Steps: exportedSteps,
            Transitions: transitions,
            FirstFailureIndex: verification.FirstFailureIndex,
            Succeeded: verification.Succeeded,
            Locale: locale.ToString().ToLowerInvariant());
    }

    /// <summary>Public helper for ad-hoc locale toggling from tests.</summary>
    public static string LabelFor(string? operationCode, CasChainLocale locale) =>
        (operationCode ?? "step", locale) switch
        {
            ("expand",         CasChainLocale.En) => "Expand",
            ("expand",         CasChainLocale.He) => "פתיחה",
            ("expand",         CasChainLocale.Ar) => "توسيع",
            ("factor",         CasChainLocale.En) => "Factor",
            ("factor",         CasChainLocale.He) => "פירוק לגורמים",
            ("factor",         CasChainLocale.Ar) => "تحليل",
            ("simplify",       CasChainLocale.En) => "Simplify",
            ("simplify",       CasChainLocale.He) => "פישוט",
            ("simplify",       CasChainLocale.Ar) => "تبسيط",
            ("equate",         CasChainLocale.En) => "Equate",
            ("equate",         CasChainLocale.He) => "השוואה",
            ("equate",         CasChainLocale.Ar) => "مساواة",
            ("rearrange",      CasChainLocale.En) => "Rearrange",
            ("rearrange",      CasChainLocale.He) => "סידור מחדש",
            ("rearrange",      CasChainLocale.Ar) => "إعادة ترتيب",
            (_,                CasChainLocale.En) => "Step",
            (_,                CasChainLocale.He) => "שלב",
            (_,                CasChainLocale.Ar) => "خطوة",
        };

    private static CasChainExportTransition ExportTransition(
        StepTransitionResult t, CasChainLocale locale)
    {
        var opCode = InferOperationCode(t);
        return new CasChainExportTransition(
            FromIndex: t.FromStepIndex,
            ToIndex: t.ToStepIndex,
            OutcomeCode: t.Outcome switch
            {
                StepTransitionOutcome.Valid => "valid",
                StepTransitionOutcome.Wrong => "wrong",
                StepTransitionOutcome.UnfollowableSkip => "unfollowable_skip",
                StepTransitionOutcome.LowConfidence => "low_confidence",
                _ => "unknown",
            },
            OperationLabel: LabelFor(opCode, locale),
            OperationCode: opCode,
            Summary: t.Summary ?? string.Empty,
            VerifyMethod: MapVerifyMethod(t.CasResult),
            Holds: t.CasResult?.Verified ?? false);
    }

    private static string? InferOperationCode(StepTransitionResult t)
    {
        // V1 heuristic: look for the operation name inside the verifier's
        // Summary string; if it's not one of the known classes, return
        // null and the exporter falls back to the neutral "step" label.
        var s = (t.Summary ?? string.Empty).ToLowerInvariant();
        if (s.Contains("expand")) return "expand";
        if (s.Contains("factor")) return "factor";
        if (s.Contains("simplif")) return "simplify";
        if (s.Contains("equate") || s.Contains("both sides")) return "equate";
        if (s.Contains("rearrang") || s.Contains("move term")) return "rearrange";
        return null;
    }

    private static CasVerifyMethodCode? MapVerifyMethod(CasVerifyResult? r)
    {
        if (r is null) return null;
        // CasVerifyResult carries an Engine string — project to the enum
        // by a case-insensitive prefix match so a future Engine value
        // extension doesn't crash this exporter.
        var m = r.Engine?.ToLowerInvariant() ?? string.Empty;
        if (m.StartsWith("mathnet")) return CasVerifyMethodCode.MathNet;
        if (m.StartsWith("sympy")) return CasVerifyMethodCode.SymPy;
        return CasVerifyMethodCode.Fallback;
    }
}
