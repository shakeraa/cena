// =============================================================================
// Cena Platform — Diagnostic-audit endpoint tests (PRR-390)
//
// Tests the GET handler directly against a fake IDiagnosticDisputeService.
// The handler is where the shape contract lives: missing disputeId 400,
// unknown disputeId 404, known disputeId 200 + DTO with dispute fields
// populated + deferred fields null + DeferredFields list matching the
// V1DeferredFields sentinel.
//
// Locks the "honest not-available" pattern from memory "No stubs —
// production grade": PhotoHash / CapturedOutcomeJson / MatchedTemplateId /
// FirstWrongStepNumber MUST return null in v1 (not fabricated), and the
// DeferredFields array MUST surface them so the UI can render a
// "pending upstream capture" state rather than a faked value.
// =============================================================================

using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Cena.Admin.Api.Host.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DiagnosticAuditEndpointTests
{
    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Missing_disputeId_returns_400()
    {
        var disputes = new FakeDisputeService();
        var ctx = new DefaultHttpContext();

        var result = await DiagnosticAuditEndpoints.HandleGetAsync(
            disputeId: "",
            http: ctx,
            disputes: disputes,
            logger: NullLogger<DiagnosticAuditEndpoints.DiagnosticAuditMarker>.Instance,
            ct: default);

        var statusCode = ExtractStatusCode(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
    }

    [Fact]
    public async Task Unknown_disputeId_returns_404()
    {
        var disputes = new FakeDisputeService { Stored = null };
        var ctx = new DefaultHttpContext();

        var result = await DiagnosticAuditEndpoints.HandleGetAsync(
            disputeId: "nonexistent-dispute",
            http: ctx,
            disputes: disputes,
            logger: NullLogger<DiagnosticAuditEndpoints.DiagnosticAuditMarker>.Instance,
            ct: default);

        var statusCode = ExtractStatusCode(result);
        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
    }

    [Fact]
    public async Task Known_disputeId_returns_200_with_populated_metadata()
    {
        var disputes = new FakeDisputeService
        {
            Stored = new DiagnosticDisputeView(
                DisputeId: "dispute-abc",
                DiagnosticId: "diag-123",
                StudentSubjectIdHash: "hash-xyz",
                Reason: DisputeReason.WrongNarration,
                StudentComment: "the narration didn't match my work",
                Status: DisputeStatus.New,
                SubmittedAt: SubmittedAt,
                ReviewedAt: null,
                ReviewerNote: null),
        };
        var ctx = new DefaultHttpContext();

        var result = await DiagnosticAuditEndpoints.HandleGetAsync(
            disputeId: "dispute-abc",
            http: ctx,
            disputes: disputes,
            logger: NullLogger<DiagnosticAuditEndpoints.DiagnosticAuditMarker>.Instance,
            ct: default);

        var ok = Assert.IsType<Ok<DiagnosticAuditResponseDto>>(result);
        Assert.NotNull(ok.Value);
        var dto = ok.Value!;
        Assert.Equal("dispute-abc", dto.DisputeId);
        Assert.Equal("diag-123", dto.DiagnosticId);
        Assert.Equal("hash-xyz", dto.StudentSubjectIdHash);
        Assert.Equal("WrongNarration", dto.Reason);
        Assert.Equal("the narration didn't match my work", dto.StudentComment);
        Assert.Equal("New", dto.Status);
        Assert.Equal(SubmittedAt, dto.SubmittedAtUtc);
        Assert.Null(dto.ReviewedAtUtc);
        Assert.Null(dto.ReviewerNote);
    }

    [Fact]
    public async Task Deferred_fields_are_null_in_v1_response()
    {
        // Memory "No stubs — production grade": photo hash + captured
        // outcome + template id + first-wrong-step MUST return null until
        // an upstream capture writer is wired. Never fabricate.
        var disputes = new FakeDisputeService
        {
            Stored = NewStoredDispute(),
        };
        var ctx = new DefaultHttpContext();

        var result = await DiagnosticAuditEndpoints.HandleGetAsync(
            disputeId: "dispute-abc",
            http: ctx,
            disputes: disputes,
            logger: NullLogger<DiagnosticAuditEndpoints.DiagnosticAuditMarker>.Instance,
            ct: default);

        var ok = Assert.IsType<Ok<DiagnosticAuditResponseDto>>(result);
        var dto = ok.Value!;
        Assert.Null(dto.PhotoHash);
        Assert.Null(dto.CapturedOutcomeJson);
        Assert.Null(dto.MatchedTemplateId);
        Assert.Null(dto.FirstWrongStepNumber);
    }

    [Fact]
    public async Task Deferred_fields_list_surfaces_v1_gaps_for_UI()
    {
        // The UI needs to render a "pending upstream capture" badge next
        // to the deferred fields. Response carries the canonical list so
        // the client doesn't hardcode its own knowledge of which fields
        // are v1-deferred.
        var disputes = new FakeDisputeService { Stored = NewStoredDispute() };
        var ctx = new DefaultHttpContext();

        var result = await DiagnosticAuditEndpoints.HandleGetAsync(
            disputeId: "dispute-abc",
            http: ctx,
            disputes: disputes,
            logger: NullLogger<DiagnosticAuditEndpoints.DiagnosticAuditMarker>.Instance,
            ct: default);

        var ok = Assert.IsType<Ok<DiagnosticAuditResponseDto>>(result);
        var dto = ok.Value!;
        Assert.Equal(4, dto.DeferredFields.Count);
        Assert.Contains("photoHash", dto.DeferredFields);
        Assert.Contains("capturedOutcomeJson", dto.DeferredFields);
        Assert.Contains("matchedTemplateId", dto.DeferredFields);
        Assert.Contains("firstWrongStepNumber", dto.DeferredFields);
    }

    [Fact]
    public async Task Reviewed_dispute_surfaces_reviewer_note_and_status()
    {
        var reviewedAt = SubmittedAt.AddHours(2);
        var disputes = new FakeDisputeService
        {
            Stored = new DiagnosticDisputeView(
                DisputeId: "dispute-abc",
                DiagnosticId: "diag-123",
                StudentSubjectIdHash: "hash-xyz",
                Reason: DisputeReason.OcrMisread,
                StudentComment: null,
                Status: DisputeStatus.Upheld,
                SubmittedAt: SubmittedAt,
                ReviewedAt: reviewedAt,
                ReviewerNote: "Student was right; OCR missed the exponent."),
        };
        var ctx = new DefaultHttpContext();

        var result = await DiagnosticAuditEndpoints.HandleGetAsync(
            disputeId: "dispute-abc",
            http: ctx,
            disputes: disputes,
            logger: NullLogger<DiagnosticAuditEndpoints.DiagnosticAuditMarker>.Instance,
            ct: default);

        var ok = Assert.IsType<Ok<DiagnosticAuditResponseDto>>(result);
        var dto = ok.Value!;
        Assert.Equal("Upheld", dto.Status);
        Assert.Equal(reviewedAt, dto.ReviewedAtUtc);
        Assert.Equal("Student was right; OCR missed the exponent.", dto.ReviewerNote);
    }

    // ---- Helpers ---------------------------------------------------------

    private static DiagnosticDisputeView NewStoredDispute() =>
        new(
            DisputeId: "dispute-abc",
            DiagnosticId: "diag-123",
            StudentSubjectIdHash: "hash-xyz",
            Reason: DisputeReason.WrongNarration,
            StudentComment: "comment",
            Status: DisputeStatus.New,
            SubmittedAt: SubmittedAt,
            ReviewedAt: null,
            ReviewerNote: null);

    private static int? ExtractStatusCode(IResult result) => result switch
    {
        BadRequest<CenaErrorLike> br => br.StatusCode,
        JsonHttpResult<CenaErrorLike> jr => jr.StatusCode,
        _ => InferStatusCodeReflection(result),
    };

    /// <summary>
    /// Minimal helper to extract a StatusCode from an opaque IResult.
    /// Results.BadRequest / Results.Json don't return typed results for
    /// our CenaError, so reflect on the StatusCode property when present.
    /// </summary>
    private static int? InferStatusCodeReflection(IResult result)
    {
        var prop = result.GetType().GetProperty("StatusCode");
        if (prop is null) return null;
        var value = prop.GetValue(result);
        return value as int?;
    }

    // Local marker type only used so the pattern-match arms above compile
    // cleanly; never instantiated.
    private sealed record CenaErrorLike(string Code, string Message);

    private sealed class FakeDisputeService : IDiagnosticDisputeService
    {
        public DiagnosticDisputeView? Stored { get; set; }

        public Task<DiagnosticDisputeView?> GetAsync(string disputeId, CancellationToken ct)
        {
            if (Stored is not null && Stored.DisputeId == disputeId)
                return Task.FromResult<DiagnosticDisputeView?>(Stored);
            return Task.FromResult<DiagnosticDisputeView?>(null);
        }

        public Task<DiagnosticDisputeView> SubmitAsync(
            SubmitDiagnosticDisputeCommand command, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DiagnosticDisputeView>> ListByStudentAsync(
            string studentSubjectIdHash, int take, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DiagnosticDisputeView>> ListRecentAsync(
            DisputeStatus? status, int take, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<DiagnosticDisputeView> ReviewAsync(
            string disputeId, DisputeStatus newStatus, string? reviewerNote, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
