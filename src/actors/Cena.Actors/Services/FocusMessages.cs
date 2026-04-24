// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Focus UI Messages
//
// All user-facing strings for the focus/break/microbreak system.
// Keyed by locale ("he", "ar") with English fallback.
// Extracted from service logic to support localization.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Provides localized user-facing messages for the focus system.
/// Default locale is Hebrew ("he"). Arabic ("ar") is a first-class locale.
/// </summary>
public static class FocusMessages
{
    // ── Break recommendation messages ──

    public static string BreakDrifting(string locale = "he") => locale switch
    {
        "ar" => "استراحة قصيرة ستساعدك على التركيز بشكل أفضل",
        "he" => "קצת הפסקה תעזור לך להתרכז טוב יותר",
        _ => "A short break will help you focus better"
    };

    public static string BreakFatigued(string locale = "he") => locale switch
    {
        "ar" => "عمل رائع! حان وقت الاستراحة",
        "he" => "עשית עבודה מעולה! זמן להפסקה",
        _ => "Great work! Time for a break"
    };

    public static string BreakExhausted(string locale = "he") => locale switch
    {
        "ar" => "لقد عملت بجد! حان وقت راحة حقيقية",
        "he" => "עבדת קשה! זמן למנוחה אמיתית",
        _ => "You worked hard! Time for real rest"
    };

    public static string BreakDisengaged(string locale = "he") => locale switch
    {
        "ar" => "لنعود بعد الاستراحة بطاقة جديدة",
        "he" => "בוא נחזור אחרי הפסקה עם אנרגיה חדשה",
        _ => "Let's come back after a break with fresh energy"
    };

    public static string BreakGeneric(string locale = "he") => locale switch
    {
        "ar" => "استراحة قصيرة؟",
        "he" => "הפסקה קצרה?",
        _ => "Short break?"
    };

    // ── Boredom (not a break — needs challenge) ──

    public static string BoredNeedsChallenge(string locale = "he") => locale switch
    {
        "ar" => "تستحق تحديًا! لنجرب شيئًا أصعب",
        "he" => "מגיע לך אתגר! בוא ננסה משהו קשה יותר",
        _ => "You deserve a challenge! Let's try something harder"
    };

    // ── Mind-wandering nudge ──

    public static string WelcomeBackNudge(string locale = "he") => locale switch
    {
        "ar" => "!مرحبًا بعودتك! لنكمل",
        "he" => "ברוך שובך! בוא נמשיך.",
        _ => "Welcome back! Let's continue."
    };

    // ── Microbreak activity messages ──

    public static string MicrobreakStretch(string locale = "he") => locale switch
    {
        "ar" => "قف/ي لتمدد قصير لمدة 60 ثانية",
        "he" => "קום/י למתיחה קצרה של 60 שניות",
        _ => "Stand up for a 60-second stretch"
    };

    public static string MicrobreakBreathing(string locale = "he") => locale switch
    {
        "ar" => "خذ/ي 5 أنفاس عميقة",
        "he" => "קח/י 5 נשימות עמוקות",
        _ => "Take 5 deep breaths"
    };

    public static string MicrobreakLookAway(string locale = "he") => locale switch
    {
        "ar" => "انظر/ي إلى شيء بعيد لمدة 30 ثانية",
        "he" => "הסתכל/י למרחק למשך 30 שניות",
        _ => "Look at something far away for 30 seconds"
    };

    public static string MicrobreakWater(string locale = "he") => locale switch
    {
        "ar" => "اشرب/ي كوب ماء",
        "he" => "שתה/שתי כוס מים",
        _ => "Drink a glass of water"
    };

    public static string MicrobreakWalk(string locale = "he") => locale switch
    {
        "ar" => "امشِ/ي إلى المطبخ وعد/ي",
        "he" => "לך/לכי למטבח וחזרה",
        _ => "Walk to the kitchen and back"
    };

    public static string MicrobreakGeneric(string locale = "he") => locale switch
    {
        "ar" => "!استراحة قصيرة",
        "he" => "הפסקה קצרה!",
        _ => "Quick break!"
    };
}
