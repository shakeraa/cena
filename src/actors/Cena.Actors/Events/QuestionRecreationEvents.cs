// =============================================================================
// Cena Platform — Question Recreation Events (RDY-058)
//
// Provenance record for curator-initiated "generate similar" runs. Appended
// to the PARENT question's Marten stream so you can query "how many
// variants has this question seeded?" without joining on a separate index.
//
// Children are still written through CasGatedQuestionPersister (the single
// gated writer site — see SeedLoaderMustUseQuestionBankServiceTest). This
// event records the generation request itself, not any individual child;
// child creation events live on each child's own stream.
// =============================================================================

namespace Cena.Actors.Events;

public sealed record QuestionSimilarGenerated_V1(
    string ParentQuestionId,
    int Count,
    float MinDifficulty,
    float MaxDifficulty,
    string? Language,
    string GeneratedBy,
    DateTimeOffset Timestamp);

/// <summary>
/// RDY-059: Batch corpus-expansion run audit event. Appended once per
/// CorpusExpansionRequest execution (stream: <c>corpus-expansion:{runId}</c>).
/// Children emitted by the run carry their own per-question provenance via
/// <see cref="QuestionSimilarGenerated_V1"/>; this event is the operator-
/// level audit trail answering "who ran what selector, when, and what did
/// it cost us in CAS drops?".
/// </summary>
public sealed record CorpusExpansionRun_V1(
    string RunId,
    string Selector,
    int SourceCount,
    int TotalAttempted,
    int TotalPassedCas,
    int TotalDropped,
    bool DryRun,
    string StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);
