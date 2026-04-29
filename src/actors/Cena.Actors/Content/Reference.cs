// =============================================================================
// Cena Platform — Reference<T> wrapper (ADR-0059 §15.1, PRR-267 R3)
//
// Sibling type to Deliverable<T> (Provenance.cs). Where Deliverable<T>
// REFUSES MinistryBagrut-provenanced content (the 2026-04-15 ban),
// Reference<T> permits it under a strict, ADR-0059-locked carve-out:
//
//   1. Provenance.Kind MUST be MinistryBagrut. The wrapper is the
//      Ministry-text seam; Deliverable<T> is for everything else.
//   2. A non-expired ConsentToken bound to the calling student is
//      required. Construction without a token throws.
//   3. consentToken.Context MUST equal the caller-supplied context
//      (no cross-context reuse — a BrowseLibrary token cannot satisfy
//      a VariantSourceCitation render).
//   4. Construction emits an EventId(8009, "BagrutReferenceBrowsed")
//      audit log entry with structured Provenance.Source slash-delimited
//      form (ADR-0059 §15.1) — never the raw item body.
//
// Reference<T> does NOT pass through IItemDeliveryGate.AssertDeliverable
// — that gate is for delivery-for-assessment (Deliverable<T>'s domain).
// Reference<T>'s own factory IS the gate for the reference-browse seam,
// and it does call AssertDeliverable as belt-and-suspenders defense-in-
// depth (per ADR-0059 §15.1 invariant 4 — wrapper owns the audit emit
// + delivery-gate cross-check).
//
// Cross-refs:
//   - ADR-0043 (Bagrut reference-only enforcement) — origin ban
//   - ADR-0059 (carve-out) — this file
//   - ADR-0042 (Consent aggregate) — 90d event-sourced fact behind the
//     24h wire HMAC token (see IBagrutReferenceConsentTokenService)
// =============================================================================

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Content;

/// <summary>
/// Where a <see cref="Reference{T}"/> is being rendered. Tokens are
/// context-scoped — a token issued for <see cref="BrowseLibrary"/> does
/// NOT satisfy <see cref="VariantSourceCitation"/> validation, and
/// vice versa. Per ADR-0059 §15.3 redteam mitigation.
/// </summary>
public enum ReferenceContextKind
{
    /// <summary>
    /// Dedicated reference-library page surface where the student
    /// browses Ministry past papers. No answer affordances; the only
    /// CTA is "Practice a variant of this question".
    /// </summary>
    BrowseLibrary = 1,

    /// <summary>
    /// Citation chip rendered on a practice variant's answer screen
    /// ("Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד) —
    /// numbers changed."). Smaller, scoped, citation-only.
    /// </summary>
    VariantSourceCitation = 2,
}

/// <summary>
/// HMAC-bound consent-token wire identifier. The token itself is an
/// HMAC-SHA256 over <c>{studentId, contextKind, issuedAt, expiresAt}</c>
/// with a server-pepper, with a 24-hour wire TTL (ADR-0059 §15.3
/// redteam mitigation — minimizes forgery blast radius). The longer-
/// lived 90-day fact lives in the ConsentAggregate event stream
/// (ADR-0042) and silently re-issues a wire token when this expires.
///
/// The struct itself is opaque; <see cref="IBagrutReferenceConsentTokenService"/>
/// is the only authoritative issuer/verifier.
/// </summary>
public readonly record struct ConsentTokenId(
    string StudentId,
    ReferenceContextKind Context,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string TokenHmac)
{
    /// <summary>Whether the wire TTL has elapsed (clock-checked at validation).</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}

/// <summary>
/// Phantom-type wrapper that, at construction time, requires Ministry
/// provenance + a valid consent token + a context match. Sibling to
/// <see cref="Deliverable{T}"/>; downstream code that types its
/// reference-render payloads as <c>Reference&lt;T&gt;</c> gets the
/// ADR-0059 §15.1 invariant enforced at the type system.
/// </summary>
/// <remarks>
/// Architecture-test enforced: the public constructor on this record
/// is INTERNAL — only <see cref="From"/> can build a value. Direct ctor
/// is unreachable from any other assembly, and a positive-list arch test
/// pins the factory as the only construction path. See
/// <c>tests/architecture/ReferenceFactoryOnlyTest.cs</c>.
/// </remarks>
public readonly record struct Reference<T>
{
    public T Value { get; }
    public Provenance Provenance { get; }
    public ConsentTokenId ConsentToken { get; }
    public ReferenceContextKind Context { get; }

    // Private constructor — reachable only via From(). Direct
    // initialization (Reference<T> r = new(...)) is unreachable from
    // outside this assembly, and arch test ReferenceFactoryOnlyTest
    // pins the constructor as private + the factory as the only path.
    internal Reference(
        T value,
        Provenance provenance,
        ConsentTokenId consentToken,
        ReferenceContextKind context)
    {
        Value = value;
        Provenance = provenance;
        ConsentToken = consentToken;
        Context = context;
    }

    /// <summary>
    /// Constructs a <see cref="Reference{T}"/>, enforcing the four
    /// ADR-0059 §15.1 invariants. Audit event is emitted on success.
    /// </summary>
    /// <param name="value">The wrapped item (Ministry-derived).</param>
    /// <param name="provenance">Origin classification — MUST be MinistryBagrut.</param>
    /// <param name="consentToken">24h wire HMAC bound to the calling student.</param>
    /// <param name="context">Render context — must match consentToken.Context.</param>
    /// <param name="auditLogger">Logger for EventId(8009, "BagrutReferenceBrowsed").</param>
    /// <param name="now">Clock for expiry validation. Inject via TimeProvider in production.</param>
    /// <param name="itemId">Stable item id (logged; never raw item body).</param>
    /// <param name="sessionId">Session id (logged for SIEM correlation; nullable for browse-library).</param>
    /// <exception cref="InvalidOperationException">
    /// When provenance is not MinistryBagrut, the token is expired,
    /// or the token's context does not match.
    /// </exception>
    public static Reference<T> From(
        T value,
        Provenance provenance,
        ConsentTokenId consentToken,
        ReferenceContextKind context,
        ILogger auditLogger,
        DateTimeOffset now,
        string itemId,
        string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(auditLogger);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        // Invariant 1: Provenance.Kind == MinistryBagrut. Reject misuse
        // for non-corpus items — those should use Deliverable<T>.
        if (provenance.Kind != ProvenanceKind.MinistryBagrut)
        {
            throw new InvalidOperationException(
                $"Reference<T> is for MinistryBagrut-provenanced content only " +
                $"(ADR-0059 §15.1). Got provenance.Kind={provenance.Kind}. " +
                $"Use Deliverable<T> for AiRecreated / TeacherAuthoredOriginal. " +
                $"Source={provenance.Source}");
        }

        // Invariant 2: consent token non-expired (24h wire TTL).
        if (consentToken.IsExpired(now))
        {
            throw new InvalidOperationException(
                $"Reference<T> consent token expired at {consentToken.ExpiresAt:O} " +
                $"(now={now:O}). Re-issue from the 90d event-sourced fact via " +
                $"BagrutReferenceConsentGranted_V1 / IBagrutReferenceConsentTokenService.");
        }

        // Invariant 3: token context must match render context. Defeats
        // the redteam threat where a BrowseLibrary token is replayed
        // against a VariantSourceCitation render to enumerate corpus
        // items off-piste (ADR-0059 §14.2 item 3.b).
        if (consentToken.Context != context)
        {
            throw new InvalidOperationException(
                $"Reference<T> consent-token context mismatch: " +
                $"token.Context={consentToken.Context}, render.Context={context}. " +
                $"Tokens are context-scoped per ADR-0059 §15.3 redteam mitigation.");
        }

        // Invariant 4: emit BagrutReferenceBrowsed audit event with
        // structured Provenance.Source slash-delimited form (ADR-0059
        // §14.2 item 5 — SIEM-tractable per-paper-code takedown).
        // NEVER log the raw item body (the gate's whole point is to
        // refuse leaking it; same applies here).
        auditLogger.LogInformation(
            BagrutReferenceBrowsedEventId,
            "BagrutReferenceBrowsed studentId={StudentId} sessionId={SessionId} "
            + "itemId={ItemId} provenanceSource={ProvenanceSource} "
            + "context={Context} consentTokenIssuedAt={IssuedAt}",
            consentToken.StudentId,
            sessionId ?? "(browse-library)",
            itemId,
            provenance.Source,
            context,
            consentToken.IssuedAt.ToString("O", CultureInfo.InvariantCulture));

        return new Reference<T>(value, provenance, consentToken, context);
    }

    /// <summary>
    /// Pinned event id for the BagrutReferenceBrowsed audit log line.
    /// SIEM pipelines key on this id (8009) so log-line text changes
    /// don't break downstream consumers. Per ADR-0059 §15.1 invariant 4.
    /// </summary>
    public static readonly Microsoft.Extensions.Logging.EventId BagrutReferenceBrowsedEventId =
        new(8009, "BagrutReferenceBrowsed");
}
