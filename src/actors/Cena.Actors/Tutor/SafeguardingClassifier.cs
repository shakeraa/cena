// =============================================================================
// Cena Platform -- Safeguarding Classifier (FIND-privacy-008)
// Content classifier for student input that detects safeguarding concerns
// (self-harm, abuse, bullying, suicide, predatory contact).
//
// Runs BEFORE the LLM call. On a positive classification the LLM call is
// skipped, the message is NOT stored, and a SafeguardingAlert is created
// and routed to a moderation queue.
//
// Design: keyword + pattern based, no LLM dependency. Fast, deterministic,
// auditable. Supports English, Hebrew, Arabic keywords.
// =============================================================================

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Tutor;

/// <summary>
/// Severity of a safeguarding concern.
/// </summary>
public enum SafeguardingSeverity
{
    /// <summary>No concern detected.</summary>
    None = 0,

    /// <summary>Low-level concern: potentially inappropriate topic but not harmful.</summary>
    Low = 1,

    /// <summary>Medium concern: bullying, intimidation, emotional distress.</summary>
    Medium = 2,

    /// <summary>High concern: self-harm, suicidal ideation, abuse disclosure, predatory contact.</summary>
    High = 3
}

/// <summary>
/// Result of a safeguarding classification scan.
/// </summary>
public sealed record SafeguardingResult(
    bool IsConcern,
    SafeguardingSeverity Severity,
    IReadOnlyList<string> TriggeredCategories,
    string? SuggestedResponse);

/// <summary>
/// Alert document created when a safeguarding concern is detected.
/// Routed to moderation queue. Does NOT include the student message content.
/// </summary>
public sealed record SafeguardingAlert(
    string AlertId,
    string StudentId,
    DateTimeOffset Timestamp,
    SafeguardingSeverity Severity,
    IReadOnlyList<string> TriggeredCategories,
    string ThreadId,
    string? Market);

/// <summary>
/// Domain event emitted when a safeguarding concern is detected.
/// Content is NOT included -- only metadata for audit.
/// </summary>
public sealed record SafeguardingConcernRaised_V1(
    string StudentId,
    string ThreadId,
    DateTimeOffset Timestamp,
    SafeguardingSeverity Severity,
    IReadOnlyList<string> TriggeredCategories)
    : Cena.Actors.Events.IDelegatedEvent;

/// <summary>
/// Scans student input text for safeguarding concerns.
/// </summary>
public interface ISafeguardingClassifier
{
    /// <summary>
    /// Scan student input for safeguarding-relevant content.
    /// </summary>
    SafeguardingResult Scan(string studentInput);

    /// <summary>
    /// Get the localized "talk to a trusted adult" response for the given market.
    /// </summary>
    string GetSafeguardingResponse(string? market);
}

public sealed class SafeguardingClassifier : ISafeguardingClassifier
{
    private readonly ILogger<SafeguardingClassifier> _logger;

    // ── HIGH severity: self-harm, suicide, abuse, predatory ──────────────

    private static readonly string[] HighSeverityEnglish =
    [
        "i want to hurt myself",
        "i want to kill myself",
        "i want to die",
        "i don't want to live",
        "i don't want to be alive",
        "end my life",
        "commit suicide",
        "committing suicide",
        "kill myself",
        "cutting myself",
        "self harm",
        "self-harm",
        "suicidal",
        "someone is hurting me",
        "someone touches me",
        "someone is touching me",
        "being abused",
        "my parent hits me",
        "my dad hits me",
        "my mom hits me",
        "my teacher hits me",
        "i am being hit",
        "molested",
        "sexual abuse",
        "i was raped",
        "send me a photo of yourself",
        "send nudes",
        "adult asked me to",
        "stranger online",
        "meet up in secret"
    ];

    private static readonly string[] HighSeverityHebrew =
    [
        "אני רוצה להרוג את עצמי",
        "אני רוצה למות",
        "לא רוצה לחיות",
        "לפגוע בעצמי",
        "התאבדות",
        "חותך את עצמי",
        "חותכת את עצמי",
        "מישהו פוגע בי",
        "מישהו נוגע בי",
        "אבא מכה אותי",
        "אמא מכה אותי",
        "המורה מכה אותי",
        "פגיעה מינית",
        "הטרדה מינית"
    ];

    private static readonly string[] HighSeverityArabic =
    [
        "اريد ان اقتل نفسي",
        "أريد أن أموت",
        "لا أريد العيش",
        "إيذاء نفسي",
        "انتحار",
        "شخص يؤذيني",
        "شخص يلمسني",
        "أبي يضربني",
        "أمي تضربني",
        "المعلم يضربني",
        "اعتداء جنسي",
        "تحرش جنسي"
    ];

    // ── MEDIUM severity: bullying, emotional distress ─────────────────────

    private static readonly string[] MediumSeverityEnglish =
    [
        "being bullied",
        "they bully me",
        "everyone hates me",
        "nobody likes me",
        "i hate my life",
        "i feel worthless",
        "i am worthless",
        "want to run away",
        "running away from home",
        "i'm scared of",
        "afraid to go to school",
        "afraid to go home",
        "threatened me",
        "blackmailing me",
        "cyberbullying"
    ];

    private static readonly string[] MediumSeverityHebrew =
    [
        "מציקים לי",
        "בריונות",
        "כולם שונאים אותי",
        "אף אחד לא אוהב אותי",
        "אני שונא את החיים שלי",
        "שונאת את החיים שלי",
        "רוצה לברוח מהבית",
        "אני מפחד",
        "אני מפחדת",
        "מאיימים עליי"
    ];

    private static readonly string[] MediumSeverityArabic =
    [
        "يتنمرون علي",
        "الجميع يكرهني",
        "لا أحد يحبني",
        "أكره حياتي",
        "أريد الهرب",
        "خائف من المدرسة",
        "يهددونني"
    ];

    // ── Regex patterns for less literal expressions ──────────────────────

    private static readonly Regex SelfHarmRegex = new(
        @"\b(?:cut(?:ting)?\s+(?:my\s+)?(?:wrist|arm|leg|thigh)s?|overdose|swallow(?:ed)?\s+pills|jump\s+off|hang\s+myself)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Per-market helpline mapping ──────────────────────────────────────

    private static readonly Dictionary<string, (string Name, string Number, string Url)> Helplines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GB"] = ("NSPCC Childline", "0800 1111", "https://www.childline.org.uk"),
        ["UK"] = ("NSPCC Childline", "0800 1111", "https://www.childline.org.uk"),
        ["US"] = ("Childhelp National Child Abuse Hotline", "1-800-422-4453", "https://www.childhelp.org"),
        ["IL"] = ("ERAN", "1201", "https://www.eran.org.il"),
        ["AE"] = ("Child Protection Center", "800-988", "https://www.mofaic.gov.ae"),
    };

    private const string DefaultHelplineResponse =
        "It sounds like you might be going through something difficult. " +
        "Please talk to a trusted adult -- a parent, teacher, school counsellor, or another adult you trust. " +
        "If you need immediate help, please contact your local child helpline.";

    public SafeguardingClassifier(ILogger<SafeguardingClassifier> logger)
    {
        _logger = logger;
    }

    public SafeguardingResult Scan(string studentInput)
    {
        if (string.IsNullOrWhiteSpace(studentInput))
            return new SafeguardingResult(false, SafeguardingSeverity.None, Array.Empty<string>(), null);

        var lowerInput = studentInput.ToLowerInvariant();
        var categories = new List<string>();
        var severity = SafeguardingSeverity.None;

        // ── HIGH severity scan ──
        foreach (var phrase in HighSeverityEnglish)
        {
            if (lowerInput.Contains(phrase))
            {
                severity = SafeguardingSeverity.High;
                var category = CategorizePhraseHigh(phrase);
                if (!categories.Contains(category))
                    categories.Add(category);
            }
        }

        foreach (var phrase in HighSeverityHebrew)
        {
            if (studentInput.Contains(phrase, StringComparison.Ordinal))
            {
                severity = SafeguardingSeverity.High;
                var category = CategorizePhraseHigh(phrase);
                if (!categories.Contains(category))
                    categories.Add(category);
            }
        }

        foreach (var phrase in HighSeverityArabic)
        {
            if (studentInput.Contains(phrase, StringComparison.Ordinal))
            {
                severity = SafeguardingSeverity.High;
                var category = CategorizePhraseHigh(phrase);
                if (!categories.Contains(category))
                    categories.Add(category);
            }
        }

        if (SelfHarmRegex.IsMatch(studentInput))
        {
            severity = SafeguardingSeverity.High;
            if (!categories.Contains("self_harm"))
                categories.Add("self_harm");
        }

        // ── MEDIUM severity scan (only if not already HIGH) ──
        if (severity < SafeguardingSeverity.High)
        {
            foreach (var phrase in MediumSeverityEnglish)
            {
                if (lowerInput.Contains(phrase))
                {
                    severity = SafeguardingSeverity.Medium;
                    if (!categories.Contains("emotional_distress"))
                        categories.Add("emotional_distress");
                }
            }

            foreach (var phrase in MediumSeverityHebrew)
            {
                if (studentInput.Contains(phrase, StringComparison.Ordinal))
                {
                    severity = SafeguardingSeverity.Medium;
                    if (!categories.Contains("emotional_distress"))
                        categories.Add("emotional_distress");
                }
            }

            foreach (var phrase in MediumSeverityArabic)
            {
                if (studentInput.Contains(phrase, StringComparison.Ordinal))
                {
                    severity = SafeguardingSeverity.Medium;
                    if (!categories.Contains("emotional_distress"))
                        categories.Add("emotional_distress");
                }
            }
        }

        if (severity == SafeguardingSeverity.None)
            return new SafeguardingResult(false, SafeguardingSeverity.None, Array.Empty<string>(), null);

        _logger.LogWarning(
            "[SAFEGUARDING] concern_level={Level} categories=[{Categories}]",
            severity, string.Join(", ", categories));

        string? suggestedResponse = severity == SafeguardingSeverity.High
            ? DefaultHelplineResponse
            : null;

        return new SafeguardingResult(
            IsConcern: true,
            Severity: severity,
            TriggeredCategories: categories,
            SuggestedResponse: suggestedResponse);
    }

    public string GetSafeguardingResponse(string? market)
    {
        if (market is not null && Helplines.TryGetValue(market, out var helpline))
        {
            return $"It sounds like you might be going through something difficult. " +
                   $"Please talk to a trusted adult -- a parent, teacher, school counsellor, or another adult you trust. " +
                   $"If you need immediate help, you can contact {helpline.Name} at {helpline.Number} ({helpline.Url}).";
        }

        return DefaultHelplineResponse;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string CategorizePhraseHigh(string phrase)
    {
        var lower = phrase.ToLowerInvariant();

        if (lower.Contains("kill") || lower.Contains("die") || lower.Contains("suicide")
            || lower.Contains("live") || lower.Contains("alive") || lower.Contains("end my life")
            || phrase.Contains("התאבדות") || phrase.Contains("למות") || phrase.Contains("לחיות")
            || phrase.Contains("انتحار") || phrase.Contains("أموت") || phrase.Contains("العيش"))
            return "suicidal_ideation";

        if (lower.Contains("hurt myself") || lower.Contains("cutting") || lower.Contains("self harm")
            || lower.Contains("self-harm")
            || phrase.Contains("לפגוע בעצמי") || phrase.Contains("חותך") || phrase.Contains("חותכת")
            || phrase.Contains("إيذاء نفسي"))
            return "self_harm";

        if (lower.Contains("abus") || lower.Contains("hit") || lower.Contains("molest")
            || lower.Contains("rape") || lower.Contains("hurting me") || lower.Contains("touches me")
            || lower.Contains("touching me")
            || phrase.Contains("פוגע בי") || phrase.Contains("נוגע בי") || phrase.Contains("מכה")
            || phrase.Contains("פגיעה מינית") || phrase.Contains("הטרדה")
            || phrase.Contains("يؤذيني") || phrase.Contains("يلمسني") || phrase.Contains("يضربني")
            || phrase.Contains("اعتداء") || phrase.Contains("تحرش"))
            return "abuse";

        if (lower.Contains("nudes") || lower.Contains("photo") || lower.Contains("meet up")
            || lower.Contains("stranger") || lower.Contains("adult asked"))
            return "predatory_contact";

        return "safeguarding_concern";
    }
}
