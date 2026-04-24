// =============================================================================
// Cena Platform — Misconception taxonomy (EPIC-PRR-J PRR-370/371/374)
//
// Closed-set mapping from a CAS-detected break-signature → one of a curated
// list of misconception templates. Per the 10-persona review (persona #7
// ML-safety): the LLM never narrates freeform — narration must come from
// a template. If nothing matches confidently, we return a conservative
// "let me check with your teacher" fallback rather than fabricate.
//
// v1 ships the taxonomy as code constants so every change lands via PR
// with SME review (memory "No stubs — production grade").
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>High-level categories of CAS breaks that map to templates.</summary>
public enum MisconceptionBreakType
{
    SignFlipDistributive,
    MinusAsSubtractionConfusion,
    PrematureCancellation,
    FactoringIncomplete,
    FoilMistake,
    ExponentRuleMisapplication,
    FractionOverFraction,
    QuadraticFormulaSignError,
    RadicalSimplification,
    TrigSignSlip,
    LogarithmRuleMisapplication,
    Other,
}

/// <summary>
/// Curated student-facing explanation + remediation hint. All three locales
/// stay in sync; this is canonical content, not a fallback.
/// </summary>
public sealed record MisconceptionTemplate(
    string TemplateId,
    MisconceptionBreakType BreakType,
    string ExplanationHe,
    string ExplanationAr,
    string ExplanationEn,
    string CounterExampleLatex,
    string SuggestedNextStep,
    double MinConfidence);

/// <summary>
/// Closed-set taxonomy of Bagrut Math 4-unit misconception templates.
/// Every change is a PR reviewed by a math-education SME.
/// </summary>
public static class BagrutMath4MisconceptionTaxonomy
{
    public static readonly MisconceptionTemplate SignFlipDistributive = new(
        TemplateId: "mc-bag4-001-sign-flip-distributive",
        BreakType: MisconceptionBreakType.SignFlipDistributive,
        ExplanationHe: "חלוקת סימן המינוס לתוך סוגריים — המינוס מחלק על כל איבר בתוך הסוגריים.",
        ExplanationAr: "توزيع إشارة الناقص داخل الأقواس — تذكّر أن إشارة الناقص توزّع على كل حد داخل الأقواس.",
        ExplanationEn: "Distributing the minus into the parentheses — the minus applies to every term inside.",
        CounterExampleLatex: "-(a + b) = -a - b \\text{  (not  } -a + b \\text{)}",
        SuggestedNextStep: "Rewrite the step and distribute the minus carefully before combining like terms.",
        MinConfidence: 0.70);

    public static readonly MisconceptionTemplate PrematureCancellation = new(
        TemplateId: "mc-bag4-002-premature-cancellation",
        BreakType: MisconceptionBreakType.PrematureCancellation,
        ExplanationHe: "צמצום מוקדם מדי — אפשר לצמצם רק גורם שמופיע כמכפלה בכל המונה והמכנה.",
        ExplanationAr: "اختصار سابق لأوانه — يمكن الاختصار فقط لعامل يظهر ضربًا في البسط والمقام كليهما.",
        ExplanationEn: "Premature cancellation — you can only cancel a factor that multiplies BOTH numerator and denominator.",
        CounterExampleLatex: "\\frac{a+b}{a} \\neq 1+b \\text{  (a is not a factor of the numerator)}",
        SuggestedNextStep: "Factor numerator and denominator first; then cancel only shared factors.",
        MinConfidence: 0.70);

    public static readonly MisconceptionTemplate FoilMistake = new(
        TemplateId: "mc-bag4-003-foil-mistake",
        BreakType: MisconceptionBreakType.FoilMistake,
        ExplanationHe: "פתיחת סוגריים — כפל כל איבר מהסוגריים הראשונים בכל איבר מהשניים.",
        ExplanationAr: "فك الأقواس — اضرب/ي كل حد من القوس الأول بكل حد من القوس الثاني.",
        ExplanationEn: "Expanding parentheses — multiply EVERY term in the first parenthesis by EVERY term in the second.",
        CounterExampleLatex: "(a+b)(c+d) = ac + ad + bc + bd",
        SuggestedNextStep: "Expand with FOIL (First, Outer, Inner, Last) and collect like terms.",
        MinConfidence: 0.70);

    public static readonly MisconceptionTemplate ExponentRuleMisapplication = new(
        TemplateId: "mc-bag4-004-exponent-rule",
        BreakType: MisconceptionBreakType.ExponentRuleMisapplication,
        ExplanationHe: "חזקה של סכום — שים/י לב ש-$(a+b)^2$ אינו $a^2+b^2$.",
        ExplanationAr: "قوة مجموع — تذكّر أن $(a+b)^2$ لا تساوي $a^2+b^2$.",
        ExplanationEn: "Exponent of a sum — $(a+b)^2$ is NOT $a^2+b^2$.",
        CounterExampleLatex: "(a+b)^2 = a^2 + 2ab + b^2",
        SuggestedNextStep: "Expand to (a+b)(a+b) and FOIL it out.",
        MinConfidence: 0.70);

    public static readonly MisconceptionTemplate QuadraticFormulaSignError = new(
        TemplateId: "mc-bag4-005-quadratic-formula-sign",
        BreakType: MisconceptionBreakType.QuadraticFormulaSignError,
        ExplanationHe: "נוסחת השורשים — בדוק/י את סימן ה-$b$: $x=\\frac{-b \\pm \\sqrt{b^2-4ac}}{2a}$.",
        ExplanationAr: "قانون الحل — تحقّق/ي من إشارة $b$: $x=\\frac{-b \\pm \\sqrt{b^2-4ac}}{2a}$.",
        ExplanationEn: "Quadratic formula — check the sign of $b$: $x=\\frac{-b \\pm \\sqrt{b^2-4ac}}{2a}$.",
        CounterExampleLatex: "x=\\frac{-b \\pm \\sqrt{b^2-4ac}}{2a}",
        SuggestedNextStep: "Plug a, b, c back in with the correct signs — watch for double negatives.",
        MinConfidence: 0.70);

    public static readonly MisconceptionTemplate FractionOverFraction = new(
        TemplateId: "mc-bag4-006-fraction-over-fraction",
        BreakType: MisconceptionBreakType.FractionOverFraction,
        ExplanationHe: "שבר חלקי שבר — $\\frac{a/b}{c/d} = \\frac{a \\cdot d}{b \\cdot c}$.",
        ExplanationAr: "كسر على كسر — $\\frac{a/b}{c/d} = \\frac{a \\cdot d}{b \\cdot c}$.",
        ExplanationEn: "Fraction over fraction — $\\frac{a/b}{c/d} = \\frac{a \\cdot d}{b \\cdot c}$ (invert & multiply).",
        CounterExampleLatex: "\\frac{a/b}{c/d} = \\frac{a}{b} \\cdot \\frac{d}{c} = \\frac{ad}{bc}",
        SuggestedNextStep: "Invert the bottom fraction and multiply.",
        MinConfidence: 0.70);

    /// <summary>Every template in the taxonomy.</summary>
    public static readonly IReadOnlyList<MisconceptionTemplate> All = new[]
    {
        SignFlipDistributive,
        PrematureCancellation,
        FoilMistake,
        ExponentRuleMisapplication,
        QuadraticFormulaSignError,
        FractionOverFraction,
    };

    /// <summary>Look up candidate templates by break-type.</summary>
    public static IEnumerable<MisconceptionTemplate> FindCandidates(MisconceptionBreakType breakType) =>
        All.Where(t => t.BreakType == breakType);
}
