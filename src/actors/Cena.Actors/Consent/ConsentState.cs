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
/// ADR-0059 §15.3 — Bagrut reference-library consent fact (single
/// global toggle per student). Populated by
/// <c>BagrutReferenceConsentGranted_V1</c>; <see cref="RevokedAt"/>
/// non-null after <c>BagrutReferenceConsentRevoked_V1</c>.
/// </summary>
public sealed record BagrutReferenceConsentInfo(
    DateTimeOffset GrantedAt,
    string DisclosureVersion,
    DateTimeOffset? RevokedAt = null,
    string? RevocationReason = null)
{
    /// <summary>True iff active (granted, not revoked, within 90d).</summary>
    public bool IsActive(DateTimeOffset asOf, TimeSpan functionalTtl) =>
        RevokedAt is null && (asOf - GrantedAt) <= functionalTtl;
}

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
    /// ADR-0059 §15.3 — Bagrut reference-library consent fact. Distinct
    /// from per-purpose <see cref="_grants"/> because reference-library
    /// consent has different semantics (one global toggle, not a
    /// per-data-use-purpose grant). Null = never granted (or revoked).
    /// Populated = active grant; <see cref="BagrutReferenceConsentInfo.RevokedAt"/>
    /// non-null = revoked. The 24h wire HMAC token is re-issued from
    /// this fact by <see cref="IBagrutReferenceConsentTokenService"/>;
    /// 90d functional retention via the consent stream.
    /// </summary>
    public BagrutReferenceConsentInfo? BagrutReference { get; private set; }

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

    /// <summary>
    /// Apply a <see cref="ConsentGranted_V2"/> event (prr-123). V2 adds the
    /// accepted privacy-policy version; the fold treats V1 and V2 identically
    /// for the IsGranted/role/timestamp view — the version string is carried
    /// at the raw-event layer for the audit exporter (prr-130), not in the
    /// folded <see cref="ConsentGrantInfo"/>.
    /// </summary>
    public void Apply(ConsentGranted_V2 e)
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

    /// <summary>
    /// Apply an <see cref="AdminConsentOverridden_V1"/> event (prr-096).
    /// Admin override behaves like a grant or revoke depending on the Operation
    /// string: "grant" = new ConsentGrantInfo with IsGranted=true; "revoke" =
    /// flip IsGranted to false + record the justification as RevocationReason.
    /// Unknown operations are defensively ignored so a future "suspend" event
    /// does not corrupt state on an older replay.
    /// </summary>
    public void Apply(AdminConsentOverridden_V1 e)
    {
        switch (e.Operation)
        {
            case "grant":
                _grants[e.Purpose] = new ConsentGrantInfo(
                    IsGranted: true,
                    GrantedByRole: ActorRole.Admin,
                    GrantedAt: e.OverrideAt,
                    ExpiresAt: null,
                    RevokedAt: null,
                    RevokedByRole: null,
                    RevocationReason: null);
                break;

            case "revoke":
                if (_grants.TryGetValue(e.Purpose, out var prior))
                {
                    _grants[e.Purpose] = prior with
                    {
                        IsGranted = false,
                        RevokedAt = e.OverrideAt,
                        RevokedByRole = ActorRole.Admin,
                        RevocationReason = e.Justification,
                    };
                }
                else
                {
                    _grants[e.Purpose] = new ConsentGrantInfo(
                        IsGranted: false,
                        GrantedByRole: null,
                        GrantedAt: null,
                        ExpiresAt: null,
                        RevokedAt: e.OverrideAt,
                        RevokedByRole: ActorRole.Admin,
                        RevocationReason: e.Justification);
                }
                break;

            default:
                // Forward-compatible: unknown operation names are logged at
                // the renderer layer (audit still captures the row) but do
                // not mutate state here.
                break;
        }
    }

    /// <summary>
    /// ADR-0059 §15.3 — student granted Bagrut reference-library consent.
    /// </summary>
    public void Apply(BagrutReferenceConsentGranted_V1 e)
    {
        BagrutReference = new BagrutReferenceConsentInfo(
            GrantedAt: e.GrantedAt,
            DisclosureVersion: e.DisclosureVersion,
            RevokedAt: null,
            RevocationReason: null);
    }

    /// <summary>
    /// ADR-0059 §15.3 — student revoked Bagrut reference-library consent.
    /// Triggers RTBF cascade on rendered events (handled at the
    /// retention-worker layer; this fold just records the fact).
    /// </summary>
    public void Apply(BagrutReferenceConsentRevoked_V1 e)
    {
        if (BagrutReference is null)
        {
            // Revoke-before-grant: record the revoke fact so the wire
            // gate can reject token-issue requests.
            BagrutReference = new BagrutReferenceConsentInfo(
                GrantedAt: e.RevokedAt,
                DisclosureVersion: "(revoke-before-grant)",
                RevokedAt: e.RevokedAt,
                RevocationReason: e.Reason);
            return;
        }
        BagrutReference = BagrutReference with
        {
            RevokedAt = e.RevokedAt,
            RevocationReason = e.Reason,
        };
    }

    /// <summary>
    /// ADR-0059 §15.7 — pure-audit event for a single Reference&lt;T&gt;
    /// render. No state fold; the event is preserved on the consent
    /// stream and surfaces via <c>ReadEventsAsync</c> for SIEM /
    /// retention-worker consumers. Implemented as a no-op so replays
    /// stay deterministic.
    /// </summary>
    public void Apply(BagrutReferenceItemRendered_V1 _) { /* pure-audit */ }
}
