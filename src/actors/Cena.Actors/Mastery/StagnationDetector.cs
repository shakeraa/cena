// =============================================================================
// Cena Platform -- Mastery Stagnation Detection
// MST-006: Detects repeated error patterns in recent errors
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Detects stagnation from repeated error patterns in ConceptMasteryState.RecentErrors.
/// Returns the dominant error type if 3+ of the same type appear.
/// </summary>
public static class MasteryStagnationDetector
{
    private const int StagnationThreshold = 3;

    /// <summary>
    /// Check if there is a dominant repeated error type in recent errors.
    /// Returns the error type if 3+ of the same type, null otherwise.
    /// </summary>
    public static ErrorType? DetectDominantError(ConceptMasteryState state)
    {
        if (state.RecentErrors.Length < StagnationThreshold)
            return null;

        // Count occurrences of each error type
        Span<int> counts = stackalloc int[5]; // 5 ErrorType values
        for (int i = 0; i < state.RecentErrors.Length; i++)
        {
            int idx = (int)state.RecentErrors[i];
            if (idx >= 0 && idx < counts.Length)
                counts[idx]++;
        }

        // Find the first type with >= threshold occurrences
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] >= StagnationThreshold)
                return (ErrorType)i;
        }

        return null;
    }
}
