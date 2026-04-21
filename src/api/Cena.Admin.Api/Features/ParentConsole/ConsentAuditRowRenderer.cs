// =============================================================================
// Cena Platform — Consent audit row renderer (prr-130)
//
// Pure-function mapping from a persisted consent event to the flat
// ConsentAuditRowDto shape. Every event type in Cena.Actors.Consent.Events
// MUST be handled here; the ConsentAuditExportDoesNotOmitEventsTest
// architecture ratchet enforces this by reflection.
//
// PII contract:
//   - The event-wire layer carries *Encrypted fields; this renderer
//     decrypts via EncryptedFieldAccessor before placing the plaintext
//     into the DTO. The plaintext is an anonymised subject/actor id
//     (ADR-0038) — not a display name or email — so surfacing it to an
//     admin who is already authorised for this student does not expand
//     the PII blast radius.
//   - Decryption failure (erased / tombstoned subject) renders the
//     actor column as "(erased)" — the event row itself is still
//     preserved because the audit purpose is to show what the system
//     did, and "done, actor unknown" is a legitimate final state.
// =============================================================================

using Cena.Actors.Consent;
using Cena.Actors.Consent.Events;
using Cena.Infrastructure.Compliance;

namespace Cena.Admin.Api.Features.ParentConsole;

/// <summary>
/// Renders persisted consent events into flat audit rows.
/// </summary>
internal static class ConsentAuditRowRenderer
{
    /// <summary>Source tag emitted for regular grant/revoke API calls.</summary>
    public const string SourceApi = "api";

    /// <summary>Source tag emitted for one-click unsubscribe-link revokes (prr-051).</summary>
    public const string SourceUnsubscribeLink = "unsubscribe-link";

    /// <summary>Source tag emitted for super-admin compliance overrides.</summary>
    public const string SourceAdminOverride = "admin-override";

    /// <summary>Source tag emitted for retention-worker revokes / expiries.</summary>
    public const string SourceSystemRetention = "system-retention";

    /// <summary>Source tag emitted for institute-policy-initiated veto / restore (prr-052).</summary>
    public const string SourceInstitutePolicy = "institute-policy";

    /// <summary>Source tag emitted for the parent-review workflow.</summary>
    public const string SourceParentReview = "parent-review";

    /// <summary>Placeholder shown when decryption fails (erased stream).</summary>
    public const string ErasedPlaceholder = "(erased)";

    /// <summary>
    /// Render a single event. Returns null if the event type is not
    /// recognised (should never happen — <see cref="RenderRowAsync"/>
    /// returns null only in the default case, which the architecture
    /// ratchet forbids from firing on any type in the Events namespace).
    /// </summary>
    public static async ValueTask<ConsentAuditRowDto?> RenderRowAsync(
        object raw,
        string studentAnonId,
        EncryptedFieldAccessor pii,
        CancellationToken ct)
    {
        return raw switch
        {
            ConsentGranted_V2 g2   => await RenderGrantV2Async(g2, studentAnonId, pii, ct).ConfigureAwait(false),
            ConsentGranted_V1 g1   => await RenderGrantV1Async(g1, studentAnonId, pii, ct).ConfigureAwait(false),
            ConsentRevoked_V1 r    => await RenderRevokeAsync(r, studentAnonId, pii, ct).ConfigureAwait(false),
            ConsentPurposeAdded_V1 p => RenderPurposeAdded(p),
            ConsentReviewedByParent_V1 rv => await RenderParentReviewAsync(rv, studentAnonId, pii, ct).ConfigureAwait(false),
            StudentVisibilityVetoed_V1 v => await RenderVetoAsync(v, studentAnonId, pii, ct).ConfigureAwait(false),
            StudentVisibilityRestored_V1 rs => await RenderRestoreAsync(rs, studentAnonId, pii, ct).ConfigureAwait(false),
            _ => null,
        };
    }

    // ── Per-event renderers ─────────────────────────────────────────────

    private static async ValueTask<ConsentAuditRowDto> RenderGrantV2Async(
        ConsentGranted_V2 e, string studentAnonId, EncryptedFieldAccessor pii, CancellationToken ct)
    {
        var actor = await DecryptAsync(e.GrantedByActorIdEncrypted, studentAnonId, pii, ct)
            .ConfigureAwait(false);
        var source = e.GrantedByRole == ActorRole.System
            ? SourceSystemRetention
            : SourceApi;
        return new ConsentAuditRowDto(
            EventType: nameof(ConsentGranted_V2),
            Timestamp: FormatIso(e.GrantedAt),
            Purpose: e.Purpose.ToString(),
            ActorRole: e.GrantedByRole.ToString(),
            ActorAnonId: actor,
            PolicyVersionAccepted: e.PolicyVersionAccepted,
            Source: source,
            Reason: string.Empty,
            Scope: e.Scope ?? string.Empty,
            InstituteId: string.Empty,
            TraceId: string.Empty,
            ExpiresAt: e.ExpiresAt.HasValue ? FormatIso(e.ExpiresAt.Value) : string.Empty);
    }

    private static async ValueTask<ConsentAuditRowDto> RenderGrantV1Async(
        ConsentGranted_V1 e, string studentAnonId, EncryptedFieldAccessor pii, CancellationToken ct)
    {
        // V1 pre-dates prr-123 policy versioning. The audit surface renders
        // the sentinel so counsel can grep for legacy grants.
        var actor = await DecryptAsync(e.GrantedByActorIdEncrypted, studentAnonId, pii, ct)
            .ConfigureAwait(false);
        return new ConsentAuditRowDto(
            EventType: nameof(ConsentGranted_V1),
            Timestamp: FormatIso(e.GrantedAt),
            Purpose: e.Purpose.ToString(),
            ActorRole: e.GrantedByRole.ToString(),
            ActorAnonId: actor,
            PolicyVersionAccepted: PolicyVersionSentinels.PreVersioning,
            Source: e.GrantedByRole == ActorRole.System ? SourceSystemRetention : SourceApi,
            Reason: string.Empty,
            Scope: e.Scope ?? string.Empty,
            InstituteId: string.Empty,
            TraceId: string.Empty,
            ExpiresAt: e.ExpiresAt.HasValue ? FormatIso(e.ExpiresAt.Value) : string.Empty);
    }

    private static async ValueTask<ConsentAuditRowDto> RenderRevokeAsync(
        ConsentRevoked_V1 e, string studentAnonId, EncryptedFieldAccessor pii, CancellationToken ct)
    {
        var actor = await DecryptAsync(e.RevokedByActorIdEncrypted, studentAnonId, pii, ct)
            .ConfigureAwait(false);
        var source = (e.Reason ?? string.Empty) switch
        {
            var r when r.Contains("unsubscribe", StringComparison.OrdinalIgnoreCase) => SourceUnsubscribeLink,
            _ when e.RevokedByRole == ActorRole.System => SourceSystemRetention,
            _ when e.RevokedByRole == ActorRole.Admin => SourceAdminOverride,
            _ => SourceApi,
        };
        return new ConsentAuditRowDto(
            EventType: nameof(ConsentRevoked_V1),
            Timestamp: FormatIso(e.RevokedAt),
            Purpose: e.Purpose.ToString(),
            ActorRole: e.RevokedByRole.ToString(),
            ActorAnonId: actor,
            PolicyVersionAccepted: string.Empty,
            Source: source,
            Reason: e.Reason ?? string.Empty,
            Scope: string.Empty,
            InstituteId: string.Empty,
            TraceId: string.Empty,
            ExpiresAt: string.Empty);
    }

    private static ConsentAuditRowDto RenderPurposeAdded(ConsentPurposeAdded_V1 e)
    {
        return new ConsentAuditRowDto(
            EventType: nameof(ConsentPurposeAdded_V1),
            Timestamp: FormatIso(e.AddedAt),
            Purpose: e.NewPurpose.ToString(),
            ActorRole: e.AddedByRole.ToString(),
            ActorAnonId: string.Empty, // event intentionally carries no actor id
            PolicyVersionAccepted: string.Empty,
            Source: SourceApi,
            Reason: string.Empty,
            Scope: string.Empty,
            InstituteId: string.Empty,
            TraceId: string.Empty,
            ExpiresAt: string.Empty);
    }

    private static async ValueTask<ConsentAuditRowDto> RenderParentReviewAsync(
        ConsentReviewedByParent_V1 e, string studentAnonId, EncryptedFieldAccessor pii, CancellationToken ct)
    {
        var parent = await DecryptAsync(e.ParentActorIdEncrypted, studentAnonId, pii, ct)
            .ConfigureAwait(false);
        var purposeJoined = string.Join("|", e.PurposesReviewed.Select(p => p.ToString()));
        return new ConsentAuditRowDto(
            EventType: nameof(ConsentReviewedByParent_V1),
            Timestamp: FormatIso(e.ReviewedAt),
            Purpose: purposeJoined,
            ActorRole: ActorRole.Parent.ToString(),
            ActorAnonId: parent,
            PolicyVersionAccepted: string.Empty,
            Source: SourceParentReview,
            Reason: e.Outcome.ToString(),
            Scope: string.Empty,
            InstituteId: string.Empty,
            TraceId: string.Empty,
            ExpiresAt: string.Empty);
    }

    private static async ValueTask<ConsentAuditRowDto> RenderVetoAsync(
        StudentVisibilityVetoed_V1 e, string studentAnonId, EncryptedFieldAccessor pii, CancellationToken ct)
    {
        var actor = await DecryptAsync(e.InitiatorActorIdEncrypted, studentAnonId, pii, ct)
            .ConfigureAwait(false);
        var role = e.Initiator == VetoInitiator.Student
            ? ActorRole.Student.ToString()
            : ActorRole.Admin.ToString();
        var source = e.Initiator == VetoInitiator.InstitutePolicy
            ? SourceInstitutePolicy
            : SourceApi;
        return new ConsentAuditRowDto(
            EventType: nameof(StudentVisibilityVetoed_V1),
            Timestamp: FormatIso(e.VetoedAt),
            Purpose: e.Purpose.ToString(),
            ActorRole: role,
            ActorAnonId: actor,
            PolicyVersionAccepted: string.Empty,
            Source: source,
            Reason: e.Reason ?? string.Empty,
            Scope: string.Empty,
            InstituteId: e.InstituteId ?? string.Empty,
            TraceId: string.Empty,
            ExpiresAt: string.Empty);
    }

    private static async ValueTask<ConsentAuditRowDto> RenderRestoreAsync(
        StudentVisibilityRestored_V1 e, string studentAnonId, EncryptedFieldAccessor pii, CancellationToken ct)
    {
        var actor = await DecryptAsync(e.InitiatorActorIdEncrypted, studentAnonId, pii, ct)
            .ConfigureAwait(false);
        var role = e.Initiator == VetoInitiator.Student
            ? ActorRole.Student.ToString()
            : ActorRole.Admin.ToString();
        var source = e.Initiator == VetoInitiator.InstitutePolicy
            ? SourceInstitutePolicy
            : SourceApi;
        return new ConsentAuditRowDto(
            EventType: nameof(StudentVisibilityRestored_V1),
            Timestamp: FormatIso(e.RestoredAt),
            Purpose: e.Purpose.ToString(),
            ActorRole: role,
            ActorAnonId: actor,
            PolicyVersionAccepted: string.Empty,
            Source: source,
            Reason: string.Empty,
            Scope: string.Empty,
            InstituteId: e.InstituteId ?? string.Empty,
            TraceId: string.Empty,
            ExpiresAt: string.Empty);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string FormatIso(DateTimeOffset ts)
        => ts.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static async ValueTask<string> DecryptAsync(
        string? cipher, string subjectId, EncryptedFieldAccessor pii, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(cipher)) return string.Empty;
        var (ok, plaintext) = await pii.TryDecryptAsync(cipher, subjectId, ct)
            .ConfigureAwait(false);
        return ok ? plaintext : ErasedPlaceholder;
    }
}
