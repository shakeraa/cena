# Parent → child IDOR threat model (prr-009)

- **Status**: authoritative for prr-009 implementation
- **Date**: 2026-04-20
- **Author**: claude-subagent-prr009
- **Scope**: every HTTP endpoint whose authorization model is "caller claims
  PARENT role for a studentAnonId passed in the URL"
- **Binding source of truth**: `IParentChildBindingStore` (authoritative DB);
  JWT `parent_of` entries are an advisory cache re-derived at login and
  every session refresh (ADR-0041).

## 1. Surface

Four parent-facing endpoints exist today (Admin API):

| # | Verb | Route | Reads/writes |
|---|------|-------|--------------|
| 1 | GET  | `/api/v1/parent/minors/{studentAnonId}/time-budget` | Marten event stream |
| 2 | PUT  | `/api/v1/parent/minors/{studentAnonId}/time-budget` | Appends `ParentalControlsConfiguredV1` |
| 3 | GET  | `/api/v1/parent/minors/{studentAnonId}/accommodations` | Marten event stream |
| 4 | PUT  | `/api/v1/parent/minors/{studentAnonId}/accommodations` | Appends `AccommodationProfileAssignedV1` |

Before this task, every one of the four was exploitable — see §3.

## 2. Actors and assets

| Actor | Capability |
|---|---|
| Parent A (authenticated, one linked child at institute X) | Expected: access their own child at X |
| Parent B (malicious, same platform) | May hold a PARENT token, any bound child |
| Attacker with stolen PARENT JWT | May hold a PARENT token, any bound child |
| Admin / Mentor (institute-scoped) | Legitimate read-only for support workflows |
| Super-admin | Unrestricted by design |

Assets: minor's accommodations profile (GDPR Art 9 — health category), minor's
time-budget and topic allow-list, audit trail integrity.

## 3. STRIDE (pre-mitigation)

| Threat | Category | Exploit (pre-prr-009) |
|---|---|---|
| Parent B reads Parent A's child's accommodations | Information disclosure | GET route accepts any `studentAnonId`; `CallerIsLinkedGuardianAsync` returns `true` unconditionally (stub). |
| Parent B sets Parent A's child's time-budget to 0 | Tampering | PUT route only checks role == PARENT; no binding check. |
| Parent B writes accommodations event with Parent B's own anonId on behalf of Parent A's child | Tampering + repudiation | Same root cause; audit trail records the wrong parent. |
| Parent at institute X reads minor's data at institute Y | Elevation-of-privilege (cross-tenant) | Institute is never compared; only `studentAnonId` is matched. Even the existing `ResourceOwnershipGuard` ignores `institute_id`. |
| Parent B enumerates valid `studentAnonId`s via 200-vs-404 oracle | Information disclosure | Untouched by the stub guard. |
| DoS via unbounded PUT traffic | Denial-of-service | Out-of-scope (rate-limiting is tracked separately); noted for completeness. |

## 4. STRIDE (post-mitigation)

Single enforcement seam: `ParentAuthorizationGuard.AssertCanAccessAsync(
parentActorId, studentSubjectId, instituteId, traceId, ct)` throws
`ForbiddenException(CENA_AUTH_IDOR_VIOLATION)` on any of:

1. Role is not PARENT (defense-in-depth; callers should already have gated).
2. Parent's claim set does not contain `(studentSubjectId, instituteId)`.
3. Authoritative `IParentChildBindingStore` reports no active binding.
4. Authoritative binding's `instituteId` does not match the argument
   (tenant-crossing — even if the parent once had a binding at a different
   institute).

Every parent endpoint in the architecture ratchet
(`NoParentEndpointBypassesBindingTest`) either calls
`AssertCanAccessAsync` before touching student data, or carries the
`[AllowsUnboundParent]` attribute with a written justification. Allowlist
starts empty.

## 5. DREAD scoring (pre-mitigation)

| Threat | D | R | E | A | D | Total | Priority |
|---|---|---|---|---|---|---|---|
| Cross-parent read of accommodations | 9 | 10 | 9 | 8 | 8 | 8.8 | critical |
| Cross-parent write of time-budget | 9 | 10 | 9 | 8 | 7 | 8.6 | critical |
| Cross-tenant read | 10 | 8 | 7 | 5 | 6 | 7.2 | high |

After prr-009 lands, all three collapse to "single guard must pass" —
architecture test keeps that invariant from regressing.

## 6. Audit logging contract

`ParentAuthorizationGuard.AssertCanAccessAsync` emits one structured log
line per call:

```
[prr-009] parent-binding-check parent=<anonId> child=<anonId>
         institute=<id> bound=<bool> tenant_match=<bool>
         outcome=<allow|deny-unbound|deny-cross-tenant|deny-revoked>
         trace_id=<id>
```

- Anon IDs only (no email, phone, or display name).
- `outcome=allow` is logged at INFO; denies at WARNING. A tenant-crossing
  deny (a parent bound at X attempting Y) is also counted on the
  `cena_parent_idor_denied_total{reason=cross_tenant}` metric as an
  early-warning signal for credential theft or a buggy client.

## 7. Out of scope (tracked separately)

- VPC-specific evidential requirements for Under13 bindings — covered by
  prr-155 / ConsentAggregate.
- ParentChildBinding Marten projection + backfill — this task ships the
  in-memory store; Marten-backed store is an EPIC-PRR-A Sprint 2
  follow-up (same pattern ADR-0042 documents for `ConsentAggregate`).
- Rate-limiting of parent endpoints — orthogonal concern.
