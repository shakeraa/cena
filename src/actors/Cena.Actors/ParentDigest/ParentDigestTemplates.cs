// =============================================================================
// Cena Platform — Parent Digest: localized template strings (RDY-067 F5a Phase 1).
//
// Hand-authored text per locale. Kept separate from the renderer so the
// copy can be reviewed by Dr. Lior + Ran (shipgate sign-off) without
// changing the render logic.
//
// Discipline applied to every template below:
//   - First line is the verdict (Dr. Lior panel review: parents read 15
//     seconds, not 2 minutes).
//   - Compassionate zero-hours variant (Rami's cross-exam).
//   - No chain-counter / FOMO / comparative / loss-aversion language (GD-004).
//   - No misconception codes, no stuck-type labels (ADR-0003).
//   - No "Premium / upgrade to see more" framing.
//   - Math-content placeholders are render-time-wrapped LTR by the renderer.
// =============================================================================

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Localized template strings for the weekly parent digest.
/// All text is plain text (not HTML) — the SmtpEmailSender currently
/// sends TextPart("plain"). HTML email is a Phase-2 concern.
/// </summary>
public static class ParentDigestTemplates
{
    // ---------------------------------------------------------------------
    // English (en)
    // ---------------------------------------------------------------------
    public const string SubjectEn = "Your weekly Cena digest";

    public const string GreetingEn =
        "Hi {0},\n\nHere's a quick read on how study went this week.\n";

    public const string RowActiveEn =
        "\n{0}: {1} hour(s) this week across {2} session(s). Mastery moved +{3}. Topics practiced: {4}.";

    public const string RowBreakEn =
        "\n{0} took a break this week — that's fine. Here's what they can pick up next week.";

    public const string FooterEn =
        "\n\nTo stop these weekly emails, open the unsubscribe link at the bottom of your account settings.";

    // ---------------------------------------------------------------------
    // Arabic (ar) — Levantine / MSA register, per the arabic-math-lexicon draft.
    // ---------------------------------------------------------------------
    public const string SubjectAr = "ملخصك الأسبوعي من سِنَة";

    public const string GreetingAr =
        "مرحبًا {0}،\n\nإليك نظرة سريعة على كيف سار التعلّم هذا الأسبوع.\n";

    public const string RowActiveAr =
        "\n{0}: {1} ساعة هذا الأسبوع عبر {2} جلسة. تحسّن الإتقان بـ +{3}. المواضيع التي تمت الممارسة عليها: {4}.";

    public const string RowBreakAr =
        "\n{0} أخذ استراحة هذا الأسبوع — هذا جيد. هنا ما يمكن استئنافه الأسبوع القادم.";

    public const string FooterAr =
        "\n\nلإيقاف هذه الرسائل الأسبوعية، افتح رابط إلغاء الاشتراك من إعدادات حسابك.";

    // ---------------------------------------------------------------------
    // Hebrew (he)
    // ---------------------------------------------------------------------
    public const string SubjectHe = "העדכון השבועי שלך מסנה";

    public const string GreetingHe =
        "שלום {0},\n\nהנה סקירה מהירה על איך הלך הלימוד השבוע.\n";

    public const string RowActiveHe =
        "\n{0}: {1} שעות השבוע לאורך {2} מפגשים. השליטה עלתה ב-+{3}. נושאים שתורגלו: {4}.";

    public const string RowBreakHe =
        "\n{0} לקחו הפסקה השבוע — זה בסדר גמור. הנה מה שאפשר להמשיך איתו בשבוע הבא.";

    public const string FooterHe =
        "\n\nלהפסקת קבלת המיילים השבועיים, פתחו את קישור ביטול המינוי בהגדרות החשבון שלכם.";

    /// <summary>
    /// Pick the subject line for a locale.
    /// </summary>
    public static string Subject(DigestLocale locale) => locale switch
    {
        DigestLocale.Ar => SubjectAr,
        DigestLocale.He => SubjectHe,
        _ => SubjectEn,
    };

    /// <summary>
    /// Pick the greeting format string for a locale.
    /// </summary>
    public static string Greeting(DigestLocale locale) => locale switch
    {
        DigestLocale.Ar => GreetingAr,
        DigestLocale.He => GreetingHe,
        _ => GreetingEn,
    };

    /// <summary>
    /// Pick the active-row format string for a locale.
    /// </summary>
    public static string RowActive(DigestLocale locale) => locale switch
    {
        DigestLocale.Ar => RowActiveAr,
        DigestLocale.He => RowActiveHe,
        _ => RowActiveEn,
    };

    /// <summary>
    /// Pick the took-a-break-row format string for a locale.
    /// </summary>
    public static string RowBreak(DigestLocale locale) => locale switch
    {
        DigestLocale.Ar => RowBreakAr,
        DigestLocale.He => RowBreakHe,
        _ => RowBreakEn,
    };

    /// <summary>
    /// Pick the footer format string for a locale.
    /// </summary>
    public static string Footer(DigestLocale locale) => locale switch
    {
        DigestLocale.Ar => FooterAr,
        DigestLocale.He => FooterHe,
        _ => FooterEn,
    };
}
