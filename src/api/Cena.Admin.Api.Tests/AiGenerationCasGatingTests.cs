// =============================================================================
// Cena Platform — AI Generation CAS-Drop Behavioural Test (RDY-046, RDY-034 §5)
//
// Pins the contract that AiGenerationService's batch response surfaces
// DroppedForCasFailure + CasDropReasons per ADR-0032 §12.
//
// This is a record-shape pin — the full end-to-end behaviour across a real
// gate + real router is covered by the nightly workflow (RDY-051).
// =============================================================================

using Xunit;

namespace Cena.Admin.Api.Tests;

public class AiGenerationCasGatingTests
{
    [Fact]
    public void BatchGenerateResponse_ReportsDroppedCount_AndPerQuestionReasons()
    {
        var reason = new Cena.Admin.Api.CasDropReason(
            QuestionStem: "Solve 2x + 3 = 7",
            Engine: "SymPy",
            Reason: "cas_equivalence_failed",
            LatencyMs: 120.5);

        var response = new Cena.Admin.Api.BatchGenerateResponse(
            Success: true,
            Results: Array.Empty<Cena.Admin.Api.BatchGenerateResult>(),
            TotalGenerated: 5,
            PassedQualityGate: 3,
            NeedsReview: 1,
            AutoRejected: 0,
            ModelUsed: "claude-sonnet",
            Error: null,
            DroppedForCasFailure: 1,
            CasDropReasons: new[] { reason });

        Assert.Equal(1, response.DroppedForCasFailure);
        Assert.NotNull(response.CasDropReasons);
        Assert.Single(response.CasDropReasons!);
        Assert.Equal("cas_equivalence_failed", response.CasDropReasons![0].Reason);
        Assert.Equal("SymPy", response.CasDropReasons![0].Engine);
    }

    [Fact]
    public void BatchGenerateResponse_NoDrops_DefaultsToZeroAndNullReasons()
    {
        var response = new Cena.Admin.Api.BatchGenerateResponse(
            Success: true,
            Results: Array.Empty<Cena.Admin.Api.BatchGenerateResult>(),
            TotalGenerated: 0,
            PassedQualityGate: 0,
            NeedsReview: 0,
            AutoRejected: 0,
            ModelUsed: "claude-sonnet",
            Error: null);

        Assert.Equal(0, response.DroppedForCasFailure);
        Assert.Null(response.CasDropReasons);
    }

    [Fact]
    public void CasDropReason_PreservesFullDiagnosticPayload()
    {
        // The admin UI renders these per-question so each field must survive
        // record-equality comparisons (regression guard for refactors that
        // collapse the record into a narrower shape).
        var r1 = new Cena.Admin.Api.CasDropReason("stem", "SymPy", "reason", 10);
        var r2 = new Cena.Admin.Api.CasDropReason("stem", "SymPy", "reason", 10);
        Assert.Equal(r1, r2);
    }
}
