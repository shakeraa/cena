// =============================================================================
// Cena Platform — CAS Verification Gate (RDY-034 / RDY-036 / RDY-037, ADR-0002)
//
// Single entry point used by question ingestion paths (manual author, AI
// generation, OCR template, backfill) to run the authored answer through
// the CAS oracle.
//
// Responsibilities:
//   1. Detect whether the question needs CAS verification (MathContentDetector).
//   2. Short-circuit on idempotency: (questionId, answer-hash) already Verified.
//   3. Route to ICasRouterService for math/physics content.
//   4. Map CasVerifyResult → CasGateOutcome and build the QuestionCasBinding
//      doc the caller persists in the same Marten session.
//   5. Emit OpenTelemetry metrics under the `Cena.Cas.Gate` meter.
//
// Circuit-open policy: when the CAS oracle is unavailable we return
// CircuitOpen (binding.Status=Unverifiable, Engine="none"). The caller
// MUST NOT auto-approve; admin queue decides. Never fail-open into "looks
// verified."
//
// RDY-037: relocated from Cena.Admin.Api.QualityGate → Cena.Actors.Cas. The
// gate is a domain invariant per ADR-0002; it belongs in the domain layer
// alongside ICasRouterService so every adapter (Admin.Api, Actors ingest,
// future hosts) can enforce it without reverse-layer dependencies.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Cas;

/// <summary>
/// RDY-034: Outcome categories returned by the CAS ingestion gate.
/// </summary>
public enum CasGateOutcome
{
    /// <summary>CAS engine confirmed the authored answer.</summary>
    Verified,

    /// <summary>
    /// No math detected (non-math subject + no math content), or CAS can't
    /// express a verdict — proceed but mark binding Unverifiable.
    /// </summary>
    Unverifiable,

    /// <summary>CAS engine ran and contradicted the authored answer.</summary>
    Failed,

    /// <summary>
    /// CAS oracle temporarily unavailable (circuit breaker open / sidecar
    /// error). Gate fails safe — caller forces NeedsReview.
    /// </summary>
    CircuitOpen
}

/// <summary>
/// RDY-034: CAS gate result. Contains the populated <see cref="QuestionCasBinding"/>
/// the caller is expected to persist in the same Marten session as the
/// question stream (atomic write).
/// </summary>
public sealed record CasGateResult(
    CasGateOutcome Outcome,
    string Engine,
    string CanonicalAnswer,
    string CorrectAnswerHash,
    double LatencyMs,
    string? FailureReason,
    QuestionCasBinding Binding);

/// <summary>
/// RDY-034 / ADR-0002: Ingestion-side CAS gate contract.
/// </summary>
public interface ICasVerificationGate
{
    /// <summary>
    /// Verify the authored answer for a new or updated question.
    /// Never throws for domain failures — returns CasGateResult with the
    /// appropriate <see cref="CasGateOutcome"/>. Throws only on programmer
    /// error / cancellation.
    /// </summary>
    Task<CasGateResult> VerifyForCreateAsync(
        string questionId,
        string subject,
        string stem,
        string correctAnswerRaw,
        string? variable,
        CancellationToken ct = default);
}

/// <summary>
/// RDY-034: Default gate implementation used by QuestionBankService,
/// AiGenerationService, and the bulk-ingest persister.
/// </summary>
public sealed class CasVerificationGate : ICasVerificationGate
{
    private readonly ICasRouterService _casRouter;
    private readonly IMathContentDetector _mathDetector;
    private readonly IStemSolutionExtractor _stemExtractor;
    private readonly IDocumentStore _store;
    private readonly ILogger<CasVerificationGate> _logger;

    // OpenTelemetry metrics (Cena.Cas.Gate meter — RDY-036)
    private static readonly Meter Meter = new("Cena.Cas.Gate", "1.0");
    private static readonly Counter<long> VerificationTotal = Meter.CreateCounter<long>(
        "cena_cas_verification_total",
        description: "CAS verification attempts bucketed by result/engine");
    private static readonly Histogram<double> VerificationDuration = Meter.CreateHistogram<double>(
        "cena_cas_verification_duration_seconds",
        unit: "s",
        description: "CAS verification wall-clock latency");
    private static readonly Counter<long> QuestionsRejected = Meter.CreateCounter<long>(
        "cena_questions_rejected_cas_total",
        description: "Questions rejected by the CAS ingestion gate");
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "cena_cas_cache_hit_total",
        description: "CAS gate idempotency-cache hits");

    public CasVerificationGate(
        ICasRouterService casRouter,
        IMathContentDetector mathDetector,
        IStemSolutionExtractor stemExtractor,
        IDocumentStore store,
        ILogger<CasVerificationGate> logger)
    {
        _casRouter = casRouter;
        _mathDetector = mathDetector;
        _stemExtractor = stemExtractor;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CasGateResult> VerifyForCreateAsync(
        string questionId,
        string subject,
        string stem,
        string correctAnswerRaw,
        string? variable,
        CancellationToken ct = default)
    {
        correctAnswerRaw ??= string.Empty;
        var hash = QuestionCasBinding.ComputeAnswerHash(correctAnswerRaw);
        var now = DateTimeOffset.UtcNow;

        // 1. Decide whether CAS verification is required at all.
        var detection = _mathDetector.Analyze(stem ?? string.Empty, subject ?? string.Empty);
        bool mathSubject = IsMathOrPhysics(subject);

        if (!detection.HasMathContent && !mathSubject)
        {
            var binding = BuildBinding(questionId, correctAnswerRaw, hash,
                canonical: correctAnswerRaw,
                engine: CasEngine.SymPy, // doc default; Engine field on result is "n/a"
                status: CasBindingStatus.Unverifiable,
                latencyMs: 0,
                failureReason: null,
                verifiedAt: now);

            VerificationTotal.Add(1,
                new KeyValuePair<string, object?>("result", "unverifiable"),
                new KeyValuePair<string, object?>("engine", "n/a"));

            return new CasGateResult(
                CasGateOutcome.Unverifiable, "n/a", correctAnswerRaw, hash, 0, null, binding);
        }

        // 2. Idempotency cache — same (questionId, hash) already Verified?
        try
        {
            await using var querySession = _store.QuerySession();
            var cached = await querySession.Query<QuestionCasBinding>()
                .Where(b => b.QuestionId == questionId && b.CorrectAnswerHash == hash)
                .FirstOrDefaultAsync(ct);

            if (cached is not null && cached.Status == CasBindingStatus.Verified)
            {
                CacheHits.Add(1);
                _logger.LogDebug(
                    "[CAS_GATE_CACHE_HIT] questionId={Qid} hash={Hash} engine={Engine}",
                    questionId, hash, cached.Engine);

                return new CasGateResult(
                    CasGateOutcome.Verified,
                    cached.Engine.ToString(),
                    cached.CanonicalAnswer,
                    hash,
                    cached.LatencyMs,
                    null,
                    cached);
            }
        }
        catch (Exception ex)
        {
            // Cache lookup failures never block verification — log + continue.
            _logger.LogDebug(ex,
                "[CAS_GATE_CACHE_MISS_ERROR] questionId={Qid} — falling through to CAS call", questionId);
        }

        // 3. RDY-038 / ADR-0002: determine what we're actually verifying.
        // Parseability alone is NOT correctness. We extract the expected
        // solution from the stem when we can, and send an Equivalence /
        // Solve request to the router. When the stem is not extractable,
        // we still probe with NormalForm to catch unparseable answers,
        // BUT we persist the binding as Unverifiable — never Verified —
        // because we cannot prove the answer is correct, only that it
        // parses. Admin queue decides.
        var extraction = _stemExtractor.Extract(stem ?? string.Empty, subject);
        string operationUsed = "NormalForm";
        bool stemBasedVerification = false;

        var sw = Stopwatch.StartNew();
        CasVerifyRequest request;
        switch (extraction)
        {
            case StemExtraction.ExpressionOnly expr:
                // The stem asks the student to produce the canonical form of
                // `expr`. Author answer must be CAS-equivalent to it.
                request = new CasVerifyRequest(
                    Operation: CasOperation.Equivalence,
                    ExpressionA: correctAnswerRaw,
                    ExpressionB: expr.Expression,
                    Variable: variable ?? expr.Variable,
                    Tolerance: 1e-9);
                operationUsed = "Equivalence";
                stemBasedVerification = true;
                break;

            case StemExtraction.Equation eq:
                // Build a residual expression: (lhs) - (rhs). The author's
                // answer is a proposed root; substituting it into the
                // residual MUST collapse to zero. We encode that as an
                // Equivalence check between (lhs - rhs) evaluated at the
                // author's answer and the literal 0 — SymPy handles the
                // substitution symbolically.
                //
                // Concretely we send `(lhs) - (rhs)` vs `0`, with the
                // variable pinned to the author's answer via a simple
                // textual substitution. If no variable was identified we
                // fall through to parseability-Unverifiable.
                var varName = variable ?? eq.Variable;
                if (string.IsNullOrWhiteSpace(varName))
                {
                    request = new CasVerifyRequest(
                        Operation: CasOperation.NormalForm,
                        ExpressionA: correctAnswerRaw,
                        ExpressionB: null,
                        Variable: null,
                        Tolerance: 1e-9);
                    break;
                }
                // Build `(lhs) - (rhs)` substituted with author answer, compared to 0.
                var substituted = $"(({eq.Lhs}) - ({eq.Rhs}))".Replace(
                    varName, $"({correctAnswerRaw})", StringComparison.Ordinal);
                request = new CasVerifyRequest(
                    Operation: CasOperation.Equivalence,
                    ExpressionA: substituted,
                    ExpressionB: "0",
                    Variable: varName,
                    Tolerance: 1e-9);
                operationUsed = "Equivalence(equation-residual)";
                stemBasedVerification = true;
                break;

            default:
                request = new CasVerifyRequest(
                    Operation: CasOperation.NormalForm,
                    ExpressionA: correctAnswerRaw,
                    ExpressionB: null,
                    Variable: variable,
                    Tolerance: 1e-9);
                break;
        }

        CasVerifyResult casResult;
        try
        {
            casResult = await _casRouter.VerifyAsync(request, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "[CAS_GATE_UNEXPECTED] questionId={Qid} op={Op}",
                questionId, operationUsed);
            casResult = CasVerifyResult.Error(request.Operation, "none",
                sw.Elapsed.TotalMilliseconds, ex.Message, CasVerifyStatus.Error);
        }
        sw.Stop();

        VerificationDuration.Record(casResult.LatencyMs / 1000.0,
            new KeyValuePair<string, object?>("engine", casResult.Engine ?? "unknown"));

        // 4. Map status → outcome.
        switch (casResult.Status)
        {
            case CasVerifyStatus.CircuitBreakerOpen:
            {
                var binding = BuildBinding(questionId, correctAnswerRaw, hash,
                    canonical: correctAnswerRaw,
                    engine: CasEngine.SymPy,
                    status: CasBindingStatus.Unverifiable,
                    latencyMs: casResult.LatencyMs,
                    failureReason: casResult.ErrorMessage,
                    verifiedAt: now);

                VerificationTotal.Add(1,
                    new KeyValuePair<string, object?>("result", "error"),
                    new KeyValuePair<string, object?>("engine", "none"));

                return new CasGateResult(
                    CasGateOutcome.CircuitOpen, "none",
                    correctAnswerRaw, hash, casResult.LatencyMs, casResult.ErrorMessage, binding);
            }

            case CasVerifyStatus.Ok:
            {
                if (casResult.Verified)
                {
                    var canonical = casResult.SimplifiedA ?? correctAnswerRaw;
                    var engine = MapEngine(casResult.Engine ?? string.Empty);

                    // ★ RDY-038 / ADR-0002: Parseability is NOT correctness.
                    // Only mark Verified if we actually ran an Equivalence /
                    // Solve check driven by the stem. A bare NormalForm
                    // simplify only tells us the author's answer string is
                    // well-formed math — it says nothing about whether it
                    // is the right answer for this question. Such questions
                    // must be persisted Unverifiable and routed to manual
                    // review; they must not auto-approve.
                    if (!stemBasedVerification)
                    {
                        var passiveBinding = BuildBinding(questionId, correctAnswerRaw, hash,
                            canonical: canonical,
                            engine: engine,
                            status: CasBindingStatus.Unverifiable,
                            latencyMs: casResult.LatencyMs,
                            failureReason: "stem_non_extractable_parseability_only",
                            verifiedAt: now);

                        VerificationTotal.Add(1,
                            new KeyValuePair<string, object?>("result", "unverifiable"),
                            new KeyValuePair<string, object?>("engine", engine.ToString()));

                        _logger.LogInformation(
                            "[CAS_GATE_UNVERIFIABLE_PROSE] questionId={Qid} — stem not extractable; " +
                            "parseability OK but correctness unproven; NeedsReview", questionId);

                        return new CasGateResult(
                            CasGateOutcome.Unverifiable, engine.ToString(),
                            canonical, hash, casResult.LatencyMs,
                            "stem_non_extractable_parseability_only", passiveBinding);
                    }

                    var binding = BuildBinding(questionId, correctAnswerRaw, hash,
                        canonical: canonical,
                        engine: engine,
                        status: CasBindingStatus.Verified,
                        latencyMs: casResult.LatencyMs,
                        failureReason: null,
                        verifiedAt: now);

                    VerificationTotal.Add(1,
                        new KeyValuePair<string, object?>("result", "verified"),
                        new KeyValuePair<string, object?>("engine", engine.ToString()));

                    return new CasGateResult(
                        CasGateOutcome.Verified, engine.ToString(),
                        canonical, hash, casResult.LatencyMs, null, binding);
                }
                else
                {
                    var engine = MapEngine(casResult.Engine ?? string.Empty);
                    var binding = BuildBinding(questionId, correctAnswerRaw, hash,
                        canonical: casResult.SimplifiedA ?? correctAnswerRaw,
                        engine: engine,
                        status: CasBindingStatus.Failed,
                        latencyMs: casResult.LatencyMs,
                        failureReason: casResult.ErrorMessage,
                        verifiedAt: now);

                    VerificationTotal.Add(1,
                        new KeyValuePair<string, object?>("result", "failed"),
                        new KeyValuePair<string, object?>("engine", engine.ToString()));
                    QuestionsRejected.Add(1,
                        new KeyValuePair<string, object?>("reason", "cas_failed"),
                        new KeyValuePair<string, object?>("subject", subject ?? "unknown"));

                    return new CasGateResult(
                        CasGateOutcome.Failed, engine.ToString(),
                        casResult.SimplifiedA ?? correctAnswerRaw, hash, casResult.LatencyMs,
                        casResult.ErrorMessage, binding);
                }
            }

            default: // Error / Timeout / UnsupportedOperation
            {
                var binding = BuildBinding(questionId, correctAnswerRaw, hash,
                    canonical: correctAnswerRaw,
                    engine: CasEngine.SymPy,
                    status: CasBindingStatus.Unverifiable,
                    latencyMs: casResult.LatencyMs,
                    failureReason: casResult.ErrorMessage,
                    verifiedAt: now);

                VerificationTotal.Add(1,
                    new KeyValuePair<string, object?>("result", "error"),
                    new KeyValuePair<string, object?>("engine", "none"));

                return new CasGateResult(
                    CasGateOutcome.CircuitOpen, "none",
                    correctAnswerRaw, hash, casResult.LatencyMs, casResult.ErrorMessage, binding);
            }
        }
    }

    private static bool IsMathOrPhysics(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        return subject.Equals("math", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("mathematics", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("maths", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("physics", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("chemistry", StringComparison.OrdinalIgnoreCase);
    }

    private static CasEngine MapEngine(string engine) => engine switch
    {
        "MathNet" => CasEngine.MathNet,
        "SymPy" => CasEngine.SymPy,
        "Giac" => CasEngine.Giac,
        _ => CasEngine.SymPy
    };

    private static QuestionCasBinding BuildBinding(
        string questionId,
        string rawAnswer,
        string hash,
        string canonical,
        CasEngine engine,
        CasBindingStatus status,
        double latencyMs,
        string? failureReason,
        DateTimeOffset verifiedAt) =>
        new()
        {
            Id = questionId,
            QuestionId = questionId,
            Engine = engine,
            CanonicalAnswer = canonical,
            StepCanonicals = Array.Empty<string>(),
            EquivalenceMode = EquivalenceMode.Symbolic,
            VerifiedAt = verifiedAt,
            HasCrossEngineDisagreement = false,
            DisagreementDetails = null,
            Status = status,
            CorrectAnswerRaw = rawAnswer ?? string.Empty,
            CorrectAnswerHash = hash,
            LatencyMs = latencyMs,
            FailureReason = failureReason
        };
}
