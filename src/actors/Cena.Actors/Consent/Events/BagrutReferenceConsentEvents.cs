// =============================================================================
// Cena Platform — Bagrut Reference Consent Events (ADR-0059 §15.3 / PRR-267)
//
// Event-sourced facts for the Bagrut reference-library consent flow.
// 90-day functional retention (ADR-0042 ConsentAggregate); RTBF-shreddable
// via the same crypto-shred pipeline as misconception data (ADR-0003).
//
//   - BagrutReferenceConsentGranted_V1: student saw the inline
//     disclosure card and explicitly granted. Emits on first reach to
//     /api/v1/reference/papers when no fact exists. Subsequent fetches
//     within the 90d functional retention window silently re-issue
//     the 24h wire HMAC token without re-prompting.
//
//   - BagrutReferenceConsentRevoked_V1: student tapped the one-click
//     revoke affordance on the reference page (ADR-0059 §15.3 — must
//     live on the reference page itself, not deep-link-only). Triggers
//     RTBF cascade on BagrutReferenceItemRendered_V1 events for that
//     student via PRR-015 / PRR-218 RetentionWorker pattern.
//
//   - BagrutReferenceItemRendered_V1: per-render audit event written
//     when Reference<T>.From() succeeds. 180-day retention horizon
//     (ADR-0059 §15.7 / PRR-266 R2). Structured Provenance.Source
//     slash-delimited form for per-paper-code takedown (R2).
// =============================================================================

namespace Cena.Actors.Consent.Events;

/// <summary>
/// Student granted consent for Bagrut reference-library access.
/// </summary>
public sealed record BagrutReferenceConsentGranted_V1(
    string StudentId,
    DateTimeOffset GrantedAt,
    string DisclosureVersion,   // copy/legal version the student saw
    string? UserAgent,          // browser UA; null when not captured
    string? IpAddressHash);     // SHA-256(salted IP); null when not captured

/// <summary>
/// Student revoked consent. Triggers RTBF cascade on rendered events.
/// </summary>
public sealed record BagrutReferenceConsentRevoked_V1(
    string StudentId,
    DateTimeOffset RevokedAt,
    string Reason);              // "user-initiated" | "policy-cascade" | "admin-action"

/// <summary>
/// Single Bagrut reference item rendered to a student. Lifetime: 180 days
/// (ADR-0059 §15.7). Crypto-shredded on RTBF erasure of the student. The
/// ProvenanceSource carries the structured slash-delimited form
/// "ministry-bagrut/{paperCode}/{year}/{season}/{moed}/q{n}" for
/// SIEM-tractable per-paper takedown.
/// </summary>
public sealed record BagrutReferenceItemRendered_V1(
    string StudentId,
    string ItemId,
    string ProvenanceSource,
    string ContextKind,          // "BrowseLibrary" | "VariantSourceCitation"
    string ConsentTokenIssuedAt, // ISO-8601 — for forensic correlation
    DateTimeOffset RenderedAt);
