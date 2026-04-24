// =============================================================================
// Cena Platform -- Scaffolding Level
// MST-011: Controls how much instructional support the LLM provides
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Scaffolding level for LLM prompt selection.
/// Full = worked example, Partial = faded example, HintsOnly = on request, None = independent.
/// </summary>
public enum ScaffoldingLevel
{
    Full,
    Partial,
    HintsOnly,
    None
}

/// <summary>
/// Metadata driving LLM prompt construction for a scaffolding level.
/// </summary>
public sealed record ScaffoldingMetadata(
    ScaffoldingLevel Level,
    string PromptVariant,
    bool ShowWorkedExample,
    bool ShowHintButton,
    int MaxHints,
    bool RevealAnswer);
