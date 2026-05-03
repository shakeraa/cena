// =============================================================================
// Cena Platform — LearningSession read-model projection (Phase 1)
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock)
//
// Single-stream projection that consumes SessionStarted_V2 from the new
// LearningSession stream (`session-{SessionId}`) and produces a
// LearningSessionRecord read model. Phase 1 scope is one event type; Sprint 2+
// adds the remaining event handlers as they migrate in.
//
// Not yet registered in MartenConfiguration — registration lands when the
// shadow-write flag is ready for pilot rollout. See
// ADR-0012 "Phase 1: Shadow-Write" § 3: "new projections read from new
// streams; old projections unchanged".
// =============================================================================

using Cena.Actors.Sessions.Events;
using Marten.Events.Aggregation;

namespace Cena.Actors.Sessions.Projections;

/// <summary>
/// Read model for a single LearningSession. Identified by SessionId; one
/// document per stream. Populated by <see cref="LearningSessionProjection"/>.
/// </summary>
public class LearningSessionRecord
{
    /// <summary>Stream key identity — the SessionId of the learning session.</summary>
    public string Id { get; set; } = "";

    /// <summary>Owning student.</summary>
    public string StudentId { get; set; } = "";

    /// <summary>Wall-clock start time reported by the client.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>Starting pedagogical methodology (Socratic, Direct, etc.).</summary>
    public string Methodology { get; set; } = "";

    /// <summary>Client device category (web, mobile-pwa, etc.).</summary>
    public string DeviceType { get; set; } = "";

    /// <summary>Client app build identifier.</summary>
    public string AppVersion { get; set; } = "";

    /// <summary>Whether the session was started in offline mode.</summary>
    public bool IsOffline { get; set; }

    /// <summary>Optional A/B experiment cohort tag.</summary>
    public string? ExperimentCohort { get; set; }

    /// <summary>REV-014 tenant scope. Null pre-tenancy events.</summary>
    public string? SchoolId { get; set; }
}

/// <summary>
/// Marten single-stream projection that builds a
/// <see cref="LearningSessionRecord"/> from the new LearningSession event
/// stream. Phase 1 handles <see cref="SessionStarted_V2"/> only; additional
/// event types migrate in as Sprint 2+ work progresses.
/// </summary>
public class LearningSessionProjection : SingleStreamProjection<LearningSessionRecord, string>
{
    /// <summary>Creates the record on the stream-start event.</summary>
    public LearningSessionRecord Create(SessionStarted_V2 e) => new()
    {
        Id = e.SessionId,
        StudentId = e.StudentId,
        StartedAt = e.ClientTimestamp,
        Methodology = e.Methodology,
        DeviceType = e.DeviceType,
        AppVersion = e.AppVersion,
        IsOffline = e.IsOffline,
        ExperimentCohort = e.ExperimentCohort,
        SchoolId = e.SchoolId,
    };
}
