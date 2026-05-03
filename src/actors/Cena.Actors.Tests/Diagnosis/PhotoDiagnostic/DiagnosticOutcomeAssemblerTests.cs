// =============================================================================
// Cena Platform — DiagnosticOutcomeAssembler tests (EPIC-PRR-J PRR-380/382)
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Diagnosis.PhotoDiagnostic;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Actors.Tests.Diagnosis.PhotoDiagnostic;

public class DiagnosticOutcomeAssemblerTests
{
    private static DiagnosticOutcomeAssembler NewAssembler(
        FakeChainVerifier chain,
        FakeScorer scorer,
        TimeProvider? clock = null)
    {
        var metrics = new PhotoDiagnosticMetrics(new DummyMeterFactory());
        var tracker = new PhotoDiagnosticConfidenceTracker();
        var sampler = new AccuracyAuditSampler(metrics);
        var auditLog = new LoggingPhotoDiagnosticAuditLog(
            NullLogger<LoggingPhotoDiagnosticAuditLog>.Instance);
        return new DiagnosticOutcomeAssembler(chain, scorer, metrics, tracker, sampler, auditLog,
            clock ?? TimeProvider.System);
    }

    private static ExtractedStep Step(int i, string latex, double conf = 0.95) =>
        new(i, latex, latex, conf);

    [Fact]
    public async Task FewerThanTwoStepsReturnsNotEnoughSteps()
    {
        var asm = NewAssembler(FakeChainVerifier.ThatAlwaysSucceeds(), FakeScorer.Empty());
        var outcome = await asm.AssembleAsync(new DiagnosticAssemblyInput(
            DiagnosticId: "d-001",
            StudentSubjectIdHash: "hash",
            Steps: new[] { Step(0, "x+1") },
            BreakSignature: null), default);

        Assert.Equal(DiagnosticVerdict.NotEnoughSteps, outcome.Verdict);
        Assert.Null(outcome.FirstWrongStepNumber);
        Assert.Equal(DiagnosticNarration.CheckWithTeacherFallback, outcome.Narration);
    }

    [Fact]
    public async Task ValidChainReturnsChainValidAndCongratsNarration()
    {
        var asm = NewAssembler(FakeChainVerifier.ThatAlwaysSucceeds(), FakeScorer.Empty());
        var outcome = await asm.AssembleAsync(new DiagnosticAssemblyInput(
            DiagnosticId: "d-002",
            StudentSubjectIdHash: "hash",
            Steps: new[] { Step(0, "x"), Step(1, "x") },
            BreakSignature: null), default);

        Assert.Equal(DiagnosticVerdict.ChainValid, outcome.Verdict);
        Assert.Equal(DiagnosticNarration.ChainValidCongrats, outcome.Narration);
        Assert.Null(outcome.MatchedTemplateId);
    }

    [Fact]
    public async Task LowConfidenceTransitionReturnsLowConfidenceFallback()
    {
        var asm = NewAssembler(FakeChainVerifier.ThatShortCircuitsLowConfidence(), FakeScorer.Empty());
        var outcome = await asm.AssembleAsync(new DiagnosticAssemblyInput(
            DiagnosticId: "d-003",
            StudentSubjectIdHash: "hash",
            Steps: new[] { Step(0, "x", 0.9), Step(1, "y", 0.3) },
            BreakSignature: null), default);

        Assert.Equal(DiagnosticVerdict.LowConfidenceFallback, outcome.Verdict);
        Assert.Equal(DiagnosticNarration.CheckWithTeacherFallback, outcome.Narration);
    }

    [Fact]
    public async Task WrongStepWithMatchingTemplateReturnsFirstWrongStepAndLocalizedNarration()
    {
        var template = BagrutMath4MisconceptionTaxonomy.SignFlipDistributive;
        var asm = NewAssembler(
            FakeChainVerifier.ThatShortCircuitsWrongAt(1),
            FakeScorer.Returning(new TemplateMatch(template, 1.0)));
        var outcome = await asm.AssembleAsync(new DiagnosticAssemblyInput(
            DiagnosticId: "d-004",
            StudentSubjectIdHash: "hash",
            Steps: new[] { Step(0, "-(a+b)"), Step(1, "-a+b") },
            BreakSignature: new CasBreakSignature(
                MisconceptionBreakType.SignFlipDistributive, "-(a+b)", "-a-b", "")), default);

        Assert.Equal(DiagnosticVerdict.FirstWrongStep, outcome.Verdict);
        Assert.Equal(2, outcome.FirstWrongStepNumber); // 1-indexed: the 2nd step is wrong
        Assert.Equal(template.TemplateId, outcome.MatchedTemplateId);
        Assert.Equal(template.ExplanationEn, outcome.Narration.En);
        Assert.Equal(template.ExplanationHe, outcome.Narration.He);
        Assert.Equal(template.ExplanationAr, outcome.Narration.Ar);
    }

    [Fact]
    public async Task WrongStepButNoMatchingTemplateFallsBack()
    {
        var asm = NewAssembler(
            FakeChainVerifier.ThatShortCircuitsWrongAt(1),
            FakeScorer.Empty());
        var outcome = await asm.AssembleAsync(new DiagnosticAssemblyInput(
            DiagnosticId: "d-005",
            StudentSubjectIdHash: "hash",
            Steps: new[] { Step(0, "x"), Step(1, "y") },
            BreakSignature: new CasBreakSignature(
                MisconceptionBreakType.Other, "x", "y", "")), default);

        Assert.Equal(DiagnosticVerdict.NoTemplateMatch, outcome.Verdict);
        Assert.Null(outcome.MatchedTemplateId);
        Assert.Equal(DiagnosticNarration.CheckWithTeacherFallback, outcome.Narration);
    }

    [Fact]
    public async Task RejectsEmptyDiagnosticId()
    {
        var asm = NewAssembler(FakeChainVerifier.ThatAlwaysSucceeds(), FakeScorer.Empty());
        await Assert.ThrowsAsync<ArgumentException>(() => asm.AssembleAsync(new DiagnosticAssemblyInput(
            DiagnosticId: "",
            StudentSubjectIdHash: "hash",
            Steps: new[] { Step(0, "x") },
            BreakSignature: null), default));
    }

    [Fact]
    public async Task RejectsEmptyStudentSubjectIdHash()
    {
        var asm = NewAssembler(FakeChainVerifier.ThatAlwaysSucceeds(), FakeScorer.Empty());
        await Assert.ThrowsAsync<ArgumentException>(() => asm.AssembleAsync(new DiagnosticAssemblyInput(
            DiagnosticId: "d-x",
            StudentSubjectIdHash: "",
            Steps: new[] { Step(0, "x") },
            BreakSignature: null), default));
    }

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }

    private sealed class FakeChainVerifier : IStepChainVerifier
    {
        private readonly Func<IReadOnlyList<ExtractedStep>, StepChainVerificationResult> _impl;
        public FakeChainVerifier(Func<IReadOnlyList<ExtractedStep>, StepChainVerificationResult> impl) => _impl = impl;

        public Task<StepChainVerificationResult> VerifyChainAsync(
            IReadOnlyList<ExtractedStep> steps, CancellationToken ct) => Task.FromResult(_impl(steps));

        public static FakeChainVerifier ThatAlwaysSucceeds() => new(steps =>
        {
            if (steps.Count < 2) return new StepChainVerificationResult(Array.Empty<StepTransitionResult>(), null);
            var transitions = new List<StepTransitionResult>(steps.Count - 1);
            for (int i = 0; i < steps.Count - 1; i++)
            {
                transitions.Add(new StepTransitionResult(i, i + 1, StepTransitionOutcome.Valid, null, "OK"));
            }
            return new StepChainVerificationResult(transitions, null);
        });

        public static FakeChainVerifier ThatShortCircuitsLowConfidence() => new(steps =>
        {
            var t = new StepTransitionResult(0, 1, StepTransitionOutcome.LowConfidence, null, "low");
            return new StepChainVerificationResult(new[] { t }, 1);
        });

        public static FakeChainVerifier ThatShortCircuitsWrongAt(int idx) => new(steps =>
        {
            var t = new StepTransitionResult(idx - 1, idx, StepTransitionOutcome.Wrong, null, "wrong");
            return new StepChainVerificationResult(new[] { t }, idx);
        });
    }

    private sealed class FakeScorer : ITemplateMatchingScorer
    {
        private readonly TemplateMatch? _result;
        public FakeScorer(TemplateMatch? result) => _result = result;

        public TemplateMatch? PickBestMatch(Cena.Actors.Diagnosis.PhotoDiagnostic.CasBreakSignature signature) => _result;

        public static FakeScorer Empty() => new(null);
        public static FakeScorer Returning(TemplateMatch m) => new(m);
    }
}
