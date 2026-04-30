// =============================================================================
// PRR-322 — Mock-exam run cost telemetry unit tests
//
// Pins the count × rate formula so a config drift or copy-paste bug in
// ComputeRunCost doesn't quietly mis-attribute USD to a run. Pure-
// computation tests; no Marten / no postgres / no LLM. Integration
// against live cena-postgres lives in MockExamRunCostIntegrationTests.
// =============================================================================

using Cena.Actors.Assessment;
using Cena.Actors.Cas;
using Cena.Actors.Mastery;
using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cena.Actors.Tests.Assessment;

public sealed class MockExamRunCostTests
{
    private static MockExamRunService BuildService(MockExamCostRateConfig? rates = null)
    {
        var store = Substitute.For<IDocumentStore>();
        var cas = Substitute.For<ICasRouterService>();
        var catalog = Substitute.For<IBagrutPaperStructureCatalog>();
        var gate = Substitute.For<IItemDeliveryGate>();
        var bkt = Substitute.For<IBktStateTracker>();

        return new MockExamRunService(
            store,
            cas,
            catalog,
            gate,
            NullLogger<MockExamRunService>.Instance,
            clock: TimeProvider.System,
            bktTracker: bkt,
            costRates: Options.Create(rates ?? new MockExamCostRateConfig()));
    }

    private static ExamSimulationState State(string runId = "run-x", string examCode = "806", string studentId = "stu-1") =>
        new()
        {
            SimulationId = runId,
            StudentId    = studentId,
            ExamCode     = examCode,
        };

    [Fact]
    public void ComputeRunCost_All_Counts_Zero_Yields_Zero_Total_And_Frozen_Timestamp()
    {
        var svc = BuildService();
        var now = DateTimeOffset.Parse("2026-04-30T12:00:00Z");

        var doc = svc.ComputeRunCost(State(), casAttempts: 0, llmTokensIn: 0, llmTokensOut: 0, ocrCalls: 0, now);

        Assert.Equal(0m,        doc.CasCostUsd);
        Assert.Equal(0m,        doc.LlmCostUsd);
        Assert.Equal(0m,        doc.OcrCostUsd);
        Assert.Equal(0m,        doc.TotalUsd);
        Assert.Equal("run-x",   doc.Id);
        Assert.Equal("stu-1",   doc.StudentId);
        Assert.Equal("806",     doc.ExamCode);
        Assert.Null(            doc.StudentTenant);   // ADR-0001 not yet wired
        Assert.Equal(now,       doc.ComputedAt);
    }

    [Fact]
    public void ComputeRunCost_CasOnly_Defaults_27_Calls_Yields_Expected_Subcent_Total()
    {
        // Default rate: 0.0001 USD per call. The directive's worked-example
        // says "~9 Q's × ~3 attempts × ~1 SymPy invocation = ~27 SymPy calls
        // / run", which projects to 0.0027 USD at the default rate.
        var svc = BuildService();
        var now = DateTimeOffset.UtcNow;

        var doc = svc.ComputeRunCost(State(), casAttempts: 27, llmTokensIn: 0, llmTokensOut: 0, ocrCalls: 0, now);

        Assert.Equal(27,         doc.CasCallsCount);
        Assert.Equal(0.0027m,    doc.CasCostUsd);     // 27 * 0.0001
        Assert.Equal(0m,         doc.LlmCostUsd);
        Assert.Equal(0m,         doc.OcrCostUsd);
        Assert.Equal(0.0027m,    doc.TotalUsd);
    }

    [Fact]
    public void ComputeRunCost_LlmOnly_Token_Math_Uses_Per_1k_Rates_Per_Side()
    {
        // Default rates: input 0.0008/1k, output 0.0040/1k. 4000 in + 8000
        // out = 4 * 0.0008 + 8 * 0.0040 = 0.0032 + 0.0320 = 0.0352.
        var svc = BuildService();
        var now = DateTimeOffset.UtcNow;

        var doc = svc.ComputeRunCost(State(), casAttempts: 0, llmTokensIn: 4_000, llmTokensOut: 8_000, ocrCalls: 0, now);

        Assert.Equal(4_000,      doc.LlmTokensInput);
        Assert.Equal(8_000,      doc.LlmTokensOutput);
        Assert.Equal(0.0352m,    doc.LlmCostUsd);
        Assert.Equal(0.0352m,    doc.TotalUsd);
    }

    [Fact]
    public void ComputeRunCost_OcrOnly_Defaults_3_Calls_Yields_15_Tenths_Of_A_Cent()
    {
        var svc = BuildService();
        var now = DateTimeOffset.UtcNow;

        var doc = svc.ComputeRunCost(State(), casAttempts: 0, llmTokensIn: 0, llmTokensOut: 0, ocrCalls: 3, now);

        Assert.Equal(3,          doc.OcrCallsCount);
        Assert.Equal(0.0150m,    doc.OcrCostUsd);     // 3 * 0.005
        Assert.Equal(0.0150m,    doc.TotalUsd);
    }

    [Fact]
    public void ComputeRunCost_All_Streams_Sum_To_Total_Within_6_Decimal_Places()
    {
        var svc = BuildService();
        var now = DateTimeOffset.UtcNow;

        var doc = svc.ComputeRunCost(State(), casAttempts: 27, llmTokensIn: 4_000, llmTokensOut: 8_000, ocrCalls: 3, now);

        var expected = 0.0027m + 0.0352m + 0.0150m;  // 0.0529
        Assert.Equal(expected, doc.TotalUsd);
        // Per-stream USDs must equal what the totals decompose to (no
        // hidden rounding asymmetry across decimal Math.Round MidpointRounding).
        Assert.Equal(doc.CasCostUsd + doc.LlmCostUsd + doc.OcrCostUsd, doc.TotalUsd);
    }

    [Fact]
    public void ComputeRunCost_Custom_Rates_Override_Defaults()
    {
        // 10x markup on CAS to exercise the IOptions config path.
        var rates = new MockExamCostRateConfig { CasUsdPerCall = 0.001m };
        var svc = BuildService(rates);

        var doc = svc.ComputeRunCost(State(), casAttempts: 10, llmTokensIn: 0, llmTokensOut: 0, ocrCalls: 0, DateTimeOffset.UtcNow);

        Assert.Equal(0.0100m, doc.CasCostUsd);   // 10 * 0.001
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(500)]   // pathological retry-storm (PRR-297 caps at 4× per Q)
    public void ComputeRunCost_CasAttempts_Linear_In_Count(int attempts)
    {
        var svc = BuildService();

        var doc = svc.ComputeRunCost(State(), casAttempts: attempts, 0, 0, 0, DateTimeOffset.UtcNow);

        // Exact equality OK because 0.0001 is representable in decimal.
        Assert.Equal(attempts * 0.0001m, doc.CasCostUsd);
    }

    [Fact]
    public void ComputeRunCost_StudentTenant_Stays_Null_Until_AdrTenancy_Phase1_Lands()
    {
        // Pin the placeholder behaviour: as long as ExamSimulationState
        // doesn't carry a tenant id, the cost doc tenant is null and the
        // OTel counter falls back to "unknown". This test fails the day
        // someone wires tenant id without updating ComputeRunCost — which
        // is exactly when we want a re-think.
        var svc = BuildService();

        var doc = svc.ComputeRunCost(State(), casAttempts: 1, 0, 0, 0, DateTimeOffset.UtcNow);

        Assert.Null(doc.StudentTenant);
    }
}
