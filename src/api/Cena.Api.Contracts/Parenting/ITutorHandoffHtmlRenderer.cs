// =============================================================================
// Cena Platform — ITutorHandoffHtmlRenderer + TutorHandoffHtmlRenderer
//                 (EPIC-PRR-I PRR-325)
//
// Why this exists:
//   The tutor-handoff deliverable is a shareable artefact the parent hands
//   to an external tutor. This task ships the HTML form of that artefact:
//   a self-contained HTML5 document with inline CSS and zero external
//   assets — safe to email, print to PDF from any browser, or display
//   inside the Cena SPA's own print-preview. An eventual server-side PDF
//   library (QuestPDF / iText / PdfSharp) is an explicit FOLLOW-UP and
//   does NOT block this task (see TutorHandoffReportDto file banner for
//   the scope-reduction rationale).
//
//   Every HTML document this renderer produces satisfies four boundary
//   rules:
//     1. Standalone. No external CSS, no external fonts, no external JS.
//        A tutor pulling the file into a local email or print preview
//        must see the same layout as a browser online.
//     2. Locale-correct direction. he / ar → <html dir="rtl">; en
//        → <html dir="ltr">; any other locale falls back to LTR.
//     3. Math / numerals stay LTR even inside an RTL page. The renderer
//        wraps every numeric cell + LaTeX-style snippet in
//        <bdi dir="ltr">…</bdi>. (User memory discipline: "Math always
//        LTR" — the user has caught a reversed-equation bug before; this
//        renderer does not repeat that mistake.)
//     4. Every user-supplied string is HTML-encoded via
//        System.Net.WebUtility.HtmlEncode before being written into the
//        document. A maliciously-crafted topic name ("<script>…</script>")
//        appears as literal text in the tutor's inbox, not as executable
//        markup. Locked by unit test.
//
//   The renderer is a pure string transformer — DTO in, HTML out — with
//   no I/O, no clock, no DI. It ships as the default
//   ITutorHandoffHtmlRenderer implementation; a host that needs different
//   branding (school-SKU mode, say) can Replace() the DI binding with a
//   skinned subclass.
//
// Why here (Cena.Api.Contracts.Parenting) and not in Cena.Actors:
//   The renderer's input type — TutorHandoffReportDto — lives in
//   Cena.Api.Contracts. Cena.Actors does not depend on Contracts (that
//   would be cyclic), so the renderer cannot live in Actors. Contracts
//   is the only compile-legal home that keeps the renderer close to
//   its input shape and its sibling pure-function assembler.
//
// Ship-gate discipline:
//   Banned-term scan — the document template below does not render any
//   streak / countdown / scarcity / loss-aversion language. The report
//   is informational only; the parent and tutor read dispassionate
//   facts (window, topics, mastery deltas, recommended focus).
// =============================================================================

using System.Globalization;
using System.Net;
using System.Text;

namespace Cena.Api.Contracts.Parenting;

/// <summary>
/// Pure string transformer: <see cref="TutorHandoffReportDto"/> → self-
/// contained HTML5 document. The endpoint composes the DTO via
/// <see cref="TutorHandoffReportAssembler"/> and writes the renderer's
/// output directly to the HTTP response body with content-type text/html.
/// </summary>
public interface ITutorHandoffHtmlRenderer
{
    /// <summary>
    /// Render the given report as a self-contained HTML5 document. Throws
    /// <see cref="ArgumentNullException"/> when <paramref name="report"/>
    /// is null; never throws for legal (even sparse) reports.
    /// </summary>
    string RenderHtml(TutorHandoffReportDto report);
}

/// <summary>
/// Default <see cref="ITutorHandoffHtmlRenderer"/>. Inline CSS, three
/// locale buckets (he / ar → RTL; en → LTR; fallback → LTR), numerals
/// and LaTeX-style snippets wrapped in <c>&lt;bdi dir="ltr"&gt;</c>,
/// every user string passed through <see cref="WebUtility.HtmlEncode"/>.
/// </summary>
public sealed class TutorHandoffHtmlRenderer : ITutorHandoffHtmlRenderer
{
    // Invariant-culture formatter for numeric cells — we do NOT want the
    // thread's current culture to flip a European decimal point into a
    // comma halfway through an HTML report.
    private static readonly CultureInfo InvariantNumbers = CultureInfo.InvariantCulture;

    /// <inheritdoc />
    public string RenderHtml(TutorHandoffReportDto report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var isRtl = IsRtlLocale(report.Locale);
        var direction = isRtl ? "rtl" : "ltr";
        var lang = SafeLangTag(report.Locale);
        var labels = LocaleLabels.Get(report.Locale);

        var sb = new StringBuilder(capacity: 4096);
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"").Append(WebUtility.HtmlEncode(lang))
          .Append("\" dir=\"").Append(direction).Append("\">\n");
        sb.Append("<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(labels.DocumentTitle)).Append("</title>\n");
        AppendStyles(sb, isRtl);
        sb.Append("</head>\n");
        sb.Append("<body>\n");
        sb.Append("<main class=\"cena-handoff\">\n");

        AppendHeader(sb, report, labels);
        AppendSummary(sb, report, labels);
        AppendTopicsSection(sb, report, labels);
        AppendMasterySection(sb, report, labels);
        AppendTimeOnTaskSection(sb, report, labels);
        AppendMisconceptionSection(sb, report, labels);
        AppendFocusSection(sb, report, labels);
        AppendFooter(sb, report, labels);

        sb.Append("</main>\n");
        sb.Append("</body>\n");
        sb.Append("</html>\n");
        return sb.ToString();
    }

    // ── Section renderers ───────────────────────────────────────────────────

    private static void AppendHeader(StringBuilder sb, TutorHandoffReportDto r, LocaleLabelSet l)
    {
        sb.Append("<header class=\"cena-header\">\n");
        sb.Append("<div class=\"cena-brand\">Cena</div>\n");
        sb.Append("<h1>").Append(WebUtility.HtmlEncode(l.DocumentTitle)).Append("</h1>\n");
        if (!string.IsNullOrWhiteSpace(r.StudentDisplayName))
        {
            sb.Append("<p class=\"cena-subject\">")
              .Append(WebUtility.HtmlEncode(l.SubjectLabel)).Append(": <strong>")
              .Append(WebUtility.HtmlEncode(r.StudentDisplayName))
              .Append("</strong></p>\n");
        }
        sb.Append("</header>\n");
    }

    private static void AppendSummary(StringBuilder sb, TutorHandoffReportDto r, LocaleLabelSet l)
    {
        sb.Append("<section class=\"cena-window\">\n");
        sb.Append("<dl>\n");
        sb.Append("<dt>").Append(WebUtility.HtmlEncode(l.WindowStartLabel)).Append("</dt>");
        sb.Append("<dd>").Append(WrapLtr(FormatDate(r.WindowStart))).Append("</dd>\n");
        sb.Append("<dt>").Append(WebUtility.HtmlEncode(l.WindowEndLabel)).Append("</dt>");
        sb.Append("<dd>").Append(WrapLtr(FormatDate(r.WindowEnd))).Append("</dd>\n");
        sb.Append("<dt>").Append(WebUtility.HtmlEncode(l.GeneratedAtLabel)).Append("</dt>");
        sb.Append("<dd>").Append(WrapLtr(FormatDateTime(r.GeneratedAtUtc))).Append("</dd>\n");
        sb.Append("</dl>\n");
        sb.Append("</section>\n");
    }

    private static void AppendTopicsSection(StringBuilder sb, TutorHandoffReportDto r, LocaleLabelSet l)
    {
        sb.Append("<section class=\"cena-section cena-topics\">\n");
        sb.Append("<h2>").Append(WebUtility.HtmlEncode(l.TopicsHeading)).Append("</h2>\n");
        if (r.TopicsPracticed.Count == 0)
        {
            sb.Append("<p class=\"cena-empty\">")
              .Append(WebUtility.HtmlEncode(l.NoData)).Append("</p>\n");
        }
        else
        {
            sb.Append("<ul>\n");
            foreach (var topic in r.TopicsPracticed)
            {
                sb.Append("<li>").Append(WebUtility.HtmlEncode(topic ?? string.Empty)).Append("</li>\n");
            }
            sb.Append("</ul>\n");
        }
        sb.Append("</section>\n");
    }

    private static void AppendMasterySection(StringBuilder sb, TutorHandoffReportDto r, LocaleLabelSet l)
    {
        sb.Append("<section class=\"cena-section cena-mastery\">\n");
        sb.Append("<h2>").Append(WebUtility.HtmlEncode(l.MasteryHeading)).Append("</h2>\n");
        if (r.MasteryDeltas.Count == 0)
        {
            sb.Append("<p class=\"cena-empty\">")
              .Append(WebUtility.HtmlEncode(l.NoData)).Append("</p>\n");
        }
        else
        {
            sb.Append("<table>\n<thead><tr>");
            sb.Append("<th>").Append(WebUtility.HtmlEncode(l.MasteryColSkill)).Append("</th>");
            sb.Append("<th>").Append(WebUtility.HtmlEncode(l.MasteryColBefore)).Append("</th>");
            sb.Append("<th>").Append(WebUtility.HtmlEncode(l.MasteryColAfter)).Append("</th>");
            sb.Append("<th>").Append(WebUtility.HtmlEncode(l.MasteryColChange)).Append("</th>");
            sb.Append("</tr></thead>\n<tbody>\n");

            foreach (var entry in r.MasteryDeltas)
            {
                var delta = entry.Value;
                var change = delta.PosteriorProbability - delta.PriorProbability;
                sb.Append("<tr>");
                sb.Append("<td>").Append(WebUtility.HtmlEncode(delta.DisplayLabel ?? entry.Key)).Append("</td>");
                sb.Append("<td>").Append(WrapLtr(FormatProbability(delta.PriorProbability))).Append("</td>");
                sb.Append("<td>").Append(WrapLtr(FormatProbability(delta.PosteriorProbability))).Append("</td>");
                sb.Append("<td>").Append(WrapLtr(FormatDelta(change))).Append("</td>");
                sb.Append("</tr>\n");
            }
            sb.Append("</tbody>\n</table>\n");
        }
        sb.Append("</section>\n");
    }

    private static void AppendTimeOnTaskSection(StringBuilder sb, TutorHandoffReportDto r, LocaleLabelSet l)
    {
        sb.Append("<section class=\"cena-section cena-time\">\n");
        sb.Append("<h2>").Append(WebUtility.HtmlEncode(l.TimeHeading)).Append("</h2>\n");
        if (!r.TimeOnTaskMinutes.HasValue)
        {
            sb.Append("<p class=\"cena-empty\">")
              .Append(WebUtility.HtmlEncode(l.NotIncluded)).Append("</p>\n");
        }
        else
        {
            sb.Append("<p>").Append(WrapLtr(r.TimeOnTaskMinutes.Value.ToString(InvariantNumbers)))
              .Append(' ').Append(WebUtility.HtmlEncode(l.MinutesUnit)).Append("</p>\n");
        }
        sb.Append("</section>\n");
    }

    private static void AppendMisconceptionSection(StringBuilder sb, TutorHandoffReportDto r, LocaleLabelSet l)
    {
        sb.Append("<section class=\"cena-section cena-misconceptions\">\n");
        sb.Append("<h2>").Append(WebUtility.HtmlEncode(l.MisconceptionHeading)).Append("</h2>\n");
        if (string.IsNullOrWhiteSpace(r.MisconceptionSummary))
        {
            sb.Append("<p class=\"cena-empty\">")
              .Append(WebUtility.HtmlEncode(l.NotIncluded)).Append("</p>\n");
        }
        else
        {
            sb.Append("<p>").Append(WebUtility.HtmlEncode(r.MisconceptionSummary)).Append("</p>\n");
        }
        sb.Append("</section>\n");
    }

    private static void AppendFocusSection(StringBuilder sb, TutorHandoffReportDto r, LocaleLabelSet l)
    {
        sb.Append("<section class=\"cena-section cena-focus\">\n");
        sb.Append("<h2>").Append(WebUtility.HtmlEncode(l.FocusHeading)).Append("</h2>\n");
        if (r.RecommendedFocusAreas.Count == 0)
        {
            sb.Append("<p class=\"cena-empty\">")
              .Append(WebUtility.HtmlEncode(l.NoData)).Append("</p>\n");
        }
        else
        {
            sb.Append("<ul>\n");
            foreach (var focus in r.RecommendedFocusAreas)
            {
                sb.Append("<li>").Append(WebUtility.HtmlEncode(focus ?? string.Empty)).Append("</li>\n");
            }
            sb.Append("</ul>\n");
        }
        sb.Append("</section>\n");
    }

    private static void AppendFooter(StringBuilder sb, TutorHandoffReportDto r, LocaleLabelSet l)
    {
        sb.Append("<footer class=\"cena-footer\">\n");
        sb.Append("<p>").Append(WebUtility.HtmlEncode(l.FooterNotice)).Append("</p>\n");
        sb.Append("</footer>\n");
    }

    // ── Style block ─────────────────────────────────────────────────────────

    private static void AppendStyles(StringBuilder sb, bool isRtl)
    {
        // Inline CSS — no external stylesheet so the file is self-contained.
        // Neutral typography so any system font renders cleanly; Cena accent
        // (#7367F0) used sparingly for brand recognition.
        sb.Append("<style>\n");
        sb.Append("html,body{margin:0;padding:0;font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif;color:#1f1f2e;background:#fff;}\n");
        sb.Append(".cena-handoff{max-width:760px;margin:2rem auto;padding:2rem;border:1px solid #e5e5ef;border-radius:8px;line-height:1.5;}\n");
        sb.Append(".cena-brand{font-weight:700;color:#7367F0;letter-spacing:.02em;text-transform:uppercase;font-size:.9rem;}\n");
        sb.Append(".cena-header h1{margin:.2rem 0 .4rem;font-size:1.6rem;}\n");
        sb.Append(".cena-header .cena-subject{margin:0 0 1rem;color:#52525b;}\n");
        sb.Append(".cena-window dl{display:grid;grid-template-columns:auto 1fr;column-gap:1rem;row-gap:.3rem;margin:0 0 1.5rem;}\n");
        sb.Append(".cena-window dt{color:#6b7280;}\n");
        sb.Append(".cena-window dd{margin:0;}\n");
        sb.Append(".cena-section{margin:1.25rem 0;padding-top:1rem;border-top:1px solid #f0f0f5;}\n");
        sb.Append(".cena-section h2{font-size:1.1rem;margin:0 0 .6rem;color:#2e2e4a;}\n");
        sb.Append(".cena-empty{color:#6b7280;font-style:italic;margin:.2rem 0;}\n");
        sb.Append(".cena-section ul{margin:.2rem 0 .4rem 1.25rem;padding:0;}\n");
        sb.Append(".cena-section table{width:100%;border-collapse:collapse;margin:.4rem 0;}\n");
        sb.Append(".cena-section th,.cena-section td{border:1px solid #e5e5ef;padding:.4rem .6rem;text-align:start;}\n");
        sb.Append(".cena-section th{background:#f8f8fb;font-weight:600;}\n");
        sb.Append(".cena-footer{margin-top:1.5rem;padding-top:.8rem;border-top:1px solid #f0f0f5;font-size:.85rem;color:#6b7280;}\n");
        sb.Append("bdi{unicode-bidi:isolate;}\n");
        if (isRtl)
        {
            sb.Append(".cena-section ul{margin-left:0;margin-right:1.25rem;}\n");
        }
        sb.Append("</style>\n");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static bool IsRtlLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return false;
        return locale.Equals("he", StringComparison.OrdinalIgnoreCase)
            || locale.Equals("ar", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeLangTag(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return "en";
        // Strip anything beyond a simple tag — defence against tag injection.
        var trimmed = locale.Trim();
        foreach (var c in trimmed)
        {
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) return "en";
        }
        return trimmed;
    }

    private static string WrapLtr(string inner) =>
        "<bdi dir=\"ltr\">" + WebUtility.HtmlEncode(inner) + "</bdi>";

    private static string FormatDate(DateTimeOffset dto) =>
        dto.UtcDateTime.ToString("yyyy-MM-dd", InvariantNumbers);

    private static string FormatDateTime(DateTimeOffset dto) =>
        dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", InvariantNumbers);

    private static string FormatProbability(double p) =>
        (Math.Round(p * 100.0, 0)).ToString("0", InvariantNumbers) + "%";

    private static string FormatDelta(double d)
    {
        var pct = Math.Round(d * 100.0, 0);
        var sign = pct > 0 ? "+" : (pct < 0 ? "−" : "±");
        return sign + Math.Abs(pct).ToString("0", InvariantNumbers) + "%";
    }
}

// =============================================================================
// Locale label bundles
//
// Why inline:
//   Three hard-coded locales (he / ar / en). Externalising these into a
//   resource file would be the correct move at 10+ locales, but at three
//   the indirection cost dwarfs the benefit. A reviewer can read the
//   exact strings shipped in each locale without hopping files.
// =============================================================================

internal sealed record LocaleLabelSet(
    string DocumentTitle,
    string SubjectLabel,
    string WindowStartLabel,
    string WindowEndLabel,
    string GeneratedAtLabel,
    string TopicsHeading,
    string MasteryHeading,
    string MasteryColSkill,
    string MasteryColBefore,
    string MasteryColAfter,
    string MasteryColChange,
    string TimeHeading,
    string MinutesUnit,
    string MisconceptionHeading,
    string FocusHeading,
    string NoData,
    string NotIncluded,
    string FooterNotice);

internal static class LocaleLabels
{
    private static readonly LocaleLabelSet English = new(
        DocumentTitle: "Tutor Handoff Report",
        SubjectLabel: "Student",
        WindowStartLabel: "Period start",
        WindowEndLabel: "Period end",
        GeneratedAtLabel: "Report generated",
        TopicsHeading: "Topics practiced",
        MasteryHeading: "Mastery changes",
        MasteryColSkill: "Skill",
        MasteryColBefore: "Before",
        MasteryColAfter: "After",
        MasteryColChange: "Change",
        TimeHeading: "Time on task",
        MinutesUnit: "minutes",
        MisconceptionHeading: "Common patterns",
        FocusHeading: "Recommended focus",
        NoData: "No data available.",
        NotIncluded: "The parent chose not to include this section.",
        FooterNotice: "Prepared by the student's family via Cena for tutor handoff.");

    private static readonly LocaleLabelSet Hebrew = new(
        DocumentTitle: "דו\"ח העברה למורה פרטי",
        SubjectLabel: "תלמיד/ה",
        WindowStartLabel: "תחילת התקופה",
        WindowEndLabel: "סיום התקופה",
        GeneratedAtLabel: "תאריך הפקה",
        TopicsHeading: "נושאים שתורגלו",
        MasteryHeading: "שינויי שליטה",
        MasteryColSkill: "מיומנות",
        MasteryColBefore: "לפני",
        MasteryColAfter: "אחרי",
        MasteryColChange: "שינוי",
        TimeHeading: "זמן עבודה",
        MinutesUnit: "דקות",
        MisconceptionHeading: "דפוסים נפוצים",
        FocusHeading: "מיקוד מומלץ",
        NoData: "אין נתונים זמינים.",
        NotIncluded: "ההורה בחר שלא לכלול חלק זה.",
        FooterNotice: "הוכן על ידי משפחת התלמיד/ה דרך Cena לצורך מסירה למורה.");

    private static readonly LocaleLabelSet Arabic = new(
        DocumentTitle: "تقرير تسليم للمعلّم الخصوصي",
        SubjectLabel: "الطالب/ة",
        WindowStartLabel: "بداية الفترة",
        WindowEndLabel: "نهاية الفترة",
        GeneratedAtLabel: "تاريخ الإصدار",
        TopicsHeading: "الموضوعات التي تم التدرّب عليها",
        MasteryHeading: "تغيّرات الإتقان",
        MasteryColSkill: "المهارة",
        MasteryColBefore: "قبل",
        MasteryColAfter: "بعد",
        MasteryColChange: "التغيير",
        TimeHeading: "الوقت على المهمة",
        MinutesUnit: "دقائق",
        MisconceptionHeading: "أنماط شائعة",
        FocusHeading: "التركيز الموصى به",
        NoData: "لا توجد بيانات.",
        NotIncluded: "اختار الوالدان عدم تضمين هذا القسم.",
        FooterNotice: "أُعدّ من قبل عائلة الطالب/ة عبر Cena لغرض التسليم للمعلّم.");

    public static LocaleLabelSet Get(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return English;
        if (locale.Equals("he", StringComparison.OrdinalIgnoreCase)) return Hebrew;
        if (locale.Equals("ar", StringComparison.OrdinalIgnoreCase)) return Arabic;
        return English;
    }
}
