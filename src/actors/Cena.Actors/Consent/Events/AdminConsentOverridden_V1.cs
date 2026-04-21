// =============================================================================
// Cena Platform — ConsentAggregate event: AdminConsentOverridden_V1 (prr-096)
//
// Emitted when an admin performs an emergency consent override — grant or
// revoke a purpose on behalf of a subject outside the normal
// parent/student-initiated flow. Appended to stream `consent-{subjectId}`.
//
// Policy (prr-096):
//   - Admin-only (ADMIN or SUPER_ADMIN). Enforced at the HTTP boundary.
//   - Requires a free-form justification string (min 10 chars, max 500). The
//     admin is the accountable actor; the justification is the audit surface.
//   - Emits a SIEM notification at write time so security engineering sees
//     every override in the SOC dashboard.
//   - Overrides are RARE by design. The metric cena_consent_admin_override_total
//     is on the weekly SRE-on-call review list; a sudden spike is investigated.
//
// PII classification per ADR-0038:
//   - Encrypted fields: SubjectIdEncrypted, AdminActorIdEncrypted
//   - Plaintext fields: Purpose, Operation ("grant" | "revoke"), InstituteId,
//                       OverrideAt, Justification
//
// Justification is short STRUCTURAL text — admin support workflow names
// rather than student narratives. Examples:
//   "legal-hold-retention-2026-Q2"
//   "parental-access-request-verified-2026-04-18"
//   "retention-policy-migration-cohort-A"
// A future version may tokenize these; keep the free-form shape stable for
// now so legal doesn't chase schema migrations mid-audit.
// =============================================================================

namespace Cena.Actors.Consent.Events;

/// <summary>
/// Admin emergency consent override. Appended to
/// <c>consent-{subjectId}</c>.
/// </summary>
/// <param name="SubjectIdEncrypted">Wire-format encrypted subject id (PII).</param>
/// <param name="Purpose">Processing purpose being granted or revoked.</param>
/// <param name="Operation">
/// Structural operation — <c>"grant"</c> or <c>"revoke"</c>. Kept as string
/// rather than enum so future expansions (e.g., <c>"suspend"</c>) are non-
/// breaking event-schema changes.
/// </param>
/// <param name="AdminActorIdEncrypted">Wire-format encrypted admin actor id (PII).</param>
/// <param name="InstituteId">Institute id scoping the action (ADR-0001).</param>
/// <param name="OverrideAt">Wall-clock UTC timestamp when the override fired.</param>
/// <param name="Justification">
/// Short structural justification (10-500 chars). Appears verbatim in the
/// admin audit export (prr-130).
/// </param>
public sealed record AdminConsentOverridden_V1(
    string SubjectIdEncrypted,
    ConsentPurpose Purpose,
    string Operation,
    string AdminActorIdEncrypted,
    string InstituteId,
    DateTimeOffset OverrideAt,
    string Justification);
