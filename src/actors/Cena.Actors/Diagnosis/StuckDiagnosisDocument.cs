// =============================================================================
// Cena Platform — StuckDiagnosisDocument (RDY-063 Phase 1)
//
// Marten document for persisting classifier outputs. 30-day TTL per
// ADR-0003 (session-scoped misconception data + derivative labels).
//
// The document intentionally carries NO raw PII — not studentId, not
// the question text, not the student's attempt. Only:
//   - session-scoped anon id (HMAC)
//   - question id (non-PII, public)
//   - chapter id (non-PII)
//   - label + confidence + strategy
//   - classifier version for replay
//
// This lets us build teacher-PD dashboards ("top items by
// encoding-stuck rate") and item-curriculum feedback signals
// without ever joining the table to a student profile.
// =============================================================================

namespace Cena.Actors.Diagnosis;

public sealed class StuckDiagnosisDocument
{
    public string Id { get; set; } = string.Empty;              // guid
    public string SessionId { get; set; } = string.Empty;
    public string StudentAnonId { get; set; } = string.Empty;   // HMAC(studentId, sessionId, salt)
    public string QuestionId { get; set; } = string.Empty;
    public string? ChapterId { get; set; }

    public StuckType Primary { get; set; } = StuckType.Unknown;
    public float PrimaryConfidence { get; set; }
    public StuckType Secondary { get; set; } = StuckType.Unknown;
    public float SecondaryConfidence { get; set; }
    public StuckScaffoldStrategy SuggestedStrategy { get; set; } = StuckScaffoldStrategy.Unspecified;

    public bool ShouldInvolveTeacher { get; set; }
    public StuckDiagnosisSource Source { get; set; } = StuckDiagnosisSource.None;
    public string ClassifierVersion { get; set; } = string.Empty;
    public string? SourceReasonCode { get; set; }
    public int LatencyMs { get; set; }

    public DateTimeOffset DiagnosedAt { get; set; }

    /// <summary>UTC midnight of the day DiagnosedAt falls in — used for
    /// fast "today's encoding-stuck rate" admin queries.</summary>
    public DateTimeOffset DayBucket { get; set; }

    /// <summary>ExpiresAt = DiagnosedAt + RetentionDays. Background reaper
    /// (or Marten expiry if configured) sweeps rows past this date.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
