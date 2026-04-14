// =============================================================================
// Cena Platform -- Scaffolding Service
// MST-011: Maps effective mastery + PSI to scaffolding level
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Interface for scaffolding service operations.
/// Allows for testable, injectable access to scaffolding logic.
/// </summary>
public interface IScaffoldingService
{
    /// <summary>
    /// Determine scaffolding level from effective mastery and PSI.
    /// </summary>
    ScaffoldingLevel DetermineLevel(float effectiveMastery, float psi);

    /// <summary>
    /// Get metadata for LLM prompt construction from scaffolding level.
    /// </summary>
    ScaffoldingMetadata GetScaffoldingMetadata(ScaffoldingLevel level);
}

/// <summary>
/// Wrapper for the static ScaffoldingService to enable DI injection.
/// Stateless, thread-safe — registered as Singleton.
/// </summary>
public sealed class ScaffoldingServiceWrapper : IScaffoldingService
{
    public ScaffoldingLevel DetermineLevel(float effectiveMastery, float psi)
        => ScaffoldingService.DetermineLevel(effectiveMastery, psi);

    public ScaffoldingMetadata GetScaffoldingMetadata(ScaffoldingLevel level)
        => ScaffoldingService.GetScaffoldingMetadata(level);
}

/// <summary>
/// Determines how much instructional support the LLM provides
/// based on the student's effective mastery and prerequisite satisfaction.
/// Pure stateless mapping function.
/// </summary>
public static class ScaffoldingService
{
    /// <summary>
    /// Determine scaffolding level from effective mastery and PSI.
    /// </summary>
    public static ScaffoldingLevel DetermineLevel(float effectiveMastery, float psi)
    {
        if (effectiveMastery >= 0.70f)
            return ScaffoldingLevel.None;

        if (effectiveMastery < 0.20f && psi < 0.80f)
            return ScaffoldingLevel.Full;

        if (effectiveMastery < 0.40f)
            return ScaffoldingLevel.Partial;

        return ScaffoldingLevel.HintsOnly;
    }

    /// <summary>
    /// Get metadata for LLM prompt construction from scaffolding level.
    /// </summary>
    public static ScaffoldingMetadata GetScaffoldingMetadata(ScaffoldingLevel level) => level switch
    {
        ScaffoldingLevel.Full => new(level, "worked-example", ShowWorkedExample: true,
            ShowHintButton: true, MaxHints: 3, RevealAnswer: true),

        // RDY-013: Faded worked examples ARE the Partial-level technique
        // per Renkl & Atkinson (2003). ShowWorkedExample must be true so the
        // frontend receives the workedExample payload and renders it in faded mode.
        ScaffoldingLevel.Partial => new(level, "faded-example", ShowWorkedExample: true,
            ShowHintButton: true, MaxHints: 2, RevealAnswer: true),

        ScaffoldingLevel.HintsOnly => new(level, "hints-only", ShowWorkedExample: false,
            ShowHintButton: true, MaxHints: 1, RevealAnswer: false),

        ScaffoldingLevel.None => new(level, "independent", ShowWorkedExample: false,
            ShowHintButton: false, MaxHints: 0, RevealAnswer: false),

        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}
