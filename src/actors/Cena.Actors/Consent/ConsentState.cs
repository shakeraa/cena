// =============================================================================
// Cena Platform — ConsentAggregate state (prr-155, EPIC-PRR-A)
//
// In-memory fold of consent events into the current per-purpose grant map.
//
// Event application is intentionally minimal and stateless with respect to
// PII: the encrypted subject-id / actor-id wire strings flow *through* the
// state unchanged — the state does not attempt to decrypt them. That is the
// read-side facade's job (GdprConsentManager).
//
// Expiry handling: ExpiresAt on ConsentGranted_V1 is fold into the grant
// info; callers ask IsEffectivelyGranted(purpose, now) to get the effective
// answer including expiry enforcement. This keeps the fold pure (no
// reliance on wall-clock) and the read-check deterministic in tests.
// =============================================================================

using Cena.Actors.Consent.Events;

namespace Cena.Actors.Consent;

/// <summary>
/// Per-purpose grant info carried by <see cref="ConsentState"/>.
/// </summary>
/// <param name="IsGranted">True if the most recent event for this purpose was a grant.</param>
/// <param name="GrantedByRole">Role that performed the active grant.</param>
/// <param name="GrantedAt">When the active grant was recorded.</param>
/// <param name="ExpiresAt">Optional expiry; null = indefinite.</param>
/// <param name="RevokedAt">When the most-recent revoke was recorded, if any.</param>
/// <param name="RevokedByRole">Role that revoked, if any.</param>
/// <param name="RevocationReason">Reason string from the revoke event, if any.</param>
public sealed record ConsentGrantInfo(
    bool IsGranted,
    ActorRole? GrantedByRole,
    DateTimeOffset? GrantedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    ActorRole? RevokedByRole,
    string? RevocationReason);

/// <summary>
/// Folded state of a single <see cref="ConsentAggregate"/> instance. Keyed by
/// <see cref="ConsentPurpose"/>; empty map = no grants have ever been made.
/// </summary>
public sealed class ConsentState
{
    private readonly Dictionary<ConsentPurpose, ConsentGrantInfo> _grants = new();

    /// <summary>
    /// Last parent-review event applied to this aggregate, if any.
    /// Null if no parent review has happened.
    /// </summary>
    public ConsentReviewedByParent_V1? LastParentReview { get; private set; }

    /// <summary>
    /// Per-purpose student-visibility veto state (prr-052). Key = purpose
    /// the student (Teen16to17+) has opted the parent out of. Value = the
    /// most-recent Vetoed event for replay/audit. The absence of a key
    /// means the parent may see the purpose subject to the usual
    /// grant rules.
    /// </summary>
    private readonly Dictionary<ConsentPurpose, StudentVisibilityVetoed_V1> _vetoes = new();

    /// <summary>
    /// Read-only set of purposes currently vetoed from parent visibility
    /// by the student. Feeds <see cref="AgeBandPolicy.EvaluateDashboard"/>.
    /// </summary>
    public IReadOnlySet<ConsentPurpose> VetoedParentVisibilityPurposes
        => _vetoes.Keys.ToHashSet();

    /// <summary>
    /// Read-only map of the most-recent veto event per purpose (for audit
    /// rendering). A purpose absent from this map is not currently vetoed.
    /// </summary>
    public IReadOnlyDictionary<ConsentPurpose, StudentVisibilityVetoed_V1> VetoHistory
        => _vetoes;

    /// <summary>
    /// Read-only view of the per-purpose grant state. Use
    /// <see cref="IsEffectivelyGranted"/> for the expiry-aware check.
    /// </summary>
    public IReadOnlyDictionary<ConsentPurpose, ConsentGrantInfo> Grants => _grants;

    /// <summary>
    /// True when the purpose was most recently granted and the grant has not
    /// passed its <c>ExpiresAt</c> as of <paramref name="asOf"/>.
    /// </summary>
    public bool IsEffectivelyGranted(ConsentPurpose purpose, DateTimeOffset asOf)
    {
        if (!_grants.TryGetValue(purpose, out var info) || !info.IsGranted)
        {
            return false;
        }
        if (info.ExpiresAt.HasValue && info.ExpiresAt.Value <= asOf)
        {
            return false;
        }
        return true;
    }

    /// <summary>Apply a <see cref="ConsentGranted_V1"/> event.</summary>
    public void Apply(ConsentGranted_V1 e)
    {
        _grants[e.Purpose] = new ConsentGrantInfo(
            IsGranted: true,
            GrantedByRole: e.GrantedByRole,
            GrantedAt: e.GrantedAt,
            ExpiresAt: e.ExpiresAt,
            RevokedAt: null,
            RevokedByRole: null,
            RevocationReason: null);
    }

    /// <summary>Apply a <see cref="ConsentRevoked_V1"/> event.</summary>
    public void Apply(ConsentRevoked_V1 e)
    {
        if (_grants.TryGetValue(e.Purpose, out var prior))
        {
            _grants[e.Purpose] = prior with
            {
                IsGranted = false,
                RevokedAt = e.RevokedAt,
                RevokedByRole = e.RevokedByRole,
                RevocationReason = e.Reason,
            };
        }
        else
        {
            // Revoke-before-grant: record a negative-only entry so the facade
            // can surface the revoke in audit trails even if no prior grant
            // was seen by this projection replay.
            _grants[e.Purpose] = new ConsentGrantInfo(
                IsGranted: false,
                GrantedByRole: null,
                GrantedAt: null,
                ExpiresAt: null,
                RevokedAt: e.RevokedAt,
                RevokedByRole: e.RevokedByRole,
                RevocationReason: e.Reason);
        }
    }

    /// <summary>Apply a <see cref="ConsentPurposeAdded_V1"/> event.</summary>
    public void Apply(ConsentPurposeAdded_V1 e)
    {
        // Purpose-added is a bookkeeping signal that a new purpose entered
        // the catalog for this subject; the actual grant state is carried
        // by the paired ConsentGranted_V1 event. If the subject has no
        // prior entry for the new purpose, seed a "not yet granted" slot.
        if (!_grants.ContainsKey(e.NewPurpose))
        {
            _grants[e.NewPurpose] = new ConsentGrantInfo(
                IsGranted: false,
                GrantedByRole: null,
                GrantedAt: null,
                ExpiresAt: null,
                RevokedAt: null,
                RevokedByRole: null,
                RevocationReason: null);
        }
    }

    /// <summary>Apply a <see cref="ConsentReviewedByParent_V1"/> event.</summary>
    public void Apply(ConsentReviewedByParent_V1 e)
    {
        LastParentReview = e;
        // Deferred reviews do not change state.
        if (e.Outcome == ConsentReviewOutcome.Deferred) return;

        // Approved/Rejected/Partial: the review outcome does not by itself
        // produce grants or revokes — the aggregate command handler emits
        // paired ConsentGranted_V1 / ConsentRevoked_V1 events alongside
        // this review event when appropriate. The review event's role in
        // the fold is to surface "a parent reviewed these purposes at T".
        _ = e;
    }

    /// <summary>Apply a <see cref="StudentVisibilityVetoed_V1"/> event (prr-052).</summary>
    public void Apply(StudentVisibilityVetoed_V1 e)
    {
        _vetoes[e.Purpose] = e;
    }

    /// <summary>Apply a <see cref="StudentVisibilityRestored_V1"/> event (prr-052).</summary>
    public void Apply(StudentVisibilityRestored_V1 e)
    {
        _vetoes.Remove(e.Purpose);
    }
}
