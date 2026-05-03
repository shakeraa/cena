// =============================================================================
// Cena Platform — Behavioral Anomaly Detection (SEC-ASSESS-003)
//
// INFORMATIONAL ONLY — no automated penalties. Flags suspicious patterns
// for teacher/admin review. Never shown to students.
//
// Flags:
// - ImpossibleResponseTime: < 3s for complex items
// - CopyPastePattern: paste events detected in free-response fields
// - DeviceSwitch: IP or device fingerprint changes mid-exam
// - AccuracyJump: sudden accuracy improvement (>3σ from rolling mean)
// =============================================================================

namespace Cena.Actors.Assessment;

/// <summary>
/// Type of behavioral anomaly detected during assessment.
/// </summary>
public enum AnomalyType
{
    /// <summary>Response submitted in less than 3 seconds for a complex item.</summary>
    ImpossibleResponseTime,

    /// <summary>Clipboard paste event detected in a free-response field.</summary>
    CopyPastePattern,

    /// <summary>IP address or device fingerprint changed during an exam session.</summary>
    DeviceSwitch,

    /// <summary>Sudden accuracy jump (>3σ from rolling 10-question mean).</summary>
    AccuracyJump,

    /// <summary>Multiple tab-switch events in rapid succession.</summary>
    RapidTabSwitching,

    /// <summary>Answer pattern matches another student's answers (post-hoc check).</summary>
    AnswerCorrelation
}

/// <summary>
/// Severity level for anomaly flags. All are informational — none trigger
/// automated penalties.
/// </summary>
public enum AnomalySeverity
{
    /// <summary>Low confidence — may be a false positive.</summary>
    Low,

    /// <summary>Medium confidence — worth reviewing.</summary>
    Medium,

    /// <summary>High confidence — multiple indicators converge.</summary>
    High
}

/// <summary>
/// A single anomaly flag instance with context for teacher review.
/// </summary>
public sealed record AnomalyFlag
{
    public string Id { get; init; } = "";
    public string StudentId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string? QuestionId { get; init; }

    public AnomalyType Type { get; init; }
    public AnomalySeverity Severity { get; init; }

    /// <summary>Human-readable explanation for the teacher dashboard.</summary>
    public string Description { get; init; } = "";

    /// <summary>Supporting data (e.g., response time in ms, paste length).</summary>
    public IReadOnlyDictionary<string, object> Evidence { get; init; } =
        new Dictionary<string, object>();

    public DateTimeOffset DetectedAt { get; init; }
}

/// <summary>
/// Thresholds for anomaly detection. Configurable per deployment.
/// </summary>
public sealed class AnomalyThresholds
{
    /// <summary>Minimum response time (seconds) below which an item is flagged.</summary>
    public double MinResponseTimeSeconds { get; set; } = 3.0;

    /// <summary>Number of standard deviations for accuracy jump detection.</summary>
    public double AccuracyJumpSigma { get; set; } = 3.0;

    /// <summary>Rolling window size for accuracy baseline.</summary>
    public int AccuracyWindowSize { get; set; } = 10;

    /// <summary>Minimum paste length (chars) to flag copy-paste.</summary>
    public int MinPasteLengthChars { get; set; } = 20;

    /// <summary>Tab-switch count within 60s to flag rapid switching.</summary>
    public int RapidTabSwitchThreshold { get; set; } = 5;
}
