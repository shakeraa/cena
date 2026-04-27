// =============================================================================
// Cena Platform — ParentBindInviteDocument (TASK-E2E-A-04-BE)
//
// Marten doc that backs single-use parent-bind invite tokens. Id = jti
// (the JWT's unique id). The token itself is signed with HS256 and lives
// only on the wire / in the parent's email; the server keeps the jti +
// associated subject ids + consumed flag so a replay (same jti, second
// consume call) can be detected and rejected with 409.
// =============================================================================

namespace Cena.Actors.Parent;

/// <summary>
/// Persisted invite metadata. The signed JWT body carries the same fields
/// (so the consume endpoint can rebuild the binding without a DB hit
/// before signature/exp validation), but the doc is the authoritative
/// source for the consumed flag — a fresh-looking JWT whose jti is
/// already marked consumed is rejected.
/// </summary>
public sealed record ParentBindInviteDocument
{
    /// <summary>
    /// JWT jti claim. Stable across the token's lifetime, unique per invite.
    /// Used as the Marten Id so existence + consume-flag are a single
    /// document lookup.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>
    /// Anon student id this invite is for. Pinned at issuance time.
    /// </summary>
    public string StudentSubjectId { get; init; } = "";

    /// <summary>
    /// ADR-0001 institute id. The consumer's own tenant must match — the
    /// endpoint refuses cross-institute invites with 403.
    /// </summary>
    public string InstituteId { get; init; } = "";

    /// <summary>
    /// Parent email this invite was sent to. The consume endpoint compares
    /// against the caller's Firebase email and rejects mismatches with 403.
    /// Empty when the test path skips the email check (still rare).
    /// </summary>
    public string ParentEmail { get; init; } = "";

    /// <summary>
    /// Free-form relationship label ("parent", "guardian", "stepparent", …).
    /// Stored verbatim and surfaced to downstream consumers via
    /// <c>ParentChildBoundV1.Relationship</c>.
    /// </summary>
    public string Relationship { get; init; } = "parent";

    /// <summary>When the invite was issued.</summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// When the invite expires. The HS256 JWT's <c>exp</c> claim mirrors
    /// this so a clock-skewed validator and a clock-skewed consumer can
    /// agree on rejection without trusting Marten's clock.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Non-null when the invite has been consumed. The consume endpoint
    /// sets this once and returns 409 on every subsequent call.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; init; }

    /// <summary>
    /// Firebase uid of the parent who consumed the invite. Null until
    /// consumed. Recorded so an audit query can tell who actually accepted
    /// the invite (which may differ from <see cref="ParentEmail"/> if the
    /// parent registered under a different email after the invite shipped).
    /// </summary>
    public string? ConsumedByParentUid { get; init; }
}
