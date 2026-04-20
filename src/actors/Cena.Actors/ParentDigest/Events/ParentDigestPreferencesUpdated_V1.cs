// =============================================================================
// Cena Platform — ParentDigestPreferencesUpdated_V1 (prr-051 / EPIC-PRR-C).
//
// Appended when a parent writes to POST /api/v1/parent/digest/preferences.
// One event per (parent, child, institute) write; the event carries every
// purpose's effective status at the moment of the write so a projection can
// be rebuilt from the event stream without replaying prior events.
//
// ADR-0003 constraints honoured:
//   - No misconception data, stuck-type codes, or transcript snippets.
//   - No free-text fields. The reason a preference changed is implicit in
//     the event type (`Updated` vs. `Unsubscribed`), never prose.
//   - PII: ParentActorId, StudentSubjectId are opaque anon ids; InstituteId
//     is a tenant id, not a school name.
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.ParentDigest.Events;

/// <summary>
/// Parent wrote a preferences update for a specific (parent, child) pair.
/// One snapshot per write — a projection can collapse the stream to the
/// latest row per pair by <see cref="UpdatedAtUtc"/>.
/// </summary>
/// <param name="ParentActorId">Opaque parent anon id.</param>
/// <param name="StudentSubjectId">Opaque student anon id.</param>
/// <param name="InstituteId">Tenant id (ADR-0001).</param>
/// <param name="PurposeStatuses">
/// Every purpose's explicit status at the moment of the write. Purposes
/// with <see cref="OptInStatus.NotSet"/> are omitted (the default-table
/// applies); the event therefore carries the MINIMAL information a
/// projection needs to recover the parent's intent.
/// </param>
/// <param name="UpdatedAtUtc">Wall clock of the write.</param>
public sealed record ParentDigestPreferencesUpdated_V1(
    string ParentActorId,
    string StudentSubjectId,
    string InstituteId,
    ImmutableDictionary<DigestPurpose, OptInStatus> PurposeStatuses,
    DateTimeOffset UpdatedAtUtc);
