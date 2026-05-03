# EPIC-E2E-A — Authentication, onboarding, identity

**Status**: Proposed
**Priority**: P0 (every other flow depends on a signed-in session)
**Scope**: Registration → Firebase emulator account → role claims → tenant bind → profile setup
**Related ADRs**: [ADR-0041](../../docs/adr/0041-parent-auth-role-age-bands.md), [ADR-0042](../../docs/adr/0042-consent-aggregate-bounded-context.md), [ADR-0046](../../docs/adr/0046-httponly-cookie-session.md)

---

## Why this exists

Auth is the load-bearing edge of every user journey. A regression here silently breaks *every* downstream flow:

- Firebase claims transformer rebuilds on every sign-in — a stale cache can give a student admin rights for 60s
- Parent-child binding is a separate aggregate (prr-009 / ADR-0041); binding a wrong child tanks the family dashboard
- Tenant id flows from the JWT custom claims → DB filters → event streams → actor id. Missing / wrong tenant is the #1 cross-institute bleed vector.

## Workflows

### E2E-A-01 — Student registration → Firebase → tenant → home

**Journey**: `/register` → email+password → Firebase emu creates account → student-api POST `/api/auth/on-first-sign-in` → custom claims set (`role=student`, `tenant_id`, `school_id`) → SPA redirects to `/onboarding` (first-time) or `/home` (returning).

**Boundaries**: DOM (register form + redirect target), DB (StudentProfile row created with correct tenant_id), bus (`StudentOnboardedV1` event emitted on `cena.events.student.*.onboarded`).

**Regression caught**: missing claims → 401 loop; wrong tenant → cross-institute visibility.

### E2E-A-02 — Sign-in path (existing account)

**Journey**: `/login` → email+password → Firebase issues idToken → SPA stores token → fetches `/api/me/profile` → lands on `/home`.

**Boundaries**: DOM (no login re-prompt after refresh), API (profile returns caller's own tenant), cookie (httpOnly session cookie set in prod path per ADR-0046).

### E2E-A-03 — Password reset

**Journey**: `/forgot-password` → email entered → Firebase emu queues reset email → test fetches reset link from emulator OOB endpoint → new password set → sign in with new password succeeds.

**Boundaries**: DOM (success message + sign-in succeeds), Firebase emu OOB endpoint (password reset code valid). Old password rejected.

### E2E-A-04 — Parent ↔ child binding (prr-009)

**Journey**: existing student logs in → parent receives bind-invite email → parent clicks link → `/parent/bind?token=...` → confirms kinship → parent-side dashboard shows child.

**Boundaries**: DOM (parent sees child name), DB (`ParentChildBinding` row with correct relationship), bus (`ParentChildBoundV1`). Token single-use: second click → 409.

**Regression caught**: binding leak (parent sees another family's child), unsigned token replay, tenant mismatch between parent + child.

### E2E-A-05 — Role-claim cache invalidation

**Journey**: admin promotes a student to teacher role via admin console → student's next SPA request surfaces teacher-only UI.

**Boundaries**: Firebase custom claims updated, student's idToken refreshed within < 2min, teacher-only route (`/apps/teacher/heatmap`) accessible.

**Regression caught**: the classic 60-second stale-claim hole — RDY-056 patched it once; this spec guards against regression.

### E2E-A-06 — Sign-out clears all surfaces

**Journey**: signed-in → click sign-out → localStorage cleared + httpOnly cookie expired + SignalR disconnected + Firebase token revoked → subsequent `/api/me/profile` returns 401.

**Regression caught**: zombie session where backend still trusts the old token after SPA thinks user is out.

## Out of scope

- Multi-factor auth (not enabled)
- Social login (not enabled — email/password only per RDY-056)
- Biometric auth (mobile-native; out of PWA scope)

## Definition of Done

- [ ] 6 specs green, each < 45s
- [ ] All 4 boundaries (DOM / DB / bus / Firebase emu) asserted on the flagship flows (A-01, A-04)
- [ ] Parent-child binding edge cases (kinship mismatch, token replay, cross-tenant invite) covered
- [ ] Runs nightly in CI; tagged `@auth @p0` for blocking gate
