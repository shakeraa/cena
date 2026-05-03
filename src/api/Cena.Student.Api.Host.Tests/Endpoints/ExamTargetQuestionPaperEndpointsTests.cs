// =============================================================================
// Cena Platform — ExamTargetQuestionPaperEndpoints validation tests (prr-243)
//
// Shape-validation tests for the two PATCH endpoints.
//   - PATCH /api/me/exam-targets/{id}/question-papers
//   - PATCH /api/me/exam-targets/{id}/per-paper-sitting
// HTTP-level integration exercises the same path via the existing
// StudentApi fixture when revived.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Student.Api.Host.Endpoints;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Endpoints;

public sealed class ExamTargetQuestionPaperEndpointsTests
{
    // ── question-papers PATCH ────────────────────────────────────────────

    [Fact]
    public void QuestionPapers_rejects_null_request()
    {
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchQuestionPapers(null, out var err);
        Assert.False(ok);
        Assert.Contains("required", err);
    }

    [Fact]
    public void QuestionPapers_rejects_both_add_and_remove()
    {
        var req = new PatchQuestionPapersRequestDto(Add: "035581", Remove: "035582", SittingOverride: null);
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchQuestionPapers(req, out var err);
        Assert.False(ok);
        Assert.Contains("Exactly one", err);
    }

    [Fact]
    public void QuestionPapers_rejects_neither_add_nor_remove()
    {
        var req = new PatchQuestionPapersRequestDto(Add: null, Remove: null, SittingOverride: null);
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchQuestionPapers(req, out var err);
        Assert.False(ok);
        Assert.Contains("Exactly one", err);
    }

    [Fact]
    public void QuestionPapers_rejects_sittingOverride_with_remove()
    {
        var req = new PatchQuestionPapersRequestDto(
            Add: null,
            Remove: "035581",
            SittingOverride: new SittingCodeDto("תשפ״ו", SittingSeason.Winter, SittingMoed.A));
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchQuestionPapers(req, out var err);
        Assert.False(ok);
        Assert.Contains("only applies when adding", err);
    }

    [Fact]
    public void QuestionPapers_accepts_add_without_sittingOverride()
    {
        var req = new PatchQuestionPapersRequestDto(
            Add: "035581", Remove: null, SittingOverride: null);
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchQuestionPapers(req, out _);
        Assert.True(ok);
    }

    [Fact]
    public void QuestionPapers_accepts_add_with_valid_sittingOverride()
    {
        var req = new PatchQuestionPapersRequestDto(
            Add: "035581",
            Remove: null,
            SittingOverride: new SittingCodeDto("תשפ״ו", SittingSeason.Winter, SittingMoed.A));
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchQuestionPapers(req, out _);
        Assert.True(ok);
    }

    [Fact]
    public void QuestionPapers_rejects_empty_academic_year_in_sittingOverride()
    {
        var req = new PatchQuestionPapersRequestDto(
            Add: "035581",
            Remove: null,
            SittingOverride: new SittingCodeDto("", SittingSeason.Winter, SittingMoed.A));
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchQuestionPapers(req, out var err);
        Assert.False(ok);
        Assert.Contains("academicYear", err);
    }

    // ── per-paper-sitting PATCH ──────────────────────────────────────────

    [Fact]
    public void PerPaperSitting_rejects_missing_paperCode()
    {
        var req = new PatchPerPaperSittingRequestDto(PaperCode: "", Sitting: null);
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchPerPaperSitting(req, out var err);
        Assert.False(ok);
        Assert.Contains("paperCode", err);
    }

    [Fact]
    public void PerPaperSitting_accepts_null_sitting_for_clear()
    {
        var req = new PatchPerPaperSittingRequestDto(PaperCode: "035581", Sitting: null);
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchPerPaperSitting(req, out _);
        Assert.True(ok);
    }

    [Fact]
    public void PerPaperSitting_accepts_valid_sitting_for_set()
    {
        var req = new PatchPerPaperSittingRequestDto(
            PaperCode: "035581",
            Sitting: new SittingCodeDto("תשפ״ו", SittingSeason.Winter, SittingMoed.B));
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchPerPaperSitting(req, out _);
        Assert.True(ok);
    }

    [Fact]
    public void PerPaperSitting_rejects_invalid_season()
    {
        var req = new PatchPerPaperSittingRequestDto(
            PaperCode: "035581",
            Sitting: new SittingCodeDto("תשפ״ו", (SittingSeason)99, SittingMoed.A));
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchPerPaperSitting(req, out var err);
        Assert.False(ok);
        Assert.Contains("season", err);
    }

    [Fact]
    public void PerPaperSitting_rejects_missing_academic_year_when_sitting_set()
    {
        var req = new PatchPerPaperSittingRequestDto(
            PaperCode: "035581",
            Sitting: new SittingCodeDto("", SittingSeason.Winter, SittingMoed.A));
        var ok = ExamTargetQuestionPaperEndpoints.TryValidatePatchPerPaperSitting(req, out var err);
        Assert.False(ok);
        Assert.Contains("academicYear", err);
    }
}
