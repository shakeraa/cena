// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Confusion Resolution Tracker (FOC-005.2)
//
// Tracks confusion → resolution sequences per concept.
// Monitors a patience window after confusion is detected:
//   - If student self-resolves → positive learning event (deep learning)
//   - If student fails within window → trigger scaffolding
//
// The tracker adapts patience based on the student's historical
// resolution rate: good self-resolvers get longer windows.
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Tracks confusion episodes and resolution outcomes per concept.
/// </summary>
public interface IConfusionResolutionTracker
{
    /// <summary>Record that confusion was detected for a concept.</summary>
    void OnConfusionDetected(string conceptId, DateTimeOffset timestamp);

    /// <summary>Record a question attempt during an active confusion window.</summary>
    ConfusionWindowOutcome OnQuestionAttempted(string conceptId, bool correct, DateTimeOffset timestamp);

    /// <summary>Get the adaptive patience window size based on resolution history.</summary>
    int GetAdaptivePatienceWindow();

    /// <summary>Get the student's confusion resolution rate.</summary>
    double GetResolutionRate();

    /// <summary>Check if a concept is currently in a confusion monitoring window.</summary>
    bool IsInConfusionWindow(string conceptId);
}

public sealed class ConfusionResolutionTracker : IConfusionResolutionTracker
{
    private readonly Dictionary<string, ConfusionEpisode> _activeEpisodes = new();
    private readonly Queue<bool> _resolutionHistory = new(); // true = resolved, false = unresolved
    private const int ResolutionHistorySize = 10;
    private const int DefaultPatienceWindow = 5;

    public void OnConfusionDetected(string conceptId, DateTimeOffset timestamp)
    {
        // Start or reset the confusion window for this concept
        _activeEpisodes[conceptId] = new ConfusionEpisode(
            ConceptId: conceptId,
            DetectedAt: timestamp,
            QuestionsAttempted: 0,
            CorrectAnswers: 0,
            PatienceWindow: GetAdaptivePatienceWindow()
        );
    }

    public ConfusionWindowOutcome OnQuestionAttempted(string conceptId, bool correct, DateTimeOffset timestamp)
    {
        if (!_activeEpisodes.TryGetValue(conceptId, out var episode))
            return ConfusionWindowOutcome.NotInWindow;

        var updated = episode with
        {
            QuestionsAttempted = episode.QuestionsAttempted + 1,
            CorrectAnswers = episode.CorrectAnswers + (correct ? 1 : 0)
        };
        _activeEpisodes[conceptId] = updated;

        double accuracy = (double)updated.CorrectAnswers / updated.QuestionsAttempted;

        // ── Resolution: student got the concept right ──
        if (correct && accuracy >= 0.5)
        {
            RecordResolution(resolved: true);
            _activeEpisodes.Remove(conceptId);
            return ConfusionWindowOutcome.Resolved;
        }

        // ── Window expired without resolution ──
        if (updated.QuestionsAttempted >= updated.PatienceWindow)
        {
            RecordResolution(resolved: false);
            _activeEpisodes.Remove(conceptId);
            return ConfusionWindowOutcome.Unresolved;
        }

        // ── Still monitoring ──
        return ConfusionWindowOutcome.Monitoring;
    }

    public int GetAdaptivePatienceWindow()
    {
        double rate = GetResolutionRate();
        // High resolvers (>0.7) → extended window (7 questions)
        // Low resolvers (<0.3) → shorter window (3 questions)
        if (rate > 0.7) return 7;
        if (rate < 0.3) return 3;
        return DefaultPatienceWindow;
    }

    public double GetResolutionRate()
    {
        if (_resolutionHistory.Count == 0) return 0.5; // Default for new students
        int resolved = 0;
        foreach (bool r in _resolutionHistory)
            if (r) resolved++;
        return (double)resolved / _resolutionHistory.Count;
    }

    public bool IsInConfusionWindow(string conceptId) =>
        _activeEpisodes.ContainsKey(conceptId);

    private void RecordResolution(bool resolved)
    {
        _resolutionHistory.Enqueue(resolved);
        if (_resolutionHistory.Count > ResolutionHistorySize)
            _resolutionHistory.Dequeue();
    }
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public enum ConfusionWindowOutcome
{
    NotInWindow, // No active confusion window for this concept
    Monitoring,  // Within window, still observing
    Resolved,    // Student resolved the confusion (positive learning event)
    Unresolved   // Window expired, confusion not resolved (trigger scaffold)
}

public record ConfusionEpisode(
    string ConceptId,
    DateTimeOffset DetectedAt,
    int QuestionsAttempted,
    int CorrectAnswers,
    int PatienceWindow
);
