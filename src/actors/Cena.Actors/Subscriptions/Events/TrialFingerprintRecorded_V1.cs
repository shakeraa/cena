// =============================================================================
// Cena Platform — TrialFingerprintRecorded_V1 (Phase 1B trial-then-paywall)
//
// Audit-trail event appended to the parent's `subscription-{parentSubjectId}`
// stream every time a trial is started against a card whose fingerprint hash
// is new to the ledger. Pairs with the singleton ledger document
// (TrialFingerprintLedger.Id = fingerprintHash) — the document is the
// hot-path uniqueness check, the event is the durable per-parent audit trail.
//
// Why two write surfaces and not just the document:
//   - The document is a singleton-by-fingerprint row. Looking at it tells
//     you whether a fingerprint has been used, but not the WHEN/WHO context
//     in the parent's bounded context.
//   - The event lives on the parent's existing subscription stream so the
//     parent's full lifecycle (activation, trial-start, conversion, refund,
//     cancellation, fingerprint-recorded) is one ordered log. Reviewers
//     auditing a single parent don't have to cross-reference two stores.
//
// PII handling per ADR-0038 wire-encryption:
//   - FingerprintHash is one-way: a SHA-256 of the Stripe `card.fingerprint`
//     (which is itself already a stable opaque token, not the raw PAN).
//     Not PII; safe to retain across RTBF (see §1.5a + §5.12 of the trial-
//     recycle-defense brief, GDPR Art 17(3)(e) fraud-prevention exemption).
//   - ParentSubjectIdEncrypted IS PII at-rest in encrypted form. Persisted
//     here so the audit history can be filtered to a single parent without
//     a global scan. Will be crypto-shredded with the rest of the parent's
//     stream on RTBF — the LEDGER DOCUMENT survives the shred (with its
//     own ParentSubjectIdEncrypted cleared); this audit event does not.
//   - NormalizedEmail is the canonical Gmail-folded form per EmailNormalizer.
//     Treated as PII; same RTBF handling as the parent stream.
//
// This event is NOT used by the StudentEntitlementProjection. It is a
// pure audit record. The fingerprint-already-used decision is taken by
// the ledger document Lookup.
// =============================================================================

namespace Cena.Actors.Subscriptions.Events;

/// <summary>
/// Emitted on the parent's <c>subscription-{parentSubjectId}</c> stream when
/// the trial-start path successfully records a new fingerprint in the
/// <c>TrialFingerprintLedger</c>. Pure audit record — the ledger document
/// is the source of truth for uniqueness.
/// </summary>
/// <param name="FingerprintHash">
/// One-way SHA-256 hash of Stripe's <c>card.fingerprint</c> token. Stable
/// across emails per Stripe spec; safe under GDPR Art 17(3)(e) for the
/// fraud-prevention legitimate-interest carve-out.
/// </param>
/// <param name="ParentSubjectIdEncrypted">
/// Encrypted subject id of the parent against whose subscription stream
/// this event is appended. Provided redundantly so the event payload
/// stands alone when read out of context.
/// </param>
/// <param name="NormalizedEmail">
/// Canonical (lower-cased, Gmail-folded) email address used at trial-start,
/// per <see cref="EmailNormalizer.Normalize"/>. Stored alongside the
/// fingerprint so the L2 (email-alias) and L3a (card-fingerprint) signals
/// can be cross-checked without rehydrating the document store.
/// </param>
/// <param name="RecordedAt">UTC timestamp at which the ledger row was written.</param>
public sealed record TrialFingerprintRecorded_V1(
    string FingerprintHash,
    string ParentSubjectIdEncrypted,
    string NormalizedEmail,
    DateTimeOffset RecordedAt);
