// =============================================================================
// Cena Platform -- Diagnostic Result
// MST-013: Result of the KST onboarding diagnostic
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Mastery;

/// <summary>
/// Result of the adaptive onboarding diagnostic.
/// </summary>
public sealed record DiagnosticResult(
    IImmutableSet<string> MasteredConcepts,
    IImmutableSet<string> GapConcepts,
    float Confidence,
    int QuestionsAsked);
