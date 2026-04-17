// =============================================================================
// Cena Platform — Pilot Data Export Records (RDY-032)
//
// Analysis-ready schema for the pilot data export. Never shipped over the
// wire — these records only live inside the exporter process, flushed to
// CSV for downstream calibration (BKT, DIF, Bagrut baseline).
//
// Privacy invariants encoded in the types:
//   • No raw student identifiers. Only pseudonymized hashes (SHA-256 /
//     16-hex-truncated) via IStudentIdHasher.
//   • No names, no emails, no IP addresses, no device identifiers.
//   • Misconception events are filtered upstream (ADR-0003 / MlExcluded);
//     schema intentionally has no misconception column so a forgotten
//     filter step doesn't silently leak the data.
// =============================================================================

namespace Cena.Actors.Pilot;

/// <summary>
/// One row per student attempt across every concept. Primary key is the
/// (student_id_hash, session_id, timestamp) triple — timestamp is UTC ISO.
/// </summary>
public sealed record PilotAttemptRow(
    string StudentIdHash,
    string ConceptId,
    string Subject,
    string QuestionId,
    bool Correct,
    int HintsUsed,
    int ResponseTimeMs,
    string SessionId,
    int SessionNumber,             // 1-based position in the student's session timeline
    double PriorMastery,
    double PosteriorMastery,
    DateTimeOffset Timestamp);

/// <summary>
/// One row per learning session. StartedAt is populated from
/// LearningSessionStarted_V1; the end fields come from LearningSessionEnded_V1.
/// Sessions without a recorded End (abandoned / crashed) get a null EndedAt
/// but are still emitted so calibration downstream can decide whether to
/// keep or drop them.
/// </summary>
public sealed record PilotSessionRow(
    string StudentIdHash,
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    long DurationMs,               // 0 when EndedAt is null
    int QuestionsAttempted,
    int QuestionsCorrect,
    double Accuracy);

/// <summary>
/// Metadata sidecar written alongside the CSVs — date range, counts,
/// quality-check results. Drops into a json file named per the
/// config template so analysts can sanity-check a run without opening
/// the CSVs.
/// </summary>
public sealed record PilotExportMetadata(
    string RunId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    DateTimeOffset ExportedAt,
    int StudentCount,
    int AttemptRowCount,
    int SessionRowCount,
    IReadOnlyList<string> QualityCheckErrors,
    bool DryRun);

/// <summary>
/// Caller-supplied request. <see cref="OutputDirectory"/> overrides the
/// config-level path; leave null to use the configured default. DryRun
/// runs every query and every validation but does NOT write CSV files —
/// the metadata record is still produced so ops can preview the row
/// counts before committing to a full export.
/// </summary>
public sealed record PilotExportRequest(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? OutputDirectory = null,
    bool DryRun = true);

/// <summary>
/// Full result of an export run. File paths are populated only on wet
/// runs. <see cref="QualityCheckErrors"/> is non-empty when referential
/// integrity is broken — the admin endpoint surfaces that as a 422.
/// </summary>
public sealed record PilotExportResult(
    string RunId,
    bool DryRun,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int StudentCount,
    int AttemptRowCount,
    int SessionRowCount,
    string? AttemptsFilePath,
    string? SessionsFilePath,
    string? MetadataFilePath,
    IReadOnlyList<string> QualityCheckErrors);
