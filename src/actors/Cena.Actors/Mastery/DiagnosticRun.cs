// =============================================================================
// Cena Platform — Diagnostic Run (RDY-073 Phase 1A)
//
// Domain scaffold for the 40-minute compression-diagnostic session
// (F7 per panel-review synthesis). A DiagnosticRun is an aggregate
// whose event stream captures:
//
//   - DiagnosticRunStarted         (student enrolment + cohort + deadline)
//   - DiagnosticItemAnswered       (per-item attempt with outcome + time)
//   - DiagnosticAbilityEstimated   (θ + SE per topic at checkpoint)
//   - DiagnosticRunCompleted       (final per-topic θ snapshot)
//   - DiagnosticRunAborted         (student quit / timeout / reset)
//
// The item-selection policy (maximum-information over the topic tree
// with Sympson-Hetter exposure control) is a separate strategy type
// that reads this aggregate's state and proposes the next item. That
// strategy ships in phase 1B; this file scaffolds the aggregate + its
// events so the event store schema lands without waiting for the
// selection policy implementation.
//
// Privacy: diagnostic-run data is session-scoped per ADR-0003. The
// aggregate carries a StudentAnonId (HMAC'd), never a plaintext
// student identifier, so the event stream is shareable with the
// adaptive-scheduler without widening the privacy surface.
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>Lifecycle state of a diagnostic run.</summary>
public enum DiagnosticRunStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Aborted = 3
}

/// <summary>
/// Cold-start prior source. When a student has no prior Cena history,
/// the diagnostic seeds its θ-per-topic prior from one of:
///   - SyllabusWeighted: use the syllabus-weighted average of the
///     track's Bagrut topic weights (default; neutral prior)
///   - DeclaredTrack: honour the student's declared 3u/4u/5u track
///     (shift the prior down for 3u, up for 5u)
///   - SchoolTranscript: use prior math grades if the school has
///     shared them via InstructorLed consent
/// </summary>
public enum ColdStartPriorSource
{
    SyllabusWeighted = 0,
    DeclaredTrack = 1,
    SchoolTranscript = 2
}

/// <summary>One item attempt inside a diagnostic run.</summary>
public sealed record DiagnosticItemAttempt(
    string ItemId,
    string TopicSlug,
    bool IsCorrect,
    TimeSpan TimeSpent,
    double ItemDifficulty,
    double ItemDiscrimination,
    DateTimeOffset AttemptedAtUtc);

/// <summary>
/// One checkpoint ability estimate during the run. Emitted every N
/// items so the adaptive-scheduler can start pre-fetching content for
/// the next-session plan while the diagnostic is still in progress.
/// </summary>
public sealed record DiagnosticCheckpoint(
    string TopicSlug,
    double Theta,
    double StandardError,
    int SampleSize,
    DateTimeOffset CheckpointAtUtc);

/// <summary>
/// Aggregate root for a single diagnostic run. Intentionally minimal
/// in phase 1A — exposes the state transitions the event stream
/// projections + item-selection strategy will read.
/// </summary>
public sealed class DiagnosticRun
{
    private readonly List<DiagnosticItemAttempt> _attempts = new();
    private readonly Dictionary<string, DiagnosticCheckpoint> _checkpoints = new();

    public string RunId { get; }
    public string StudentAnonId { get; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? AbortedAtUtc { get; private set; }
    public string? AbortReason { get; private set; }
    public DiagnosticRunStatus Status { get; private set; }
    public ColdStartPriorSource PriorSource { get; }
    public DateTimeOffset? StudentDeadlineUtc { get; }

    public IReadOnlyList<DiagnosticItemAttempt> Attempts => _attempts;
    public IReadOnlyDictionary<string, DiagnosticCheckpoint> Checkpoints => _checkpoints;

    /// <summary>Create a new diagnostic run. Emits DiagnosticRunStarted.</summary>
    public DiagnosticRun(
        string runId,
        string studentAnonId,
        ColdStartPriorSource priorSource,
        DateTimeOffset? studentDeadlineUtc,
        DateTimeOffset startedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new ArgumentException("RunId is required", nameof(runId));
        if (string.IsNullOrWhiteSpace(studentAnonId))
            throw new ArgumentException("StudentAnonId is required", nameof(studentAnonId));

        RunId = runId;
        StudentAnonId = studentAnonId;
        PriorSource = priorSource;
        StudentDeadlineUtc = studentDeadlineUtc;
        StartedAtUtc = startedAtUtc;
        Status = DiagnosticRunStatus.InProgress;
    }

    public void RecordAttempt(DiagnosticItemAttempt attempt)
    {
        if (Status != DiagnosticRunStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot record attempt on diagnostic run in status {Status}");
        ArgumentNullException.ThrowIfNull(attempt);
        _attempts.Add(attempt);
    }

    public void RecordCheckpoint(DiagnosticCheckpoint checkpoint)
    {
        if (Status != DiagnosticRunStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot record checkpoint on diagnostic run in status {Status}");
        ArgumentNullException.ThrowIfNull(checkpoint);
        _checkpoints[checkpoint.TopicSlug] = checkpoint;
    }

    public void Complete(DateTimeOffset completedAtUtc)
    {
        if (Status != DiagnosticRunStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot complete diagnostic run in status {Status}");
        Status = DiagnosticRunStatus.Completed;
        CompletedAtUtc = completedAtUtc;
    }

    public void Abort(string reason, DateTimeOffset abortedAtUtc)
    {
        if (Status != DiagnosticRunStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot abort diagnostic run in status {Status}");
        Status = DiagnosticRunStatus.Aborted;
        AbortReason = reason;
        AbortedAtUtc = abortedAtUtc;
    }

    /// <summary>
    /// True when the run has enough evidence per topic to hand off to
    /// the adaptive scheduler. Per Dr. Yael's spec: ≥ 70% of topics
    /// should have SE(θ) ≤ 0.3 at the 40-minute mark. Below that
    /// threshold the scheduler gets a "provisional plan" not a "final
    /// plan" and continues sampling.
    /// </summary>
    public bool HasSufficientEvidence(int minimumTopicsCovered = 7, double targetSe = 0.3)
    {
        if (_checkpoints.Count < minimumTopicsCovered) return false;
        var well = _checkpoints.Values.Count(c => c.StandardError <= targetSe);
        return (double)well / _checkpoints.Count >= 0.7;
    }
}
