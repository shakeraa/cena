// =============================================================================
// Cena Platform — Peer Explanation Pipeline (prr-025, ADR-0002)
//
// Every peer-authored math explanation is routed through the CAS oracle BEFORE
// another student can see it. If the CAS verdict disagrees with the author's
// claimed answer, the explanation is diverted to the teacher-moderation queue
// instead of silently dropped — teachers can coach misconceptions, they just
// can't reach other students unverified.
//
// Why this pipeline exists (persona-ministry + persona-redteam, 2026-04-20):
//   A student who posts "x² - 5x + 6 = 0 → x = 2 or x = 4" into a study circle
//   will propagate a misconception to every peer who reads it. The LLM is
//   not the oracle — SymPy is (ADR-0002). Peer-authored content is therefore
//   the highest-risk content class: human-authored, cheaper than LLM calls,
//   but with no correctness signal unless we impose one. This pipeline is
//   that signal.
//
// Invariant (enforced by PeerExplanationCasGatedTest.cs):
//   NO code path that persists-as-visible-to-peers a peer explanation MAY skip
//   the `ICasRouterService.VerifyAsync` call. A CAS `Status != Ok` or
//   `Verified == false` result routes to the moderation queue, not the feed.
//
// ADR-0002 circuit-breaker policy:
//   When CAS is unavailable (circuit-breaker open), we fail CLOSED — the
//   explanation is routed to the moderation queue (not auto-delivered). This
//   mirrors CasVerificationGate.cs: never fail-open into "looks verified."
//
// ADR-0003 session scope:
//   This pipeline does NOT touch misconception state. It produces a binary
//   outcome (delivered-to-peers | moderation-queued) plus telemetry counters.
//   Nothing is written to student profiles.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Actors.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Collaboration;

/// <summary>
/// prr-025: A peer-authored explanation that claims to solve a math problem.
/// The author has already stated their claimed answer in structured form
/// (claimedAnswer) so we can verify against it without parsing prose.
/// </summary>
/// <param name="ExplanationId">Stable id (peer-content stream key).</param>
/// <param name="AuthorStudentId">Peer who authored the explanation.</param>
/// <param name="InstituteId">Tenant the explanation belongs to (ADR-0001).</param>
/// <param name="QuestionId">Question the explanation targets.</param>
/// <param name="ClaimedAnswer">The author's structured answer (e.g. "x=2, x=3").</param>
/// <param name="ReferenceAnswer">Canonical authored answer for the question.</param>
/// <param name="Variable">Variable in play, if any (for CAS routing).</param>
/// <param name="ProseBodyLength">
/// Character length of the prose body. The prose itself is NOT verified by CAS
/// (prose goes through a separate moderator — prr-056). Only the math is.
/// </param>
public sealed record PeerExplanationSubmission(
    string ExplanationId,
    string AuthorStudentId,
    string InstituteId,
    string QuestionId,
    string ClaimedAnswer,
    string ReferenceAnswer,
    string? Variable,
    int ProseBodyLength);

/// <summary>
/// prr-025: Terminal state of the pipeline. Only <see cref="Delivered"/>
/// reaches another student's feed; everything else sits in the teacher
/// moderation queue.
/// </summary>
public enum PeerExplanationOutcome
{
    /// <summary>CAS confirmed the math — explanation is visible to peers.</summary>
    Delivered,

    /// <summary>CAS disagreed with the author — teacher moderation required.</summary>
    RoutedToModeration,

    /// <summary>
    /// CAS oracle unavailable (circuit-breaker open / sidecar timeout).
    /// Fail-closed: explanation held in moderation until CAS is back.
    /// </summary>
    HeldPendingCas,

    /// <summary>
    /// Submission rejected before reaching CAS (e.g. missing claimed answer,
    /// non-math subject without teacher flag). Never reaches peers.
    /// </summary>
    Rejected
}

/// <summary>prr-025: Pipeline result returned to the caller.</summary>
public sealed record PeerExplanationResult(
    PeerExplanationOutcome Outcome,
    string Engine,
    double CasLatencyMs,
    string? ModerationReason,
    CasVerifyStatus CasStatus);

/// <summary>prr-025: Pipeline contract.</summary>
public interface IPeerExplanationPipeline
{
    /// <summary>
    /// Run the submission through CAS and route to feed or moderation.
    /// Never throws for domain outcomes — returns a PeerExplanationResult.
    /// </summary>
    Task<PeerExplanationResult> ProcessAsync(
        PeerExplanationSubmission submission,
        CancellationToken ct = default);
}

/// <summary>prr-025: Teacher-moderation queue sink.</summary>
public interface IPeerExplanationModerationQueue
{
    /// <summary>
    /// Enqueue the explanation for teacher review. Implementations persist
    /// to Marten in the tenant's stream (ADR-0001 isolation).
    /// </summary>
    Task EnqueueAsync(
        PeerExplanationSubmission submission,
        string moderationReason,
        CancellationToken ct = default);
}

/// <summary>prr-025: Peer-visible feed sink (opaque to this pipeline).</summary>
public interface IPeerExplanationFeed
{
    /// <summary>
    /// Publish the verified explanation to the per-tenant peer feed.
    /// Called only for the CAS-verified path.
    /// </summary>
    Task PublishAsync(
        PeerExplanationSubmission submission,
        CancellationToken ct = default);
}

/// <summary>
/// prr-025: The pipeline implementation. CAS-gated, fail-closed, stateless.
/// </summary>
public sealed class PeerExplanationPipeline : IPeerExplanationPipeline
{
    private readonly ICasRouterService _cas;
    private readonly IPeerExplanationFeed _feed;
    private readonly IPeerExplanationModerationQueue _moderation;
    private readonly ILogger<PeerExplanationPipeline> _logger;

    private static readonly Meter Meter = new("Cena.Collaboration.PeerExplanation", "1.0");
    private static readonly Counter<long> ProcessedTotal = Meter.CreateCounter<long>(
        "cena_peer_explanation_processed_total",
        description: "Peer explanation pipeline outcomes, by outcome+engine (prr-025).");
    private static readonly Histogram<double> CasLatencyMs = Meter.CreateHistogram<double>(
        "cena_peer_explanation_cas_latency_ms",
        description: "CAS verify latency observed by the peer-explanation pipeline.");

    public PeerExplanationPipeline(
        ICasRouterService cas,
        IPeerExplanationFeed feed,
        IPeerExplanationModerationQueue moderation,
        ILogger<PeerExplanationPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(cas);
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(moderation);
        ArgumentNullException.ThrowIfNull(logger);
        _cas = cas;
        _feed = feed;
        _moderation = moderation;
        _logger = logger;
    }

    public async Task<PeerExplanationResult> ProcessAsync(
        PeerExplanationSubmission submission,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        // Structural guardrail: claimed answer and reference answer must both
        // be present. Without the claimed answer we have nothing to verify;
        // without the reference answer CAS has nothing to verify against.
        if (string.IsNullOrWhiteSpace(submission.ClaimedAnswer)
            || string.IsNullOrWhiteSpace(submission.ReferenceAnswer))
        {
            _logger.LogWarning(
                "[PEER_EXPL_REJECTED] explanationId={ExplId} reason=missing-answer " +
                "author={AuthorStudentId} tenant={InstituteId}",
                submission.ExplanationId,
                submission.AuthorStudentId,
                submission.InstituteId);

            ProcessedTotal.Add(1,
                new KeyValuePair<string, object?>("outcome", "rejected"),
                new KeyValuePair<string, object?>("reason", "missing-answer"));

            return new PeerExplanationResult(
                PeerExplanationOutcome.Rejected,
                Engine: "none",
                CasLatencyMs: 0,
                ModerationReason: "missing claimed or reference answer",
                CasStatus: CasVerifyStatus.UnsupportedOperation);
        }

        // CAS verify: are the claimed and reference answers mathematically
        // equivalent? We use Equivalence (not StepValidity) because the peer
        // explanation asserts a *result*, not a rewrite chain. Step validity
        // is a separate gate covered by StepVerifierService.
        var request = new CasVerifyRequest(
            Operation: CasOperation.Equivalence,
            ExpressionA: submission.ClaimedAnswer,
            ExpressionB: submission.ReferenceAnswer,
            Variable: submission.Variable);

        var sw = Stopwatch.StartNew();
        CasVerifyResult cas;
        try
        {
            cas = await _cas.VerifyAsync(request, ct);
        }
        catch (OperationCanceledException)
        {
            throw; // caller-driven cancel — propagate.
        }
        catch (Exception ex)
        {
            // CAS router unexpectedly threw (sidecar died mid-RPC, etc.).
            // Fail-closed: route to moderation; never fail-open.
            _logger.LogError(ex,
                "[PEER_EXPL_CAS_THREW] explanationId={ExplId} — routing to moderation (fail-closed).",
                submission.ExplanationId);

            sw.Stop();
            await _moderation.EnqueueAsync(submission, "cas-exception", ct);
            ProcessedTotal.Add(1,
                new KeyValuePair<string, object?>("outcome", "held-pending-cas"),
                new KeyValuePair<string, object?>("engine", "exception"));
            return new PeerExplanationResult(
                PeerExplanationOutcome.HeldPendingCas,
                Engine: "exception",
                CasLatencyMs: sw.Elapsed.TotalMilliseconds,
                ModerationReason: "cas oracle threw: " + ex.GetType().Name,
                CasStatus: CasVerifyStatus.Error);
        }

        CasLatencyMs.Record(cas.LatencyMs,
            new KeyValuePair<string, object?>("engine", cas.Engine));

        // ── Engine unavailable → fail-closed to moderation ──
        if (cas.Status == CasVerifyStatus.CircuitBreakerOpen
            || cas.Status == CasVerifyStatus.Timeout
            || cas.Status == CasVerifyStatus.Error)
        {
            _logger.LogWarning(
                "[PEER_EXPL_HELD] explanationId={ExplId} engine={Engine} status={Status} — " +
                "CAS unavailable; routing to moderation until CAS recovers.",
                submission.ExplanationId, cas.Engine, cas.Status);

            await _moderation.EnqueueAsync(submission, $"cas-{cas.Status}", ct);
            ProcessedTotal.Add(1,
                new KeyValuePair<string, object?>("outcome", "held-pending-cas"),
                new KeyValuePair<string, object?>("engine", cas.Engine));
            return new PeerExplanationResult(
                PeerExplanationOutcome.HeldPendingCas,
                Engine: cas.Engine,
                CasLatencyMs: cas.LatencyMs,
                ModerationReason: $"cas-{cas.Status}",
                CasStatus: cas.Status);
        }

        // ── CAS ran, disagreed → teacher moderation ──
        if (!cas.Verified)
        {
            _logger.LogInformation(
                "[PEER_EXPL_DISAGREED] explanationId={ExplId} engine={Engine} " +
                "latencyMs={Ms:F1} — routing to moderation.",
                submission.ExplanationId, cas.Engine, cas.LatencyMs);

            await _moderation.EnqueueAsync(submission, "cas-disagreed", ct);
            ProcessedTotal.Add(1,
                new KeyValuePair<string, object?>("outcome", "routed-to-moderation"),
                new KeyValuePair<string, object?>("engine", cas.Engine));
            return new PeerExplanationResult(
                PeerExplanationOutcome.RoutedToModeration,
                Engine: cas.Engine,
                CasLatencyMs: cas.LatencyMs,
                ModerationReason: "cas-disagreed",
                CasStatus: cas.Status);
        }

        // ── CAS ran, agreed → peer feed ──
        _logger.LogInformation(
            "[PEER_EXPL_DELIVERED] explanationId={ExplId} engine={Engine} latencyMs={Ms:F1}",
            submission.ExplanationId, cas.Engine, cas.LatencyMs);

        await _feed.PublishAsync(submission, ct);
        ProcessedTotal.Add(1,
            new KeyValuePair<string, object?>("outcome", "delivered"),
            new KeyValuePair<string, object?>("engine", cas.Engine));
        return new PeerExplanationResult(
            PeerExplanationOutcome.Delivered,
            Engine: cas.Engine,
            CasLatencyMs: cas.LatencyMs,
            ModerationReason: null,
            CasStatus: cas.Status);
    }
}
