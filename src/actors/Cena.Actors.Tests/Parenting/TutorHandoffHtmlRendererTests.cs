// =============================================================================
// Cena Platform — TutorHandoffHtmlRenderer tests (EPIC-PRR-I PRR-325)
//
// Locks the four boundary rules from the renderer's file banner:
//   1. Self-contained (non-empty HTML, Cena branding, inline style).
//   2. Locale-correct <html dir="…">.
//   3. <bdi dir="ltr"> around numerals / dates even in RTL pages.
//   4. WebUtility.HtmlEncode on every user string (XSS escape smoke test).
// Plus null-guard and banned-term (streak / countdown / scarcity) scan.
// =============================================================================

using Cena.Api.Contracts.Parenting;
using Xunit;

namespace Cena.Actors.Tests.Parenting;

public class TutorHandoffHtmlRendererTests
{
    private static readonly ITutorHandoffHtmlRenderer Renderer = new TutorHandoffHtmlRenderer();

    // ── Rule 1: self-contained, Cena-branded, non-empty ─────────────────────

    [Fact]
    public void RenderHtml_produces_nonempty_document_with_cena_branding_and_student_name()
    {
        var dto = BuildDto(studentDisplayName: "Yael Cohen", locale: "en");

        var html = Renderer.RenderHtml(dto);

        Assert.False(string.IsNullOrWhiteSpace(html));
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Cena", html); // brand mark
        Assert.Contains("<style>", html); // inline style block — self-contained
        Assert.Contains("Yael Cohen", html);
    }

    [Fact]
    public void RenderHtml_contains_no_external_stylesheet_or_script_tags()
    {
        var dto = BuildDto();

        var html = Renderer.RenderHtml(dto);

        // Self-containment guard — HTML must not pull anything from the network
        // or require a script runtime to render.
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
        Assert.DoesNotContain("<script", html);
    }

    // ── Rule 2: RTL for he/ar, LTR for en ───────────────────────────────────

    [Theory]
    [InlineData("he")]
    [InlineData("ar")]
    [InlineData("HE")]
    [InlineData("Ar")]
    public void RenderHtml_uses_rtl_direction_for_hebrew_and_arabic(string locale)
    {
        var dto = BuildDto(locale: locale);

        var html = Renderer.RenderHtml(dto);

        Assert.Contains("dir=\"rtl\"", html);
        Assert.DoesNotContain("<html lang=\"" + locale + "\" dir=\"ltr\"", html);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("EN")]
    [InlineData("")]
    [InlineData("fr")]
    public void RenderHtml_uses_ltr_direction_for_english_and_unknown_locales(string locale)
    {
        var dto = BuildDto(locale: locale);

        var html = Renderer.RenderHtml(dto);

        Assert.Contains("dir=\"ltr\"", html);
    }

    // ── Rule 3: numerals / dates wrapped in <bdi dir="ltr"> even on RTL pages

    [Fact]
    public void RenderHtml_wraps_numerals_in_bdi_ltr_even_inside_rtl_document()
    {
        var dto = BuildDto(
            locale: "he",
            timeOnTaskMinutes: 217L,
            windowStart: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            windowEnd: new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero));

        var html = Renderer.RenderHtml(dto);

        Assert.Contains("dir=\"rtl\"", html);
        // The minutes scalar must be wrapped for LTR iso-text behaviour
        // (memory discipline: "Math always LTR"). We check the marker is
        // present AND the numeral appears inside a <bdi dir="ltr"> block.
        Assert.Contains("<bdi dir=\"ltr\">217</bdi>", html);
        // Date scalars likewise stay LTR.
        Assert.Contains("<bdi dir=\"ltr\">2026-04-01</bdi>", html);
        Assert.Contains("<bdi dir=\"ltr\">2026-04-22</bdi>", html);
    }

    [Fact]
    public void RenderHtml_wraps_mastery_probabilities_in_bdi_ltr()
    {
        var dto = BuildDto(
            locale: "he",
            masteryDeltas: new Dictionary<string, MasteryDelta>
            {
                ["ALG.LIN"] = new("ALG.LIN", 0.30, 0.72, "משוואות לינאריות"),
            });

        var html = Renderer.RenderHtml(dto);

        // 30%, 72%, +42% scalars should all be inside <bdi dir="ltr"> so
        // RTL page direction never reverses them.
        Assert.Contains("<bdi dir=\"ltr\">30%</bdi>", html);
        Assert.Contains("<bdi dir=\"ltr\">72%</bdi>", html);
        Assert.Contains("<bdi dir=\"ltr\">+42%</bdi>", html);
    }

    // ── Rule 4: HTML-encode every user string ───────────────────────────────

    [Fact]
    public void RenderHtml_escapes_injectable_topic_names_instead_of_rendering_markup()
    {
        var dto = BuildDto(topicsPracticed: new[]
        {
            "<script>alert('pwn')</script>",
            "Linear systems & functions",
        });

        var html = Renderer.RenderHtml(dto);

        // The literal <script>...</script> string must appear escaped — NOT
        // as executable markup. If HtmlEncode is skipped, this assertion
        // fails because the raw "<script>" would be present unescaped.
        Assert.DoesNotContain("<script>alert('pwn')</script>", html);
        Assert.Contains("&lt;script&gt;alert", html);
        Assert.Contains("Linear systems &amp; functions", html);
    }

    [Fact]
    public void RenderHtml_escapes_injectable_misconception_summary_text()
    {
        var dto = BuildDto(
            includeMisconceptions: true,
            misconceptionSummary: "Drops negative signs <img src=x onerror=alert(1)>");

        var html = Renderer.RenderHtml(dto);

        Assert.DoesNotContain("<img src=x onerror=alert(1)>", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
    }

    // ── Opt-in flag semantics surface in rendered HTML ──────────────────────

    [Fact]
    public void RenderHtml_when_misconceptions_excluded_renders_not_included_notice_not_summary()
    {
        var dto = BuildDto(
            includeMisconceptions: false,
            misconceptionSummary: "secret clinical detail that must not leak");

        var html = Renderer.RenderHtml(dto);

        Assert.DoesNotContain("secret clinical detail", html);
    }

    [Fact]
    public void RenderHtml_when_time_excluded_renders_not_included_notice_not_minutes()
    {
        // Build the DTO via the assembler to honour the opt-in rule — the
        // renderer trusts the DTO's field-presence contract rather than
        // re-interpreting the original flags.
        var req = new TutorHandoffReportRequestDto(
            StudentSubjectIdEncrypted: "enc::student",
            WindowStart: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            WindowEnd: new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero),
            IncludeMisconceptions: true,
            IncludeTimeOnTask: false,
            IncludeMastery: true,
            Locale: "en");
        var cards = new TutorHandoffCards(
            StudentDisplayName: null,
            TopicsPracticed: Array.Empty<string>(),
            MasteryDeltas: new Dictionary<string, MasteryDelta>(),
            TimeOnTaskMinutes: 5000,
            MisconceptionSummary: null,
            RecommendedFocusAreas: Array.Empty<string>());
        var dto = TutorHandoffReportAssembler.Assemble(req, cards, NowUtc);

        var html = Renderer.RenderHtml(dto);

        // 5000 minutes from the cards must NOT bleed into the rendered
        // HTML when the parent chose to exclude time-on-task.
        Assert.DoesNotContain("5000", html);
    }

    // ── Null-guard ──────────────────────────────────────────────────────────

    [Fact]
    public void RenderHtml_null_report_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => Renderer.RenderHtml(null!));
    }

    // ── Ship-gate memory discipline (banned terms) ──────────────────────────

    [Theory]
    [InlineData("streak")]
    [InlineData("countdown")]
    [InlineData("scarcity")]
    [InlineData("don't lose")]
    [InlineData("don't break")]
    public void RenderHtml_locale_labels_contain_no_banned_gamification_terms(string banned)
    {
        foreach (var locale in new[] { "en", "he", "ar" })
        {
            var html = Renderer.RenderHtml(BuildDto(locale: locale));
            Assert.DoesNotContain(
                banned,
                html,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset NowUtc =
        new(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);

    private static TutorHandoffReportDto BuildDto(
        string? studentDisplayName = "Sample Student",
        string locale = "en",
        IReadOnlyList<string>? topicsPracticed = null,
        IReadOnlyDictionary<string, MasteryDelta>? masteryDeltas = null,
        long? timeOnTaskMinutes = null,
        string? misconceptionSummary = null,
        IReadOnlyList<string>? recommendedFocusAreas = null,
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null,
        bool includeMisconceptions = true)
    {
        var end = windowEnd ?? new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);
        var start = windowStart ?? end.AddDays(-30);
        return new TutorHandoffReportDto(
            StudentSubjectIdEncrypted: "enc::student::test",
            GeneratedAtUtc: NowUtc,
            WindowStart: start,
            WindowEnd: end,
            Locale: locale,
            StudentDisplayName: studentDisplayName,
            TopicsPracticed: topicsPracticed ?? Array.Empty<string>(),
            MasteryDeltas: masteryDeltas ?? new Dictionary<string, MasteryDelta>(),
            TimeOnTaskMinutes: timeOnTaskMinutes,
            MisconceptionSummary: includeMisconceptions ? misconceptionSummary : null,
            RecommendedFocusAreas: recommendedFocusAreas ?? Array.Empty<string>());
    }
}
