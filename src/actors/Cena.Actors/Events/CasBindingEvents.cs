// =============================================================================
// Cena Platform — CAS Binding Events (CAS-BIND-001)
// =============================================================================

using Cena.Infrastructure.Documents;

namespace Cena.Actors.Events;

/// <summary>
/// CAS-BIND-001: Emitted when a question is bound to a CAS engine.
/// This happens at authoring time and during nightly re-verification.
/// </summary>
public record QuestionCasBindingSet_V1(
    string QuestionId,
    CasEngine Engine,
    string CanonicalAnswer,
    IReadOnlyList<string> StepCanonicals,
    EquivalenceMode EquivalenceMode,
    string SetBy,
    DateTimeOffset SetAt
) : IDelegatedEvent;

/// <summary>
/// Emitted when the nightly batch detects a cross-engine disagreement.
/// Flagged for human review — the question should not be served until resolved.
/// </summary>
public record CasDisagreementDetected_V1(
    string QuestionId,
    CasEngine Engine1,
    string Engine1Result,
    CasEngine Engine2,
    string Engine2Result,
    string StudentExpression,
    DateTimeOffset DetectedAt
) : IDelegatedEvent;
