// =============================================================================
// Cena Platform — TutorHandoffReportAssembler tests (EPIC-PRR-I PRR-325)
//
// Locks the four assembler invariants (see TutorHandoffReportAssembler
// file banner):
//   1. Opt-in flags are authoritative (null out excluded sections).
//   2. TopicsPracticed order is preserved verbatim.
//   3. Output window matches normalised request window.
//   4. GeneratedAtUtc is independent of WindowEnd.
// Plus input validation (empty id / locale / bad window) and the
// null-guards on request + cards.
// =============================================================================

using Cena.Api.Contracts.Parenting;
using Xunit;

namespace Cena.Actors.Tests.Parenting;

public class TutorHandoffReportAssemblerTests
{
    // ── Opt-in enforcement ──────────────────────────────────────────────────

    [Fact]
    public void Assemble_respects_include_misconceptions_false_nulls_the_summary()
    {
        var req = BuildRequest(includeMisconceptions: false);
        var cards = BuildCards(misconceptionSummary: "Frequent sign errors on subtraction");

        var report = TutorHandoffReportAssembler.Assemble(req, cards, NowUtc);

        Assert.Null(report.MisconceptionSummary);
    }

    [Fact]
    public void Assemble_respects_include_time_on_task_false_nulls_the_minutes()
    {
        var req = BuildRequest(includeTimeOnTask: false);
        var cards = BuildCards(timeOnTaskMinutes: 217);

        var report = TutorHandoffReportAssembler.Assemble(req, cards, NowUtc);

        Assert.Null(report.TimeOnTaskMinutes);
    }

    [Fact]
    public void Assemble_respects_include_mastery_false_empties_the_deltas()
    {
        var req = BuildRequest(includeMastery: false);
        var cards = BuildCards(masteryDeltas: new Dictionary<string, MasteryDelta>
        {
            ["ALG.LIN"] = new("ALG.LIN", 0.3, 0.6, "Linear"),
        });

        var report = TutorHandoffReportAssembler.Assemble(req, cards, NowUtc);

        Assert.Empty(report.MasteryDeltas);
    }

    [Fact]
    public void Assemble_all_includes_true_passes_every_field_through()
    {
        var req = BuildRequest(
            includeMisconceptions: true,
            includeTimeOnTask: true,
            includeMastery: true);
        var cards = BuildCards(
            misconceptionSummary: "Tends to drop negative signs",
            timeOnTaskMinutes: 120,
            masteryDeltas: new Dictionary<string, MasteryDelta>
            {
                ["ALG.LIN"] = new("ALG.LIN", 0.3, 0.72, "Linear"),
            });

        var report = TutorHandoffReportAssembler.Assemble(req, cards, NowUtc);

        Assert.Equal("Tends to drop negative signs", report.MisconceptionSummary);
        Assert.Equal(120L, report.TimeOnTaskMinutes);
        Assert.Single(report.MasteryDeltas);
        Assert.Equal(0.72, report.MasteryDeltas["ALG.LIN"].PosteriorProbability);
    }

    // ── Topic order preservation ────────────────────────────────────────────

    [Fact]
    public void Assemble_preserves_topics_practiced_order_verbatim()
    {
        var req = BuildRequest();
        // Deliberately unsorted — assembler must NOT re-sort.
        var cards = BuildCards(topicsPracticed: new[]
        {
            "Quadratic equations",
            "Linear systems",
            "Factoring polynomials",
            "Exponents and logarithms",
        });

        var report = TutorHandoffReportAssembler.Assemble(req, cards, NowUtc);

        Assert.Equal(new[]
        {
            "Quadratic equations",
            "Linear systems",
            "Factoring polynomials",
            "Exponents and logarithms",
        }, report.TopicsPracticed);
    }

    // ── Window normalisation ────────────────────────────────────────────────

    [Fact]
    public void Assemble_null_window_start_defaults_to_window_end_minus_30_days()
    {
        var end = new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero);
        var req = BuildRequest(windowStart: null, windowEnd: end);

        var report = TutorHandoffReportAssembler.Assemble(req, BuildCards(), NowUtc);

        Assert.Equal(end - TutorHandoffReportAssembler.DefaultWindow, report.WindowStart);
        Assert.Equal(end, report.WindowEnd);
    }

    [Fact]
    public void Assemble_explicit_window_is_preserved_exactly()
    {
        var start = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
        var req = BuildRequest(windowStart: start, windowEnd: end);

        var report = TutorHandoffReportAssembler.Assemble(req, BuildCards(), NowUtc);

        Assert.Equal(start, report.WindowStart);
        Assert.Equal(end, report.WindowEnd);
    }

    [Fact]
    public void Assemble_rejects_window_start_at_or_after_window_end()
    {
        var t = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
        var req = BuildRequest(windowStart: t, windowEnd: t);

        var ex = Assert.Throws<ArgumentException>(() =>
            TutorHandoffReportAssembler.Assemble(req, BuildCards(), NowUtc));
        Assert.Contains("strictly", ex.Message);
    }

    // ── GeneratedAtUtc is independent of WindowEnd ──────────────────────────

    [Fact]
    public void Assemble_generated_at_is_the_now_argument_not_window_end()
    {
        var end = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 4, 22, 9, 30, 0, TimeSpan.Zero);
        var req = BuildRequest(windowEnd: end);

        var report = TutorHandoffReportAssembler.Assemble(req, BuildCards(), now);

        Assert.Equal(now, report.GeneratedAtUtc);
        Assert.NotEqual(end, report.GeneratedAtUtc);
    }

    // ── Input validation ────────────────────────────────────────────────────

    [Fact]
    public void Assemble_empty_student_id_throws_argument_exception()
    {
        var req = BuildRequest(studentId: "");

        var ex = Assert.Throws<ArgumentException>(() =>
            TutorHandoffReportAssembler.Assemble(req, BuildCards(), NowUtc));
        Assert.Contains("StudentSubjectIdEncrypted", ex.Message);
    }

    [Fact]
    public void Assemble_blank_locale_throws_argument_exception()
    {
        var req = BuildRequest(locale: "   ");

        var ex = Assert.Throws<ArgumentException>(() =>
            TutorHandoffReportAssembler.Assemble(req, BuildCards(), NowUtc));
        Assert.Contains("Locale", ex.Message);
    }

    // ── Null-guards ─────────────────────────────────────────────────────────

    [Fact]
    public void Assemble_null_request_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TutorHandoffReportAssembler.Assemble(null!, BuildCards(), NowUtc));
    }

    [Fact]
    public void Assemble_null_cards_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TutorHandoffReportAssembler.Assemble(BuildRequest(), null!, NowUtc));
    }

    // ── Student display name pass-through ───────────────────────────────────

    [Fact]
    public void Assemble_passes_student_display_name_through_from_cards()
    {
        var cards = BuildCards(studentDisplayName: "Yael Cohen");

        var report = TutorHandoffReportAssembler.Assemble(BuildRequest(), cards, NowUtc);

        Assert.Equal("Yael Cohen", report.StudentDisplayName);
    }

    [Fact]
    public void Assemble_null_student_display_name_is_preserved()
    {
        // Student opted out of name display per ADR-0041 visibility veto.
        var cards = BuildCards(studentDisplayName: null);

        var report = TutorHandoffReportAssembler.Assemble(BuildRequest(), cards, NowUtc);

        Assert.Null(report.StudentDisplayName);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset NowUtc =
        new(2026, 4, 22, 12, 0, 0, TimeSpan.Zero);

    private static TutorHandoffReportRequestDto BuildRequest(
        string studentId = "enc::student::default",
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null,
        bool includeMisconceptions = true,
        bool includeTimeOnTask = true,
        bool includeMastery = true,
        string locale = "en")
        => new(
            StudentSubjectIdEncrypted: studentId,
            WindowStart: windowStart,
            WindowEnd: windowEnd ?? new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero),
            IncludeMisconceptions: includeMisconceptions,
            IncludeTimeOnTask: includeTimeOnTask,
            IncludeMastery: includeMastery,
            Locale: locale);

    private static TutorHandoffCards BuildCards(
        string? studentDisplayName = "Sample Student",
        IReadOnlyList<string>? topicsPracticed = null,
        IReadOnlyDictionary<string, MasteryDelta>? masteryDeltas = null,
        long timeOnTaskMinutes = 60,
        string? misconceptionSummary = null,
        IReadOnlyList<string>? recommendedFocusAreas = null)
        => new(
            StudentDisplayName: studentDisplayName,
            TopicsPracticed: topicsPracticed ?? Array.Empty<string>(),
            MasteryDeltas: masteryDeltas ?? new Dictionary<string, MasteryDelta>(),
            TimeOnTaskMinutes: timeOnTaskMinutes,
            MisconceptionSummary: misconceptionSummary,
            RecommendedFocusAreas: recommendedFocusAreas ?? Array.Empty<string>());
}
