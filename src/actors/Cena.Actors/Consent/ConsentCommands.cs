// =============================================================================
// Cena Platform — ConsentAggregate command handlers (prr-155, EPIC-PRR-A)
//
// Command surface for the ConsentAggregate. Each handler:
//   1. Runs AgeBandAuthorizationRules to gate the action (refuses with a
//      typed error if denied).
//   2. Encrypts PII fields through EncryptedFieldAccessor per ADR-0038.
//   3. Returns the event(s) to append; callers (the aggregate repository /
//      DI seam) handle the Marten append.
//
// The handler types are immutable record-struct commands carrying the
// inputs needed to make the authorization decision + build the event.
// The separation between command and event is deliberate: commands are the
// write API surface (could be POSTed as DTOs); events are the wire-format
// on-disk shape.
//
// PII contract:
//   - On command handling, SubjectId and ActorId come in as *plaintext*
//     strings (the controller just received them from a validated session
//     token). The handler encrypts them via EncryptedFieldAccessor before
//     constructing the event record. Events therefore never carry
//     plaintext PII on the wire.
// =============================================================================

using Cena.Actors.Consent.Events;
using Cena.Infrastructure.Compliance;

namespace Cena.Actors.Consent;

/// <summary>
/// Grant a consent purpose for a subject. The handler gates on
/// <see cref="AgeBandAuthorizationRules.CanActorGrant"/> and throws
/// <see cref="ConsentAuthorizationException"/> on denial.
/// </summary>
public sealed record GrantConsent(
    string SubjectId,
    AgeBand SubjectBand,
    ConsentPurpose Purpose,
    string Scope,
    ActorRole GrantedByRole,
    string GrantedByActorId,
    DateTimeOffset GrantedAt,
    DateTimeOffset? ExpiresAt);

/// <summary>Revoke a consent purpose for a subject.</summary>
public sealed record RevokeConsent(
    string SubjectId,
    AgeBand SubjectBand,
    ConsentPurpose Purpose,
    ActorRole RevokedByRole,
    string RevokedByActorId,
    DateTimeOffset RevokedAt,
    string Reason);

/// <summary>Add a new purpose to a subject's consent catalog.</summary>
public sealed record AddPurpose(
    string SubjectId,
    AgeBand SubjectBand,
    ConsentPurpose NewPurpose,
    ActorRole AddedByRole,
    DateTimeOffset AddedAt);

/// <summary>
/// Record a parent review cycle over a set of purposes. Used for the
/// ADR-0041 Teen13to15 parent-review workflow.
/// </summary>
public sealed record RecordParentReview(
    string StudentSubjectId,
    AgeBand StudentBand,
    string ParentActorId,
    IReadOnlyList<ConsentPurpose> PurposesReviewed,
    ConsentReviewOutcome Outcome,
    DateTimeOffset ReviewedAt);

/// <summary>
/// prr-052 — opt the parent out of visibility for a specific non-safety
/// purpose. Only permitted for bands where
/// <see cref="AgeBandPolicy.CanStudentVetoPurpose"/> returns true
/// (Teen16to17 and Adult).
/// </summary>
public sealed record VetoParentVisibility(
    string StudentSubjectId,
    AgeBand StudentBand,
    ConsentPurpose Purpose,
    VetoInitiator Initiator,
    string InitiatorActorId,
    string InstituteId,
    DateTimeOffset VetoedAt,
    string Reason);

/// <summary>
/// prr-052 — restore parent visibility of a previously vetoed purpose.
/// Symmetrical with <see cref="VetoParentVisibility"/>.
/// </summary>
public sealed record RestoreParentVisibility(
    string StudentSubjectId,
    AgeBand StudentBand,
    ConsentPurpose Purpose,
    VetoInitiator Initiator,
    string InitiatorActorId,
    string InstituteId,
    DateTimeOffset RestoredAt);

/// <summary>
/// Thrown when a consent command is refused by the age-band authorization
/// matrix. Carries the denial reason string verbatim from
/// <see cref="AgeBandAuthorizationRules"/> so the caller can surface a
/// compliance-auditable audit line.
/// </summary>
public sealed class ConsentAuthorizationException : Exception
{
    public ConsentAuthorizationException(string reason)
        : base(reason) { }
}

/// <summary>
/// Command handlers for the ConsentAggregate. Each handler is async because
/// encryption is async (ISubjectKeyStore may do I/O).
/// </summary>
public sealed class ConsentCommandHandler
{
    private readonly EncryptedFieldAccessor _pii;

    public ConsentCommandHandler(EncryptedFieldAccessor piiAccessor)
    {
        _pii = piiAccessor ?? throw new ArgumentNullException(nameof(piiAccessor));
    }

    /// <summary>
    /// Handle <see cref="GrantConsent"/>. Returns the event to append.
    /// Throws <see cref="ConsentAuthorizationException"/> if the age-band
    /// matrix denies the action.
    /// </summary>
    public async ValueTask<ConsentGranted_V1> HandleAsync(
        GrantConsent cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.SubjectId))
        {
            throw new ArgumentException("SubjectId must be non-empty.", nameof(cmd));
        }

        var outcome = AgeBandAuthorizationRules.CanActorGrant(
            cmd.Purpose, cmd.GrantedByRole, cmd.SubjectBand);
        if (!outcome.Allowed)
        {
            throw new ConsentAuthorizationException(outcome.DenialReason);
        }

        var subjectCipher = await _pii.EncryptAsync(cmd.SubjectId, cmd.SubjectId, ct)
            .ConfigureAwait(false);
        var actorCipher = await _pii.EncryptAsync(cmd.GrantedByActorId, cmd.SubjectId, ct)
            .ConfigureAwait(false);

        return new ConsentGranted_V1(
            SubjectIdEncrypted: subjectCipher ?? string.Empty,
            Purpose: cmd.Purpose,
            Scope: cmd.Scope,
            GrantedByRole: cmd.GrantedByRole,
            GrantedByActorIdEncrypted: actorCipher ?? string.Empty,
            GrantedAt: cmd.GrantedAt,
            ExpiresAt: cmd.ExpiresAt);
    }

    /// <summary>Handle <see cref="RevokeConsent"/>.</summary>
    public async ValueTask<ConsentRevoked_V1> HandleAsync(
        RevokeConsent cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.SubjectId))
        {
            throw new ArgumentException("SubjectId must be non-empty.", nameof(cmd));
        }

        var outcome = AgeBandAuthorizationRules.CanActorRevoke(
            cmd.Purpose, cmd.RevokedByRole, cmd.SubjectBand);
        if (!outcome.Allowed)
        {
            throw new ConsentAuthorizationException(outcome.DenialReason);
        }

        var subjectCipher = await _pii.EncryptAsync(cmd.SubjectId, cmd.SubjectId, ct)
            .ConfigureAwait(false);
        var actorCipher = await _pii.EncryptAsync(cmd.RevokedByActorId, cmd.SubjectId, ct)
            .ConfigureAwait(false);

        return new ConsentRevoked_V1(
            SubjectIdEncrypted: subjectCipher ?? string.Empty,
            Purpose: cmd.Purpose,
            RevokedByRole: cmd.RevokedByRole,
            RevokedByActorIdEncrypted: actorCipher ?? string.Empty,
            RevokedAt: cmd.RevokedAt,
            Reason: cmd.Reason ?? string.Empty);
    }

    /// <summary>
    /// Handle <see cref="AddPurpose"/>. Adding a purpose to the catalog does
    /// not by itself grant the purpose — a paired <see cref="GrantConsent"/>
    /// is expected next. The age-band matrix still applies to "who may
    /// introduce a new purpose on this subject's stream": we use the same
    /// CanActorGrant check since the intent is equivalent.
    /// </summary>
    public ValueTask<ConsentPurposeAdded_V1> HandleAsync(
        AddPurpose cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.SubjectId))
        {
            throw new ArgumentException("SubjectId must be non-empty.", nameof(cmd));
        }

        var outcome = AgeBandAuthorizationRules.CanActorGrant(
            cmd.NewPurpose, cmd.AddedByRole, cmd.SubjectBand);
        if (!outcome.Allowed)
        {
            throw new ConsentAuthorizationException(outcome.DenialReason);
        }

        var streamKey = ConsentAggregate.StreamKey(cmd.SubjectId);
        var evt = new ConsentPurposeAdded_V1(
            ConsentId: streamKey,
            NewPurpose: cmd.NewPurpose,
            AddedByRole: cmd.AddedByRole,
            AddedAt: cmd.AddedAt);
        _ = ct;
        return ValueTask.FromResult(evt);
    }

    /// <summary>Handle <see cref="RecordParentReview"/>.</summary>
    public async ValueTask<ConsentReviewedByParent_V1> HandleAsync(
        RecordParentReview cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.StudentSubjectId))
        {
            throw new ArgumentException("StudentSubjectId must be non-empty.", nameof(cmd));
        }
        if (string.IsNullOrWhiteSpace(cmd.ParentActorId))
        {
            throw new ArgumentException("ParentActorId must be non-empty.", nameof(cmd));
        }
        if (cmd.PurposesReviewed is null || cmd.PurposesReviewed.Count == 0)
        {
            throw new ArgumentException(
                "PurposesReviewed must contain at least one purpose.", nameof(cmd));
        }

        var outcome = AgeBandAuthorizationRules.CanActorReviewAsParent(
            ActorRole.Parent, cmd.StudentBand);
        if (!outcome.Allowed)
        {
            throw new ConsentAuthorizationException(outcome.DenialReason);
        }

        var studentCipher = await _pii.EncryptAsync(
            cmd.StudentSubjectId, cmd.StudentSubjectId, ct).ConfigureAwait(false);
        var parentCipher = await _pii.EncryptAsync(
            cmd.ParentActorId, cmd.StudentSubjectId, ct).ConfigureAwait(false);

        return new ConsentReviewedByParent_V1(
            StudentSubjectIdEncrypted: studentCipher ?? string.Empty,
            ParentActorIdEncrypted: parentCipher ?? string.Empty,
            PurposesReviewed: cmd.PurposesReviewed,
            Outcome: cmd.Outcome,
            ReviewedAt: cmd.ReviewedAt);
    }

    /// <summary>
    /// Handle <see cref="VetoParentVisibility"/> (prr-052). Refuses bands
    /// that lack veto authority (Under13, Teen13to15). Institute-policy
    /// initiators bypass the band check (the institute is not limited by
    /// the age band — it is adding a narrower restriction).
    /// </summary>
    public async ValueTask<StudentVisibilityVetoed_V1> HandleAsync(
        VetoParentVisibility cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.StudentSubjectId))
        {
            throw new ArgumentException("StudentSubjectId must be non-empty.", nameof(cmd));
        }
        if (string.IsNullOrWhiteSpace(cmd.InitiatorActorId))
        {
            throw new ArgumentException("InitiatorActorId must be non-empty.", nameof(cmd));
        }
        if (string.IsNullOrWhiteSpace(cmd.InstituteId))
        {
            throw new ArgumentException("InstituteId must be non-empty.", nameof(cmd));
        }

        if (cmd.Initiator == VetoInitiator.Student
            && !AgeBandPolicy.CanStudentVetoPurpose(cmd.StudentBand, cmd.Purpose))
        {
            throw new ConsentAuthorizationException(
                $"Student veto refused: band {cmd.StudentBand} has no veto authority "
                + $"for purpose '{cmd.Purpose}' (ADR-0041 / prr-052).");
        }

        var studentCipher = await _pii.EncryptAsync(
            cmd.StudentSubjectId, cmd.StudentSubjectId, ct).ConfigureAwait(false);
        var initiatorCipher = await _pii.EncryptAsync(
            cmd.InitiatorActorId, cmd.StudentSubjectId, ct).ConfigureAwait(false);

        return new StudentVisibilityVetoed_V1(
            StudentSubjectIdEncrypted: studentCipher ?? string.Empty,
            Purpose: cmd.Purpose,
            Initiator: cmd.Initiator,
            InitiatorActorIdEncrypted: initiatorCipher ?? string.Empty,
            InstituteId: cmd.InstituteId,
            VetoedAt: cmd.VetoedAt,
            Reason: cmd.Reason ?? string.Empty);
    }

    /// <summary>Handle <see cref="RestoreParentVisibility"/> (prr-052).</summary>
    public async ValueTask<StudentVisibilityRestored_V1> HandleAsync(
        RestoreParentVisibility cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (string.IsNullOrWhiteSpace(cmd.StudentSubjectId))
        {
            throw new ArgumentException("StudentSubjectId must be non-empty.", nameof(cmd));
        }
        if (string.IsNullOrWhiteSpace(cmd.InitiatorActorId))
        {
            throw new ArgumentException("InitiatorActorId must be non-empty.", nameof(cmd));
        }
        if (string.IsNullOrWhiteSpace(cmd.InstituteId))
        {
            throw new ArgumentException("InstituteId must be non-empty.", nameof(cmd));
        }

        // Restore is allowed whenever the band has any veto right at all,
        // regardless of whether a veto is currently recorded (idempotent).
        if (cmd.Initiator == VetoInitiator.Student
            && !AgeBandPolicy.StudentHasAnyVetoRight(cmd.StudentBand))
        {
            throw new ConsentAuthorizationException(
                $"Student restore refused: band {cmd.StudentBand} has no veto authority.");
        }

        var studentCipher = await _pii.EncryptAsync(
            cmd.StudentSubjectId, cmd.StudentSubjectId, ct).ConfigureAwait(false);
        var initiatorCipher = await _pii.EncryptAsync(
            cmd.InitiatorActorId, cmd.StudentSubjectId, ct).ConfigureAwait(false);

        return new StudentVisibilityRestored_V1(
            StudentSubjectIdEncrypted: studentCipher ?? string.Empty,
            Purpose: cmd.Purpose,
            Initiator: cmd.Initiator,
            InitiatorActorIdEncrypted: initiatorCipher ?? string.Empty,
            InstituteId: cmd.InstituteId,
            RestoredAt: cmd.RestoredAt);
    }
}
