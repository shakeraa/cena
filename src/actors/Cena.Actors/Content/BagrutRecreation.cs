// =============================================================================
// Cena Platform — Bagrut Recreation Pipeline (RDY-072 Phase 1A)
//
// A BagrutRecreation is an AI-authored ORIGINAL item inspired by the
// structural pattern of a Ministry-published Bagrut exam item. Per
// ADR-0033 + Ran's Round 4 demand, student-facing items are AI
// recreations CAS-gated by SymPy (ADR-0002), never raw Ministry text.
//
// Phase 1A scope: aggregate + review workflow state machine + legal
// disclosure types. The actual generate-and-verify pipeline
// (RecreationPipeline.cs) + Vue review-queue UI land in Phase 1B
// with Prof. Amjad's sign-off on the procedure doc.
// =============================================================================

namespace Cena.Actors.Content;

/// <summary>
/// Lifecycle states of a candidate recreation. The workflow moves
/// strictly forward except for Rejected (terminal) and Needs-Revision
/// which cycles back to Submitted with a new candidate.
/// </summary>
public enum RecreationReviewState
{
    Draft = 0,
    Submitted = 1,
    CasVerified = 2,
    ExpertReviewing = 3,
    Approved = 4,
    NeedsRevision = 5,
    Rejected = 6
}

/// <summary>
/// Source metadata for a recreation. Kept as reference only; NEVER
/// published as part of the student-facing item. Used for internal
/// audit + the expert-review UI so the reviewer can see which
/// Ministry item the candidate is modelled on.
/// </summary>
public sealed record MinistryReference(
    string MinistryCode,
    int ExamYear,
    string MoedSlug,
    string QuestionNumber,
    string TopicSlug);

/// <summary>
/// AI-generation fingerprint for traceability. Ministry staff or
/// auditors can re-produce the generation path from this record.
/// </summary>
public sealed record GenerationFingerprint(
    string ModelId,
    string PromptTemplateHash,
    int TemperatureE4,        // temperature × 10_000 as int so record is value-equal
    int CandidateIndex,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// CAS verification summary. Every candidate that reaches
/// <see cref="RecreationReviewState.CasVerified"/> carries one of
/// these; without it, the workflow refuses transition.
/// </summary>
public sealed record CasVerificationResult(
    bool SymbolicallyCorrect,
    string CasExpressionHash,
    string VerifierVersion,
    TimeSpan VerificationLatency);

/// <summary>
/// Expert-reviewer decision.
/// </summary>
public sealed record ExpertReviewDecision(
    string ReviewerId,
    bool Approved,
    string Notes,
    DateTimeOffset DecidedAtUtc);

/// <summary>
/// Aggregate root. One candidate recreation goes through the workflow;
/// the live item bank references the approved candidate's id.
/// </summary>
public sealed class BagrutRecreationAggregate
{
    public string RecreationId { get; }
    public RecreationReviewState State { get; private set; }
    public MinistryReference Reference { get; }
    public GenerationFingerprint Fingerprint { get; }
    public CasVerificationResult? CasResult { get; private set; }
    public ExpertReviewDecision? ExpertDecision { get; private set; }
    public string? RevisionReason { get; private set; }

    public BagrutRecreationAggregate(
        string recreationId,
        MinistryReference reference,
        GenerationFingerprint fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recreationId);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(fingerprint);
        RecreationId = recreationId;
        Reference = reference;
        Fingerprint = fingerprint;
        State = RecreationReviewState.Draft;
    }

    public void Submit()
    {
        RequireState(RecreationReviewState.Draft, nameof(Submit));
        State = RecreationReviewState.Submitted;
    }

    public void RecordCasVerification(CasVerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RequireState(RecreationReviewState.Submitted, nameof(RecordCasVerification));
        CasResult = result;
        State = result.SymbolicallyCorrect
            ? RecreationReviewState.CasVerified
            : RecreationReviewState.Rejected;
        if (!result.SymbolicallyCorrect)
            RevisionReason = "CAS verification failed; candidate is not symbolically equivalent.";
    }

    public void StartExpertReview()
    {
        RequireState(RecreationReviewState.CasVerified, nameof(StartExpertReview));
        State = RecreationReviewState.ExpertReviewing;
    }

    public void RecordExpertDecision(ExpertReviewDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        RequireState(RecreationReviewState.ExpertReviewing, nameof(RecordExpertDecision));
        ExpertDecision = decision;
        if (decision.Approved)
        {
            State = RecreationReviewState.Approved;
        }
        else
        {
            State = string.IsNullOrWhiteSpace(decision.Notes)
                ? RecreationReviewState.Rejected
                : RecreationReviewState.NeedsRevision;
            RevisionReason = decision.Notes;
        }
    }

    /// <summary>
    /// True only in the Approved terminal state — this is the only
    /// signal the item-bank loader uses to decide whether to surface
    /// the item to students. CAS-verified alone is insufficient; we
    /// require expert sign-off too per Prof. Amjad + Ran.
    /// </summary>
    public bool IsApprovedForProduction => State == RecreationReviewState.Approved;

    private void RequireState(RecreationReviewState expected, string action)
    {
        if (State != expected)
            throw new InvalidOperationException(
                $"Cannot {action} on recreation in state {State}; expected {expected}.");
    }
}

/// <summary>
/// Legal disclosure metadata that accompanies every approved
/// recreation when it lands in the item bank. The rendering layer
/// shows the <see cref="PublicAttribution"/> string to students so
/// the Ministry-reference posture is honest, never "from Bagrut 2024"
/// (that would imply Ministry publication which we do not have).
/// </summary>
public sealed record RecreationDisclosure(
    string RecreationId,
    string PublicAttribution,
    string InternalReferenceNote)
{
    public static RecreationDisclosure ForApproved(
        BagrutRecreationAggregate aggregate)
    {
        if (!aggregate.IsApprovedForProduction)
            throw new InvalidOperationException(
                "Disclosure can only be emitted for approved recreations.");

        return new RecreationDisclosure(
            RecreationId: aggregate.RecreationId,
            PublicAttribution:
                $"Inspired by Bagrut {aggregate.Reference.ExamYear} "
                + $"{aggregate.Reference.MoedSlug}",
            InternalReferenceNote:
                $"Ministry code {aggregate.Reference.MinistryCode}, "
                + $"Q{aggregate.Reference.QuestionNumber}; "
                + $"AI recreation {aggregate.RecreationId}, "
                + $"CAS-verified, expert-approved by "
                + $"{aggregate.ExpertDecision?.ReviewerId ?? "(unknown)"}.");
    }
}
