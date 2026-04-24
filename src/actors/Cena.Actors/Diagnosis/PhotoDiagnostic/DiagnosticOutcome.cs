// =============================================================================
// Cena Platform — DiagnosticOutcome + DiagnosticNarration (EPIC-PRR-J PRR-380/382)
//
// Student-facing output types for the photo-diagnostic pipeline.
//
// Layers:
//   - DiagnosticOutcome: rich record with verdict + per-step chain + matched
//     template + confidence advice + audit flag. This is what the API emits
//     (trimmed of PII) and what the UI renders.
//   - DiagnosticNarration: localized text block derived from the matched
//     template. Pre-baked for HE/AR/EN so the UI just picks by locale.
//
// Framed around PRR-380 (reflection gate — student must see what they did
// before they see what was wrong) and PRR-382 (show-my-work — full
// per-step trail, not just "step N is wrong").
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Top-level verdict class for the whole diagnostic.</summary>
public enum DiagnosticVerdict
{
    /// <summary>Chain is fully valid — student's solution is correct.</summary>
    ChainValid,
    /// <summary>A specific step is wrong; Narration describes why.</summary>
    FirstWrongStep,
    /// <summary>OCR or template confidence too low; conservative fallback shown.</summary>
    LowConfidenceFallback,
    /// <summary>No template matched the break-type; conservative fallback shown.</summary>
    NoTemplateMatch,
    /// <summary>Fewer than 2 steps extracted — nothing to verify.</summary>
    NotEnoughSteps,
}

/// <summary>Locale-packaged narration block. UI picks by student locale.</summary>
public sealed record DiagnosticNarration(
    string He,
    string Ar,
    string En,
    string CounterExampleLatex,
    string SuggestedNextStep)
{
    /// <summary>Conservative "check with your teacher" fallback narration.</summary>
    public static DiagnosticNarration CheckWithTeacherFallback { get; } = new(
        He: "לא הצלחתי לזהות בבטחה את השלב שעורר קושי — כדאי להתייעץ עם המורה/ה שלך.",
        Ar: "لم أتمكن من تحديد الخطوة الخاطئة بثقة — يُفضّل مراجعة معلّمك/معلّمتك.",
        En: "I couldn't confidently identify the misstep — consider reviewing it with your teacher.",
        CounterExampleLatex: string.Empty,
        SuggestedNextStep: "Walk through the steps with your teacher so we can learn together next time.");

    /// <summary>Narration when chain is fully valid.</summary>
    public static DiagnosticNarration ChainValidCongrats { get; } = new(
        He: "כל המעברים מאומתים — פתרון נכון. כל הכבוד!",
        Ar: "جميع الخطوات صحيحة — حلّ سليم. أحسنت!",
        En: "All steps check out — your solution is correct. Nice work.",
        CounterExampleLatex: string.Empty,
        SuggestedNextStep: "Try a harder problem next to keep stretching.");

    /// <summary>Produce a narration from a matched template.</summary>
    public static DiagnosticNarration FromTemplate(MisconceptionTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new DiagnosticNarration(
            He: template.ExplanationHe,
            Ar: template.ExplanationAr,
            En: template.ExplanationEn,
            CounterExampleLatex: template.CounterExampleLatex,
            SuggestedNextStep: template.SuggestedNextStep);
    }
}

/// <summary>Full diagnostic outcome; consumed by the API endpoint / UI.</summary>
public sealed record DiagnosticOutcome(
    string DiagnosticId,
    DiagnosticVerdict Verdict,
    /// <summary>1-indexed step number where the first wrong transition landed. Null when ChainValid.</summary>
    int? FirstWrongStepNumber,
    StepChainVerificationResult Chain,
    /// <summary>Matched template id (or null if no match / fallback used).</summary>
    string? MatchedTemplateId,
    DiagnosticNarration Narration,
    PhotoDiagnosticAdvice Advice,
    /// <summary>True when the sampler flagged this for retrospective SME review.</summary>
    bool FlaggedForAudit);
