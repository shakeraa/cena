// =============================================================================
// Cena Platform — CAS-Gated Question Persister (RDY-037, ADR-0002 / ADR-0032)
//
// THE sole writer of new QuestionState streams. Every question-creation path
// in the codebase — admin UI, AI generation, Bagrut ingestion, seed data,
// test fixtures — MUST route through this service. The architectural
// guardrail test `SeedLoaderMustUseQuestionBankServiceTest` pattern-matches
// for `StartStream<QuestionState>` and fails the build if any file other
// than this one contains it.
//
// Why this exists:
//   RDY-034 wired the CAS gate into `QuestionBankService.CreateQuestionAsync`
//   but left two pre-existing bypasses (`QuestionBankSeedData`,
//   `IngestionOrchestrator`) allow-listed. That left the ADR-0002 invariant
//   locally enforced but globally bypassable. RDY-037 relocates the gate
//   primitives to `Cena.Actors.Cas` (this layer) so every caller can reach
//   the gate, and consolidates the write into this single method.
//
// Contract:
//   - Caller supplies the creation event (V1 or V2 flavour) and the
//     domain context needed for the gate (`subject`, `stem`,
//     `correctAnswerRaw`, optional variable name).
//   - Persister runs `ICasVerificationGate.VerifyForCreateAsync`.
//   - In Enforce mode: throws `CasVerificationFailedException` on Failed.
//   - In Shadow mode: records + continues; binding still persisted.
//   - In Off mode: skips the gate call; no binding persisted.
//   - Extra events (e.g., `QuestionApproved_V1` for auto-approve) append
//     onto the same newly-opened stream atomically with the creation event.
//   - Companion documents (e.g., `ModerationAuditDocument`) persist in
//     the same session/transaction.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery.Extraction;
using Cena.Actors.Questions;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Cas;

/// <summary>
/// RDY-037: Domain context the persister needs to feed the CAS gate. The
/// creation event itself encodes most of this data but not uniformly across
/// <c>QuestionAuthored_V2</c>, <c>QuestionIngested_V2</c>,
/// <c>QuestionAiGenerated_V2</c>, and their V1 predecessors — so callers
/// pass it explicitly.
///
/// ADR-0062 Phase 1 — concept-extraction hints are optional. Callers with
/// upstream-classified topic / track context (e.g. variant generation off
/// a Bagrut draft, ingestion off a curator-confirmed metadata row) pass
/// them so the rules-tier extractor canonicalises to a SkillCode. Callers
/// without context pass null; the extractor returns an empty set and the
/// curator picks at review time. The persister appends
/// <c>QuestionConceptsExtracted_V1</c> on every creation path when an
/// <c>IQuestionConceptExtractor</c> is bound, so no caller can forget to
/// emit the event — same single-writer logic as the CAS gate itself.
/// </summary>
public sealed record GatedPersistContext(
    string Subject,
    string Stem,
    string CorrectAnswerRaw,
    string Language,
    string? Variable = null,
    string? Latex = null,
    string? TrackHint = null,
    string? RuleTierHint = null,
    double RuleTierConfidence = 0.0);

/// <summary>
/// RDY-037: Outcome of a gated persist. The binding (if any) has already been
/// stored by the persister; the caller uses this for post-processing decisions
/// (auto-approval, telemetry, response shaping).
/// </summary>
public sealed record GatedPersistOutcome(
    CasGateOutcome Outcome,
    string Engine,
    string? FailureReason,
    string? CanonicalAnswer,
    QuestionCasBindingSummary? Binding);

/// <summary>
/// RDY-037: Minimal projection of <c>QuestionCasBinding</c> fields the
/// caller may need without a Marten re-read. The full binding lives in the
/// same session write.
/// </summary>
public sealed record QuestionCasBindingSummary(
    string Status,
    string CanonicalAnswer,
    string CorrectAnswerHash,
    double LatencyMs);

/// <summary>
/// RDY-037 / ADR-0002: The ONE legitimate writer of new QuestionState streams.
/// </summary>
public interface ICasGatedQuestionPersister
{
    /// <summary>
    /// Run the CAS gate, append the creation event (plus any extras) on a
    /// new QuestionState stream, persist the CAS binding and any companion
    /// documents atomically in the same session.
    /// </summary>
    /// <remarks>
    /// RDY-041: there is deliberately no way for the caller to supply a
    /// pre-computed <see cref="CasGateResult"/>. The persister always runs
    /// the gate itself — the gate's own idempotency cache (keyed on
    /// QuestionId + CorrectAnswerHash) absorbs the duplicate cost when a
    /// caller has already called the gate to decide on auto-approval
    /// events. This forecloses the trust-the-caller bypass where a
    /// forged result could sidestep ADR-0002.
    /// </remarks>
    /// <exception cref="CasVerificationFailedException">
    /// Thrown only in Enforce mode on <see cref="CasGateOutcome.Failed"/>.
    /// </exception>
    Task<GatedPersistOutcome> PersistAsync(
        string questionId,
        object creationEvent,
        GatedPersistContext context,
        IReadOnlyList<object>? extraEventsOnNewStream = null,
        IReadOnlyList<object>? companionDocuments = null,
        CancellationToken ct = default);

    /// <summary>
    /// RDY-039: Session-aware overload. When supplied, the persister uses
    /// the caller's <see cref="IDocumentSession"/> for the event append,
    /// binding store, and companion-document writes — and does NOT call
    /// <c>SaveChangesAsync</c>. Caller owns the unit-of-work so the
    /// question + binding commit atomically with the caller's other
    /// document writes (e.g. ingestion pipeline item).
    /// </summary>
    Task<GatedPersistOutcome> PersistAsync(
        IDocumentSession session,
        string questionId,
        object creationEvent,
        GatedPersistContext context,
        IReadOnlyList<object>? extraEventsOnNewStream = null,
        IReadOnlyList<object>? companionDocuments = null,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CasGatedQuestionPersister : ICasGatedQuestionPersister
{
    private readonly ICasVerificationGate _gate;
    private readonly ICasGateModeProvider _mode;
    private readonly IDocumentStore _store;
    private readonly ILogger<CasGatedQuestionPersister> _logger;
    // ADR-0062 Phase 1 — optional (nullable) so legacy host compositions
    // and unit tests that don't bind an extractor still build. Production
    // hosts (admin + student) bind RulesOnlyConceptExtractor by default.
    // When null, the persister silently skips the QuestionConceptsExtracted
    // append — same graceful-degradation pattern as the CAS gate's Off mode.
    private readonly IQuestionConceptExtractor? _conceptExtractor;

    public CasGatedQuestionPersister(
        ICasVerificationGate gate,
        ICasGateModeProvider mode,
        IDocumentStore store,
        ILogger<CasGatedQuestionPersister> logger,
        IQuestionConceptExtractor? conceptExtractor = null)
    {
        _gate = gate;
        _mode = mode;
        _store = store;
        _logger = logger;
        _conceptExtractor = conceptExtractor;
    }

    /// <inheritdoc />
    public async Task<GatedPersistOutcome> PersistAsync(
        string questionId,
        object creationEvent,
        GatedPersistContext context,
        IReadOnlyList<object>? extraEventsOnNewStream = null,
        IReadOnlyList<object>? companionDocuments = null,
        CancellationToken ct = default)
    {
        // Session-less overload: open + commit our own session.
        await using var session = _store.LightweightSession();
        var outcome = await PersistAsync(session, questionId, creationEvent, context,
            extraEventsOnNewStream, companionDocuments, ct);
        await session.SaveChangesAsync(ct);
        return outcome;
    }

    /// <inheritdoc />
    public async Task<GatedPersistOutcome> PersistAsync(
        IDocumentSession session,
        string questionId,
        object creationEvent,
        GatedPersistContext context,
        IReadOnlyList<object>? extraEventsOnNewStream = null,
        IReadOnlyList<object>? companionDocuments = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(questionId))
            throw new ArgumentException("questionId is required", nameof(questionId));
        ArgumentNullException.ThrowIfNull(creationEvent);
        ArgumentNullException.ThrowIfNull(context);

        // RDY-041: the persister always runs the gate itself. No caller-
        // supplied result is accepted — a forged Verified outcome would
        // silently bypass ADR-0002. The gate's own idempotency cache (in
        // CasVerificationGate) absorbs duplicate cost when a caller has
        // already called the gate to decide on auto-approval events.
        CasGateResult? gateResult = null;
        if (_mode.CurrentMode != CasGateMode.Off)
        {
            gateResult = await _gate.VerifyForCreateAsync(
                questionId,
                context.Subject ?? string.Empty,
                context.Stem ?? string.Empty,
                context.CorrectAnswerRaw ?? string.Empty,
                context.Variable,
                ct);
        }

        if (gateResult is not null
            && _mode.CurrentMode == CasGateMode.Enforce
            && gateResult.Outcome == CasGateOutcome.Failed)
        {
            _logger.LogWarning(
                "[CAS_GATE_REJECT] questionId={Qid} engine={Engine} reason={Reason}",
                questionId, gateResult.Engine, gateResult.FailureReason);
            throw new CasVerificationFailedException(
                gateResult.FailureReason
                ?? $"CAS verification failed for question {questionId}.");
        }

        // Build the event batch. Creation event MUST lead — it bootstraps
        // the QuestionState aggregate.
        var events = new List<object>(2 + (extraEventsOnNewStream?.Count ?? 0))
        {
            creationEvent
        };

        // ADR-0062 Phase 1 — emit QuestionConceptsExtracted_V1 immediately
        // after the creation event so the new question stream carries the
        // canonical concept set on the very first write. Done HERE, not
        // in callers, so every creation path (admin authoring, AI variant
        // generation, ingestion, seeders) gets the event for free — same
        // architectural rationale as the single-writer CAS gate.
        //
        // The extractor is closed-set: a topic hint that doesn't
        // canonicalise yields an empty Concepts list, which Apply() in
        // QuestionState + QuestionListProjection treats as a no-op
        // (curator picks at review). We never invent a SkillCode.
        if (_conceptExtractor is not null)
        {
            var extraction = await _conceptExtractor.ExtractAsync(
                new ExtractionInput(
                    QuestionId:         questionId,
                    Prompt:             context.Stem,
                    Latex:              context.Latex,
                    TrackHint:          context.TrackHint,
                    RuleTierHint:       context.RuleTierHint,
                    RuleTierConfidence: context.RuleTierConfidence),
                ct);

            events.Add(new QuestionConceptsExtracted_V1(
                QuestionId:         questionId,
                Concepts:           extraction.Concepts,
                ExtractionStrategy: extraction.Strategy,
                ExtractedBy:        nameof(CasGatedQuestionPersister),
                Timestamp:          DateTimeOffset.UtcNow));
        }

        if (extraEventsOnNewStream is not null)
            events.AddRange(extraEventsOnNewStream);

        // ★ The ONE StartStream<QuestionState> call in the entire repository.
        //   Do not copy this pattern elsewhere — route through this service.
        session.Events.StartStream<QuestionState>(questionId, events.ToArray());

        if (gateResult is not null)
            session.Store(gateResult.Binding);

        if (companionDocuments is not null)
        {
            foreach (var doc in companionDocuments)
            {
                if (doc is null) continue;
                session.Store((dynamic)doc);
            }
        }

        // RDY-039: caller owns SaveChangesAsync. The session-aware overload
        // intentionally does not commit here — the caller composes question
        // + binding + pipeline item into one transaction.

        _logger.LogInformation(
            "[CAS_GATED_PERSIST] questionId={Qid} outcome={Outcome} engine={Engine} mode={Mode}",
            questionId,
            gateResult?.Outcome.ToString() ?? "skipped",
            gateResult?.Engine ?? "none",
            _mode.CurrentMode);

        QuestionCasBindingSummary? summary = gateResult is null
            ? null
            : new QuestionCasBindingSummary(
                gateResult.Binding.Status.ToString(),
                gateResult.CanonicalAnswer,
                gateResult.CorrectAnswerHash,
                gateResult.LatencyMs);

        return new GatedPersistOutcome(
            gateResult?.Outcome ?? CasGateOutcome.Unverifiable,
            gateResult?.Engine ?? "off",
            gateResult?.FailureReason,
            gateResult?.CanonicalAnswer,
            summary);
    }
}
