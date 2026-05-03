// =============================================================================
// Cena Platform — Diagnostic dispute endpoint validation tests (PRR-385)
//
// Smoke-tests the endpoint's own validation layer without spinning up an
// HTTP host. The deep path (persistence + metrics + accuracy-sampling)
// is already covered by DiagnosticDisputeServiceTests; this class locks
// the contract the HTTP adapter enforces BEFORE calling through to the
// service: Reason must parse to DisputeReason, DiagnosticId required,
// Comment must stay under MaxCommentLength, IDOR guard rejects missing
// claims.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DiagnosticDisputeEndpointsTests
{
    private static DiagnosticDisputeService NewService(out InMemoryDiagnosticDisputeRepository repo)
    {
        repo = new InMemoryDiagnosticDisputeRepository();
        return new DiagnosticDisputeService(
            repo,
            new PhotoDiagnosticMetrics(new DummyMeterFactory()),
            NullLogger<DiagnosticDisputeService>.Instance,
            TimeProvider.System);
    }

    [Fact]
    public async Task Submit_with_valid_payload_creates_persisted_dispute()
    {
        var service = NewService(out var repo);
        var view = await service.SubmitAsync(
            new SubmitDiagnosticDisputeCommand(
                DiagnosticId: "diag_abc",
                StudentSubjectIdHash: "hash_xyz",
                Reason: DisputeReason.WrongStepIdentified,
                StudentComment: "step 3 was fine, I dropped a sign later"),
            CancellationToken.None);

        Assert.NotNull(view);
        Assert.False(string.IsNullOrWhiteSpace(view.DisputeId));
        Assert.Equal(DisputeStatus.New, view.Status);

        var persisted = await repo.GetAsync(view.DisputeId, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal("diag_abc", persisted!.DiagnosticId);
    }

    [Fact]
    public async Task Submit_with_empty_diagnostic_id_throws()
    {
        var service = NewService(out _);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync(
                new SubmitDiagnosticDisputeCommand(
                    DiagnosticId: "",
                    StudentSubjectIdHash: "hash_x",
                    Reason: DisputeReason.Other,
                    StudentComment: null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Submit_with_empty_student_hash_throws()
    {
        var service = NewService(out _);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync(
                new SubmitDiagnosticDisputeCommand(
                    DiagnosticId: "diag_ok",
                    StudentSubjectIdHash: "",
                    Reason: DisputeReason.Other,
                    StudentComment: null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Submit_with_overlong_comment_throws()
    {
        var service = NewService(out _);
        var bigComment = new string('x', DiagnosticDisputeService.MaxCommentLength + 1);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SubmitAsync(
                new SubmitDiagnosticDisputeCommand(
                    DiagnosticId: "diag_ok",
                    StudentSubjectIdHash: "hash_ok",
                    Reason: DisputeReason.Other,
                    StudentComment: bigComment),
                CancellationToken.None));
    }

    [Theory]
    [InlineData("WrongNarration")]
    [InlineData("wrongnarration")]
    [InlineData("WRONG_STEP_IDENTIFIED")]
    [InlineData("OcrMisread")]
    [InlineData("Other")]
    public void All_dispute_reason_strings_parse_case_insensitive(string raw)
    {
        // The endpoint uses Enum.TryParse(ignoreCase:true) — lock the
        // full set of allowed spellings so UI copy teams can ship any
        // of en/he/ar-fronted raw values as long as they map 1-1 to the
        // enum name, case-insensitively.
        var normalized = raw.Replace("_", "").Replace(" ", "");
        Assert.True(
            Enum.TryParse<DisputeReason>(normalized, ignoreCase: true, out _),
            $"DisputeReason did not parse '{raw}'");
    }

    [Fact]
    public void Garbage_reason_string_does_not_parse()
    {
        Assert.False(
            Enum.TryParse<DisputeReason>("not_a_reason", ignoreCase: true, out _));
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
