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
