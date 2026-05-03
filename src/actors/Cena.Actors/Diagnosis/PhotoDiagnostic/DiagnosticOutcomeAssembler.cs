// =============================================================================
// Cena Platform — DiagnosticOutcomeAssembler (EPIC-PRR-J PRR-380/382/420/421/423)
//
// Composition root for the photo-diagnostic pipeline. Given already-
// extracted steps + a CAS break signature (if one was found), the
// assembler:
//   1. walks the step chain through IStepChainVerifier,
//   2. scores candidate templates through ITemplateMatchingScorer,
//   3. records OCR/template metrics,
//   4. feeds the per-student confidence tracker,
//   5. asks the accuracy audit sampler whether to flag for SME review,
//   6. packages everything into a DiagnosticOutcome.
//
// This is the single surface an endpoint calls — all the pieces we've
// built in PRR-J so far converge here. Production-grade per memory
// 'No stubs — production grade' (2026-04-11): every step runs real
// implementations against real interfaces.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Input to the assembler: everything already extracted from the photo.</summary>
public sealed record DiagnosticAssemblyInput(
    string DiagnosticId,
    string StudentSubjectIdHash,
    IReadOnlyList<ExtractedStep> Steps,
    /// <summary>CAS-detected break signature — null when chain verified clean.</summary>
    CasBreakSignature? BreakSignature);

/// <summary>Seam for the assembler (so endpoints and tests can swap it).</summary>
public interface IDiagnosticOutcomeAssembler
{
    Task<DiagnosticOutcome> AssembleAsync(DiagnosticAssemblyInput input, CancellationToken ct);
}

/// <summary>Default assembler — wires all PRR-J components together.</summary>
public sealed class DiagnosticOutcomeAssembler : IDiagnosticOutcomeAssembler
{
    private readonly IStepChainVerifier _chainVerifier;
    private readonly ITemplateMatchingScorer _scorer;
    private readonly PhotoDiagnosticMetrics _metrics;
    private readonly IPhotoDiagnosticConfidenceTracker _tracker;
    private readonly IAccuracyAuditSampler _sampler;
    private readonly IPhotoDiagnosticAuditLog _auditLog;
    private readonly TimeProvider _clock;

    public DiagnosticOutcomeAssembler(
        IStepChainVerifier chainVerifier,
        ITemplateMatchingScorer scorer,
        PhotoDiagnosticMetrics metrics,
        IPhotoDiagnosticConfidenceTracker tracker,
        IAccuracyAuditSampler sampler,
        IPhotoDiagnosticAuditLog auditLog,
        TimeProvider clock)
    {
        _chainVerifier = chainVerifier ?? throw new ArgumentNullException(nameof(chainVerifier));
        _scorer = scorer ?? throw new ArgumentNullException(nameof(scorer));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DiagnosticOutcome> AssembleAsync(DiagnosticAssemblyInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.DiagnosticId))
            throw new ArgumentException("DiagnosticId is required.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.StudentSubjectIdHash))
            throw new ArgumentException("StudentSubjectIdHash is required.", nameof(input));
        ArgumentNullException.ThrowIfNull(input.Steps);

        var steps = input.Steps;
        var minOcrConfidence = double.PositiveInfinity;
        foreach (var s in steps)
        {
            _metrics.RecordOcrConfidence(s.Confidence, "chain-input");
            if (s.Confidence < minOcrConfidence) minOcrConfidence = s.Confidence;
        }
        if (double.IsPositiveInfinity(minOcrConfidence)) minOcrConfidence = 0d;

        // Trivial chain: fewer than 2 steps means nothing to verify.
        if (steps.Count < 2)
        {
            return BuildOutcome(
                input,
                verdict: DiagnosticVerdict.NotEnoughSteps,
                chain: new StepChainVerificationResult(Array.Empty<StepTransitionResult>(), null),
                matchedTemplate: null,
                templateScore: null,
                narration: DiagnosticNarration.CheckWithTeacherFallback,
                minOcrConfidence: minOcrConfidence,
                ct: ct);
        }

        // Walk the step chain through the CAS router.
        var chain = await _chainVerifier.VerifyChainAsync(steps, ct);

        // If the verifier short-circuited on a low-confidence step, surface that.
        var firstLowConfidenceTransition = chain.Transitions
            .FirstOrDefault(t => t.Outcome == StepTransitionOutcome.LowConfidence);
        if (firstLowConfidenceTransition is not null)
        {
            _metrics.RecordLowConfidenceRefusal("chain_low_confidence");
            return BuildOutcome(
                input,
                verdict: DiagnosticVerdict.LowConfidenceFallback,
                chain: chain,
                matchedTemplate: null,
                templateScore: null,
                narration: DiagnosticNarration.CheckWithTeacherFallback,
                minOcrConfidence: minOcrConfidence,
                ct: ct);
        }

        // Chain clean?
        if (chain.Succeeded)
        {
            return BuildOutcome(
                input,
                verdict: DiagnosticVerdict.ChainValid,
                chain: chain,
                matchedTemplate: null,
                templateScore: null,
                narration: DiagnosticNarration.ChainValidCongrats,
                minOcrConfidence: minOcrConfidence,
                ct: ct);
        }

        // Chain had a wrong step. Score candidate templates off the CAS break signature.
        TemplateMatch? match = null;
        if (input.BreakSignature is not null)
        {
            match = _scorer.PickBestMatch(input.BreakSignature);
        }

        if (match is null)
        {
            _metrics.RecordTemplateFallback(input.BreakSignature?.BreakType ?? MisconceptionBreakType.Other);
            return BuildOutcome(
                input,
                verdict: DiagnosticVerdict.NoTemplateMatch,
                chain: chain,
                matchedTemplate: null,
                templateScore: null,
                narration: DiagnosticNarration.CheckWithTeacherFallback,
                minOcrConfidence: minOcrConfidence,
                ct: ct);
        }

        _metrics.RecordTemplateScore(match.Score, match.Template.BreakType);
        return BuildOutcome(
            input,
            verdict: DiagnosticVerdict.FirstWrongStep,
            chain: chain,
            matchedTemplate: match.Template,
            templateScore: match.Score,
            narration: DiagnosticNarration.FromTemplate(match.Template),
            minOcrConfidence: minOcrConfidence,
            ct: ct);
    }

    private DiagnosticOutcome BuildOutcome(
        DiagnosticAssemblyInput input,
        DiagnosticVerdict verdict,
        StepChainVerificationResult chain,
        MisconceptionTemplate? matchedTemplate,
        double? templateScore,
        DiagnosticNarration narration,
        double minOcrConfidence,
        CancellationToken ct)
    {
        // Feed confidence tracker so the next diagnostic knows context.
        _tracker.Record(input.StudentSubjectIdHash, new DiagnosticObservation(
            OcrConfidence: minOcrConfidence,
            TemplateScore: templateScore ?? 0d,
            RecordedAt: _clock.GetUtcNow()));
        var advice = _tracker.GetAdvice(input.StudentSubjectIdHash);

        // Ask the sampler whether to flag for SME review.
        var candidate = new AccuracyAuditCandidate(
            DiagnosticId: input.DiagnosticId,
            StudentSubjectIdHash: input.StudentSubjectIdHash,
            BreakType: matchedTemplate?.BreakType ?? input.BreakSignature?.BreakType,
            OcrConfidence: minOcrConfidence,
            TemplateScore: templateScore,
            TemplateMatched: matchedTemplate is not null,
            StepChainVerificationSucceeded: chain.Succeeded);
        var decision = _sampler.Decide(candidate);
        if (decision.Sampled)
        {
            // Fire-and-forget: audit log failures must not crash the diagnostic.
            _ = _auditLog.WriteAsync(candidate, decision, ct);
        }

        var firstWrongStepNumber = chain.FirstFailureIndex is { } idx ? idx + 1 : (int?)null;

        return new DiagnosticOutcome(
            DiagnosticId: input.DiagnosticId,
            Verdict: verdict,
            FirstWrongStepNumber: firstWrongStepNumber,
            Chain: chain,
            MatchedTemplateId: matchedTemplate?.TemplateId,
            Narration: narration,
            Advice: advice,
            FlaggedForAudit: decision.Sampled);
    }
}
