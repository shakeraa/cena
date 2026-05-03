// =============================================================================
// Cena Platform — Program Fork/Reference Events (TENANCY-P3e)
//
// Events for platform program fork workflow: an institute copies a platform
// program template, customizes it, and optionally receives version updates.
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when a platform program is forked by an institute.
/// </summary>
public record ProgramForked_V1(
    string NewProgramId,
    string SourceProgramId,
    string InstituteId,
    string ForkedByMentorId,
    string ContentPackVersion,
    DateTimeOffset ForkedAt
) : IDelegatedEvent;

/// <summary>
/// Emitted when a forked program receives an update from the platform source.
/// </summary>
public record ProgramVersionUpdated_V1(
    string ProgramId,
    string SourceProgramId,
    string OldVersion,
    string NewVersion,
    string[] ChangedContentIds,
    DateTimeOffset UpdatedAt
) : IDelegatedEvent;

/// <summary>
/// Emitted when an institute declines a platform version update.
/// </summary>
public record ProgramUpdateDeclined_V1(
    string ProgramId,
    string SourceProgramId,
    string DeclinedVersion,
    string Reason,
    string DeclinedByMentorId,
    DateTimeOffset DeclinedAt
) : IDelegatedEvent;
