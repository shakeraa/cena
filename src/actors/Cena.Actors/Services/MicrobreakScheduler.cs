// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Proactive Microbreak Scheduler (FOC-003)
//
// The single highest-impact change from the 20-iteration autoresearch.
// Frontiers in Psychology (2025): 90-second micro-breaks every 10 minutes
// produced Cohen's d = 1.784 improvement in sustained attention (citation_id=frontiers-2025-microbreaks — see contracts/citations/approved-citations.yml).
//
// Two-tier break system:
//   Tier 1 (Proactive): Microbreaks BEFORE focus drops — 60-90s, scheduled
//   Tier 2 (Reactive):  Recovery breaks AFTER focus drops — 5-30 min, existing
//
// References:
// - Frontiers in Psychology (2025): "Sustaining student concentration" — d = 1.784 (citation_id=frontiers-2025-microbreaks)
// - Kitayama et al. (2022): systematic microbreaks → positive performance
// - Biwer et al. (2023): systematic breaks → efficiency + mood restoration
// - Attention Restoration Theory: nature exposure restores attentional resources
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Services;

/// <summary>
/// Determines when to trigger proactive microbreaks BEFORE focus degrades.
/// </summary>
public interface IMicrobreakScheduler
{
    /// <summary>
    /// Check if a microbreak should be triggered right now.
    /// Called after each question submission (never mid-problem).
    /// </summary>
    MicrobreakDecision ShouldTrigger(MicrobreakContext context);

    /// <summary>Record that a microbreak was taken (resets counters).</summary>
    void RecordMicrobreakTaken(DateTimeOffset timestamp);

    /// <summary>Record that a microbreak was skipped by the student.</summary>
    void RecordMicrobreakSkipped(DateTimeOffset timestamp);

    /// <summary>Record that a reactive recovery break was taken (resets cooldown).</summary>
    void RecordRecoveryBreakTaken(DateTimeOffset timestamp);

    /// <summary>Session-level analytics snapshot.</summary>
    MicrobreakSessionStats GetSessionStats();
}

public sealed class MicrobreakScheduler : IMicrobreakScheduler
{
    private readonly MicrobreakConfig _config;
    private int _questionsSinceLastBreak;
    private DateTimeOffset _lastBreakTime;
    private DateTimeOffset _sessionStartTime;
    private int _totalMicrobreaksTaken;
    private int _totalMicrobreaksSkipped;
    private int _consecutiveSkips;
    private int _lastActivityIndex = -1;

    public MicrobreakScheduler(MicrobreakConfig config, DateTimeOffset? sessionStart = null)
    {
        _config = config;
        _sessionStartTime = sessionStart ?? DateTimeOffset.UtcNow;
        _lastBreakTime = _sessionStartTime;
    }

    public MicrobreakDecision ShouldTrigger(MicrobreakContext context)
    {
        // ── Guard: student opted out (skipped 3+ consecutive microbreaks) ──
        if (_consecutiveSkips >= _config.MaxConsecutiveSkipsBeforeDisable)
        {
            return MicrobreakDecision.NoBreak("Student opted out after consecutive skips");
        }

        // ── Guard: flow state — don't interrupt deep focus ──
        if (context.CurrentFocusLevel == FocusLevel.Flow && context.FocusScore >= 0.85)
        {
            return MicrobreakDecision.NoBreak("Student in flow state");
        }

        // ── Guard: cooldown — don't trigger right after a reactive break ──
        double minutesSinceLastBreak = (context.CurrentTime - _lastBreakTime).TotalMinutes;
        if (minutesSinceLastBreak < _config.CooldownMinutesAfterReactiveBreak)
        {
            return MicrobreakDecision.NoBreak("Cooldown period active");
        }

        // ── Trigger: question count threshold ──
        bool questionThreshold = _questionsSinceLastBreak >= _config.QuestionsPerMicrobreak;

        // ── Trigger: time threshold ──
        bool timeThreshold = minutesSinceLastBreak >= _config.MinutesBetweenMicrobreaks;

        if (questionThreshold || timeThreshold)
        {
            var activity = SelectActivity();
            int durationSeconds = SelectDuration(context);
            string message = GetLocalizedMessage(activity);

            return new MicrobreakDecision(
                ShouldBreak: true,
                DurationSeconds: durationSeconds,
                Activity: activity,
                Message: message,
                Reason: questionThreshold
                    ? $"Reached {_config.QuestionsPerMicrobreak} questions"
                    : $"Reached {_config.MinutesBetweenMicrobreaks} minutes",
                SessionMicrobreakNumber: _totalMicrobreaksTaken + 1
            );
        }

        return MicrobreakDecision.NoBreak("No threshold reached");
    }

    public void RecordMicrobreakTaken(DateTimeOffset timestamp)
    {
        _lastBreakTime = timestamp;
        _questionsSinceLastBreak = 0;
        _totalMicrobreaksTaken++;
        _consecutiveSkips = 0;
    }

    public void RecordMicrobreakSkipped(DateTimeOffset timestamp)
    {
        _lastBreakTime = timestamp;
        _questionsSinceLastBreak = 0;
        _totalMicrobreaksSkipped++;
        _consecutiveSkips++;
    }

    public void RecordRecoveryBreakTaken(DateTimeOffset timestamp)
    {
        _lastBreakTime = timestamp;
        _questionsSinceLastBreak = 0;
    }

    public MicrobreakSessionStats GetSessionStats() => new(
        MicrobreaksTaken: _totalMicrobreaksTaken,
        MicrobreaksSkipped: _totalMicrobreaksSkipped,
        ConsecutiveSkips: _consecutiveSkips,
        QuestionsSinceLastBreak: _questionsSinceLastBreak
    );

    /// <summary>
    /// Called by the session actor after each question to advance the question counter.
    /// </summary>
    public void OnQuestionAnswered()
    {
        _questionsSinceLastBreak++;
    }

    // ── Activity selection: round-robin with no-repeat constraint ──
    private MicrobreakActivity SelectActivity()
    {
        var activities = Enum.GetValues<MicrobreakActivity>();
        int nextIndex;
        do
        {
            nextIndex = (_lastActivityIndex + 1) % activities.Length;
            // Skip if same as last (only matters on first wrap)
        } while (nextIndex == _lastActivityIndex && activities.Length > 1);

        _lastActivityIndex = nextIndex;
        return activities[nextIndex];
    }

    // ── Duration: 60s default, 90s if focus is already drifting ──
    private int SelectDuration(MicrobreakContext context)
    {
        return context.CurrentFocusLevel switch
        {
            FocusLevel.Drifting => 90,
            FocusLevel.Fatigued => 90,
            _ => 60
        };
    }

    private static string GetLocalizedMessage(MicrobreakActivity activity)
    {
        return activity switch
        {
            MicrobreakActivity.StretchBreak => FocusMessages.MicrobreakStretch(),
            MicrobreakActivity.BreathingExercise => FocusMessages.MicrobreakBreathing(),
            MicrobreakActivity.LookAway => FocusMessages.MicrobreakLookAway(),
            MicrobreakActivity.WaterBreak => FocusMessages.MicrobreakWater(),
            MicrobreakActivity.MiniWalk => FocusMessages.MicrobreakWalk(),
            _ => FocusMessages.MicrobreakGeneric()
        };
    }
}

// ═══════════════════════════════════════════════════════════════
// TYPES
// ═══════════════════════════════════════════════════════════════

public record MicrobreakConfig(
    int QuestionsPerMicrobreak = 8,           // Trigger every N questions
    double MinutesBetweenMicrobreaks = 10.0,  // OR every M minutes
    double CooldownMinutesAfterReactiveBreak = 5.0, // Don't trigger right after reactive break
    int MaxConsecutiveSkipsBeforeDisable = 3   // Student opts out after 3 skips
)
{
    public static readonly MicrobreakConfig Default = new();
}

public record MicrobreakContext(
    int QuestionsAnswered,
    double ElapsedMinutes,
    FocusLevel CurrentFocusLevel,
    double FocusScore,
    DateTimeOffset CurrentTime
);

public record MicrobreakDecision(
    bool ShouldBreak,
    int DurationSeconds,
    MicrobreakActivity Activity,
    string Message,
    string Reason,
    int SessionMicrobreakNumber
)
{
    public static MicrobreakDecision NoBreak(string reason) => new(
        ShouldBreak: false,
        DurationSeconds: 0,
        Activity: default,
        Message: string.Empty,
        Reason: reason,
        SessionMicrobreakNumber: 0
    );
}

public enum MicrobreakActivity
{
    StretchBreak,       // Stand up and stretch (60s)
    BreathingExercise,  // 5 deep breaths
    LookAway,           // 20-20-20 rule — look far away (30s)
    WaterBreak,         // Grab water
    MiniWalk            // Walk to kitchen and back
}

public record MicrobreakSessionStats(
    int MicrobreaksTaken,
    int MicrobreaksSkipped,
    int ConsecutiveSkips,
    int QuestionsSinceLastBreak
);
