// =============================================================================
// Cena Platform — Arabic Translation Corpus for Seed Questions (RDY-004b)
//
// PILOT BATCH — 15 hand-crafted Math questions (3U/4U/5U span) translated
// into Modern Standard Arabic (MSA) for the Israeli-Arab student cohort.
// Follows the canonical glossary at config/glossary.json.
//
// Provenance:
//   - Translator: ai-draft (Claude Opus 4.7, 2026-04-19)
//   - Status: AWAITING native-speaker peer review before merge to prod seed
//   - Reviewers required: 2 translators fluent in Israeli Arabic math
//     conventions (per RDY-004b §4 peer-review cycle)
//   - QA: glossary-consistency verified against config/glossary.json terms
//     {معادلة, معادلة تربيعية, مشتقة, تكامل, انحدار/ميل, اللوغاريتم,
//      متباينة, نهاية, برهان, احتمال, متتالية حسابية, ترتيب,
//      انحراف معياري, مساحة, متوسط}
//
// Translation conventions applied:
//   - MSA throughout (no colloquial Levantine / Egyptian forms)
//   - Math notation stays LTR (Western Arabic numerals 0-9); the rendering
//     layer wraps math expressions in `<bdi dir="ltr">` — source strings
//     contain Latin math tokens verbatim, no RTL-override chars
//   - Gender agreement: third-person masculine singular for all implicit
//     "student" subjects in distractor rationales (الطالب → ضمير مستتر)
//   - Distractor rationales are short nominal phrases or past-tense
//     verbs, matching the terse style of the Hebrew/English seed
//   - Variables (x, y, n, k, f, ...) kept as Latin letters per Israeli-
//     Arab textbook convention (Palestinian curriculum alignment)
//
// Keys are the verbatim original-language stems from
// QuestionBankSeedData.cs — case-sensitive, Unicode-exact. The seeder
// looks up each seeded question's stem here and emits a
// LanguageVersionAdded_V1 event when a translation exists.
// =============================================================================

using System.Collections.Generic;

namespace Cena.Admin.Api;

public sealed record ArabicOption(string Label, string Text, string? Rationale);

public sealed record ArabicTranslation(
    string Stem,
    IReadOnlyList<ArabicOption> Options,
    string? Explanation,
    string TranslatedBy,
    string ReviewStatus);

public static class QuestionBankArabicTranslations
{
    /// <summary>
    /// AI-draft translator ID. Bump version when the prompt / review model
    /// changes materially (so downstream filters can re-queue for review).
    /// </summary>
    public const string DraftTranslator = "ai-draft:claude-opus-4-7:2026-04-19";

    /// <summary>
    /// "pending-review" = translated but not yet signed off by a native
    /// speaker. "approved" = two-translator peer review complete.
    /// The seeder uses this tag in the LanguageVersionAdded_V1 event's
    /// TranslatedBy field so admin analytics can filter.
    /// </summary>
    public const string StatusPendingReview = "pending-review";

    private static ArabicTranslation Draft(string stem, params ArabicOption[] options) =>
        new(stem, options, null, DraftTranslator, StatusPendingReview);

    /// <summary>
    /// Keyed by the original Hebrew / English stem exactly as it appears
    /// in QuestionBankSeedData.cs. Unicode-exact; do not re-encode math
    /// symbols. The seeder matches on string equality.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ArabicTranslation> Translations =
        new Dictionary<string, ArabicTranslation>
        {
            // ── Linear Equations (3 Units) ──────────────────────────────
            ["Solve for x: 2x + 6 = 14"] = Draft(
                "أوجد قيمة x إذا كان: 2x + 6 = 14",
                new("A", "4", null),
                new("B", "8", "جَمَع بدلاً من الطرح"),
                new("C", "3", "قسم قبل الطرح"),
                new("D", "20", "جَمَع جميع الأعداد")
            ),

            // ── Quadratic Equations (4 Units) ───────────────────────────
            ["Solve the quadratic: x\u00B2 - 5x + 6 = 0"] = Draft(
                "حلّ المعادلة التربيعية: x\u00B2 − 5x + 6 = 0",
                new("A", "x = 2, x = 3", null),
                new("B", "x = -2, x = -3", "خطأ في الإشارة"),
                new("C", "x = 1, x = 6", "تحليل خاطئ إلى عوامل"),
                new("D", "x = 5, x = 1", "خلط بين المجموع والجداء")
            ),

            // ── Derivatives (5 Units) ───────────────────────────────────
            ["Find the derivative of f(x) = x\u00B3 - 3x\u00B2 + 2x - 1"] = Draft(
                "أوجد مشتقة الدالة f(x) = x\u00B3 − 3x\u00B2 + 2x − 1",
                new("A", "3x\u00B2 - 6x + 2", null),
                new("B", "3x\u00B2 - 6x", "نسي مشتقة الحدّ الثابت"),
                new("C", "x\u00B2 - 3x + 2", "قاعدة الأسّ خاطئة"),
                new("D", "3x\u00B3 - 6x\u00B2 + 2x", "لم يُنقِص القوى")
            ),

            // ── Integrals (5 Units) ─────────────────────────────────────
            ["Calculate \u222B\u2080\u00B2 (3x\u00B2 + 1)dx"] = Draft(
                "احسب قيمة التكامل المحدّد \u222B\u2080\u00B2 (3x\u00B2 + 1)dx",
                new("A", "10", null),
                new("B", "9", "نسي الحدّ الثابت"),
                new("C", "12", "استخدم الحدّ الأعلى فقط"),
                new("D", "8", "قاعدة الأسّ خاطئة")
            ),

            // ── Linear Equations + Analytic Geometry (3 Units) ──────────
            ["Find the equation of the line through (1,3) and (4,9)"] = Draft(
                "أوجد معادلة المستقيم المارّ بالنقطتين (1, 3) و(4, 9)",
                new("A", "y = 2x + 1", null),
                new("B", "y = 3x - 1", "ميل خاطئ"),
                new("C", "y = 2x + 3", "مقطع y خاطئ"),
                new("D", "y = x + 2", "الميل = 1 خطأ")
            ),

            // ── Probability (4 Units) ───────────────────────────────────
            ["P(A\u222AB) given P(A)=0.4, P(B)=0.3, P(A\u2229B)=0.1"] = Draft(
                "أوجد P(A\u222AB) علماً أنّ P(A)=0.4 وP(B)=0.3 وP(A\u2229B)=0.1",
                new("A", "0.6", null),
                new("B", "0.7", "جمع P(A)+P(B) فقط"),
                new("C", "0.8", "جمع الاحتمالات الثلاثة"),
                new("D", "0.1", "استخدم التقاطع فقط")
            ),

            // ── Sequences (4 Units) ─────────────────────────────────────
            ["Sum of arithmetic sequence: a\u2081=3, d=4, n=20"] = Draft(
                "أوجد مجموع المتتالية الحسابية علماً أنّ a\u2081=3 وd=4 وn=20",
                new("A", "820", null),
                new("B", "80", "جمع الحدّ الأول والأخير فقط"),
                new("C", "160", "نسي القسمة على 2"),
                new("D", "400", "استخدم صيغة خاطئة")
            ),

            // ── Logarithms (5 Units) ────────────────────────────────────
            ["Solve: log\u2082(x) + log\u2082(x-2) = 3"] = Draft(
                "حلّ المعادلة: log\u2082(x) + log\u2082(x−2) = 3",
                new("A", "4", null),
                new("B", "8", "حلّ log\u2082(x)=3 مباشرةً"),
                new("C", "2", "تجاهَل اللوغاريتم الثاني"),
                new("D", "-2", "حلّ دخيل")
            ),

            // ── Combinatorics (4 Units) ─────────────────────────────────
            ["How many ways can 5 students be arranged in a line?"] = Draft(
                "بكم طريقةٍ يمكن ترتيب 5 طلاب في صفٍّ واحد؟",
                new("A", "120", null),
                new("B", "25", "استخدم n\u00B2"),
                new("C", "10", "استخدم C(5,2)"),
                new("D", "5", "لم يَضرب الأعداد")
            ),

            // ── Inequalities (3 Units) ──────────────────────────────────
            ["Find all x satisfying |2x-3| < 5"] = Draft(
                "أوجد جميع قيم x التي تُحقّق |2x−3| < 5",
                new("A", "-1 < x < 4", null),
                new("B", "x < 4", "نسي الحدّ الأيسر"),
                new("C", "-4 < x < 1", "أخطاء في الإشارة"),
                new("D", "x > -1", "نسي الحدّ الأيمن")
            ),

            // ── Limits (5 Units) ────────────────────────────────────────
            ["Evaluate lim(x\u21920) sin(3x)/x"] = Draft(
                "احسب قيمة النهاية lim(x\u21920) sin(3x)/x",
                new("A", "3", null),
                new("B", "1", "نسي العدد 3"),
                new("C", "0", "خطأ في تطبيق قاعدة لوبيتال"),
                new("D", "\u221E", "ظنّ أنّ النهاية تتباعد")
            ),

            // ── Derivatives — tangent slope (5 Units) ───────────────────
            ["Determine the slope of the tangent to y = x\u00B3 at x = 2"] = Draft(
                "أوجد ميل المماس للمنحنى y = x\u00B3 عند x = 2",
                new("A", "12", null),
                new("B", "8", "استخدم f(2) بدلاً من f'(2)"),
                new("C", "6", "مشتقة خاطئة"),
                new("D", "3", "نسي قاعدة الأسّ")
            ),

            // ── Integrals — area under curve (5 Units) ──────────────────
            ["Find the area under f(x) = 2x + 1 from x = 0 to x = 3"] = Draft(
                "أوجد المساحة المحصورة تحت منحنى الدالة f(x) = 2x + 1 بين x = 0 وx = 3",
                new("A", "12", null),
                new("B", "7", "حسب f(3) فقط"),
                new("C", "9", "نسي الحدّ الثابت"),
                new("D", "21", "ضرب f(3) في 3")
            ),

            // ── Proof Techniques (5 Units) ──────────────────────────────
            ["Prove: if n is even, then n\u00B2 is even. Which approach works?"] = Draft(
                "أثبت: إذا كان n عدداً زوجياً، فإنّ n\u00B2 زوجيٌّ أيضاً. أيّ الطرق التالية يصلح للبرهان؟",
                new("A", "برهان مباشر: ليكن n = 2k، إذاً n\u00B2 = 4k\u00B2 = 2(2k\u00B2)", null),
                new("B", "نجرّب n=2, 4, 6 ونلاحظ النمط", "الاستقراء من الأمثلة لا يُعدّ برهاناً"),
                new("C", "نفترض أنّ n\u00B2 فرديّ ونصل إلى تناقض", "فكرة صحيحة لكنّها أكثر تعقيداً من اللازم"),
                new("D", "نستخدم الاستقراء الرياضي على n", "مبالغة لإثبات هذه العبارة")
            ),

            // ── Statistics (4 Units) ────────────────────────────────────
            ["Calculate the standard deviation of: 4, 7, 2, 9, 5, 8"] = Draft(
                "احسب الانحراف المعياري للأعداد: 4, 7, 2, 9, 5, 8",
                new("A", "\u22482.4", null),
                new("B", "5.83", "هذا هو المتوسط"),
                new("C", "7", "هذا هو المدى"),
                new("D", "35", "هذا هو المجموع")
            ),
        };

    /// <summary>Count of translated questions in this batch.</summary>
    public static int Count => Translations.Count;

    /// <summary>
    /// Returns true if an Arabic translation exists for the given
    /// original-language stem. Used by the seeder to decide whether
    /// to emit a LanguageVersionAdded_V1 event.
    /// </summary>
    public static bool TryGet(string originalStem, out ArabicTranslation translation)
        => Translations.TryGetValue(originalStem, out translation!);
}
