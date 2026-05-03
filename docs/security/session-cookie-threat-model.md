# Session cookie threat model (prr-011)

- **Status**: authoritative for prr-011 Phase 1B hardening
- **Date**: 2026-04-20
- **Author**: claude-subagent-prr011
- **Scope**: the BFF session-exchange flow, its httpOnly cookie, its
  server-minted session JWT, its refresh rotation, and its logout.
- **Binding source of truth**: Firebase Admin SDK for ID-token verification
  at exchange time. Post-exchange the server session JWT (HS256, jti-keyed)
  is authoritative; `SessionRevocationList` gates the jti.

## 0. Design non-negotiables honoured here

- ADR-0001 tenant scoping survives the whole flow — the server session JWT
  mirrors `school_id` / `parent_of` claims from the Firebase ID token at
  mint time so downstream middleware (`ResourceOwnershipGuard`,
  `ParentAuthorizationGuard`) still sees them.
- ADR-0041 parent visibility — parent cookies carry per-`(studentId,
  instituteId)` `parent_of` entries; refresh re-derives those from the
  consent aggregate, so a revoked link takes effect in ≤ one refresh cycle.
- Session is session-scoped. No student profile gets a "last session JWT"
  field anywhere — that would reintroduce an XSS exfil target.
- `<500 LOC per file`, no stubs, no dark-pattern engagement copy in any
  string emitted by the flow.

## 1. Surface

Three endpoints plus one middleware, all mounted under `/api/auth/session`:

| # | Verb | Route | Purpose | Reads |
|---|------|-------|---------|-------|
| 1 | POST | `/api/auth/session` | Firebase ID token → cookie | `Authorization: Bearer` (one-shot) |
| 2 | POST | `/api/auth/session/refresh` | Rotate cookie session ID | cookie |
| 3 | POST | `/api/auth/session/logout` | Clear cookie + revoke jti | cookie (best-effort) |
| — | middleware | `CookieAuthMiddleware` | Attach principal from cookie | cookie |

And one invariant: every OTHER endpoint in `src/api/` authenticates by
cookie. The `NoBearerTokenEndpointsTest` allowlist is exactly one file.

Response DTOs: `SessionExchangeResponse(userId, expiresAt)` — never carries
the raw JWT. The new `NoJwtInLocalStorageResponseTest` enforces this by
scanning every auth DTO for string properties named `access_token`,
`id_token`, `jwt`, `session_token`, `bearer`, or `sessionJwt` and failing
the build if one appears.

## 2. Actors and assets

| Actor | Capability |
|---|---|
| Student (authenticated) | Holds a cookie; can refresh, logout, exercise student-facing endpoints |
| Parent (authenticated) | Same, plus `parent_of` claims scope visibility |
| Teacher / Admin (authenticated) | Same, plus role claims |
| XSS drive-by on the SPA | Can run arbitrary JS in the victim's origin; CANNOT read httpOnly cookies |
| Network adversary (same LAN, hostile Wi-Fi) | Can watch plaintext traffic if TLS is stripped |
| Subdomain adversary (compromised sibling subdomain) | Can set cookies that shadow our cookie UNLESS `__Host-` prefix is used |
| Attacker in possession of an exfiltrated cookie value | Can replay until cookie expires or revocation catches it |
| Attacker with physical access to an unlocked device | Can perform any legitimate action the cookie grants |

Assets:

- Student session authority (all learning actions, mastery writes)
- Parent access to minor's data (GDPR Art 8, COPPA, Israeli PPA — ADR-0041)
- Admin/teacher tenant-wide privileges
- Audit trail integrity (every write is attributable to a Firebase uid)

## 3. STRIDE (pre-mitigation, then post-mitigation)

| # | Threat | STRIDE | Pre-mitigation exploit | Post-mitigation status |
|---|---|---|---|---|
| T1 | **XSS exfiltrates JWT from localStorage** | Information disclosure | Any XSS (dependency, markdown, embed) does `fetch('/api/me', {headers:{Authorization: localStorage.getItem('jwt')}})` and uploads the token. | **Closed.** `NoLocalStorageAuthTest` fails CI on any `localStorage.setItem` with auth-shaped key outside a build guard. `NoJwtInLocalStorageResponseTest` prevents a backend regression that would hand the JWT back in a body for a client to store. Cookie is httpOnly. |
| T2 | **Subdomain cookie shadowing** | Spoofing / Tampering | Attacker controls `evil.cena.io`; sets `Set-Cookie: cena_session=evil; Domain=.cena.io; Path=/`; browser sends it alongside our legitimate cookie and a racing parse picks evil. | **Closed.** Cookie name is `__Host-cena_session`. The `__Host-` prefix is browser-enforced: cookies carrying it MUST have `Secure`, `Path=/`, and MUST NOT carry `Domain`. Subdomains cannot set a `__Host-` cookie that collides with ours. |
| T3 | **CSRF on mutating endpoints** | Tampering | Attacker on `evil.example` POSTs to `/api/session/answer` via a hidden form; browser attaches our cookie automatically. | **Mitigated by SameSite=Strict**, not closed. A user following a link from `evil.example` directly to us does NOT carry the cookie (Strict), so the CSRF attack has no cookie to ride. Deferred to follow-up prr-011c: double-submit token for stronger defence (blocks same-origin iframe + XSS chains). Today's Strict + httpOnly gives us the practical CSRF closure for authenticated POSTs. |
| T4 | **Refresh-rotation race** | Spoofing | Attacker steals cookie at 10:00. Victim naturally refreshes at 10:05 — old cookie now dead, new cookie minted. Attacker still holds the 10:00 copy but tries to use it; if we accept it, he continues. If we ONLY accept it, victim gets logged out. | **Closed.** Rotation is atomic: refresh mints a new jti AND revokes the old jti in `SessionRevocationList`. If a presented cookie's jti is found in the revocation list AND its `rotated_to` claim points to a live successor, the server (a) rejects the request, (b) revokes the successor too (the successor is now suspect — either the attacker raced ahead with the old jti, or a double-refresh victim), and (c) logs a `SESSION_ROTATION_RACE` event. User is forced back to Firebase for re-auth. Conservative but correct. |
| T5 | **Refresh-reuse (single old cookie replayed after legitimate refresh)** | Tampering | Attacker replays the pre-rotation cookie after the victim has already refreshed. | **Closed.** Same mechanism as T4 — the old jti is on the revocation list; `CookieAuthMiddleware.IsRevoked` yields 401. |
| T6 | **Cookie fixation** | Spoofing | Attacker sets a known cookie on victim's browser pre-login (e.g. via subdomain or meta-refresh), then waits for victim to authenticate against the attacker's jti. | **Closed.** `__Host-` prevents the subdomain set. Every successful exchange mints a fresh jti server-side and sets `__Host-cena_session` with the new JWT — a pre-set value never survives the exchange. |
| T7 | **Firebase ID-token replay** | Spoofing | Attacker captures the Firebase ID token on the one-shot exchange request and replays it. | **Bounded, not closed.** Firebase ID tokens are ≤1h TTL and enforce audience binding to `Firebase:ProjectId`. An attacker with the ID token has a ≤1h window to call `/api/auth/session` and get their own cookie. The cookie they receive binds to their own user-agent (SameSite=Strict cookie on a non-victim origin fails SameSite on the attacker's browser). The practical window is the HTTPS-in-flight window between victim and our origin — i.e. zero outside a TLS break. We do NOT accept ID tokens twice (the endpoint is stateless; duplicate-call detection is a follow-up if post-mortem says it's needed). |
| T8 | **Logout replay (exfil'd cookie reused after victim logs out)** | Tampering | Attacker exfils cookie at 10:00, victim logs out at 10:05, attacker replays at 10:10. | **Closed.** Logout adds the jti to `SessionRevocationList` with the cookie's own `exp`. Subsequent requests with the same jti are 401'd by `CookieAuthMiddleware.IsRevoked`, regardless of whether the client honoured the `Set-Cookie: ...Expires=<past>` deletion. |
| T9 | **Logout without cookie** | Repudiation | Attacker calls `/logout` without a cookie hoping to smoke out the revocation list. | Neutral. Logout is idempotent: no cookie → 204 with no state change. No oracle. |
| T10 | **Middleware ordering bypass** | Elevation-of-privilege | Endpoint happens to sit in front of `UseAuthentication()` / `CookieAuthMiddleware` in the pipeline; cookie is present but never parsed; endpoint reads claims as unauthenticated (or falls back to a default that is too permissive). | **Closed** by explicit ordering assertion. `CookieAuthMiddleware` is registered exactly once, inside `StudentApiExtrasRegistration.UseStudentApiAuthPipeline()` which is called immediately after `UseAuthentication()` and before `UseAuthorization()`. An architecture test (`NoBearerTokenEndpointsTest` today + the new pipeline-order assertion) enforces that exactly one cookie middleware registration exists and that it precedes `UseAuthorization()`. |
| T11 | **Session-signing-key disclosure** | Spoofing | Attacker obtains `SessionJwt:SigningKey`; forges cookies for arbitrary users. | **Out of scope** for this task, tracked as a follow-up: the key today is a symmetric HS256 secret, rotated manually. Production port to RS256 + JWKS rotation is prr-011d. Interim mitigation: secret lives in env/secret-store, is distinct per-environment, and is covered by the existing secret-scanner CI. |
| T12 | **DoS via unbounded revocation-list growth** | Denial-of-service | Attacker floods /logout with fresh jtis; list grows without bound. | **Closed.** `SessionRevocationListCleanupService` sweeps expired entries every 5 minutes; entries also self-expire at cookie lifetime. Worst case is `logouts_per_24h` entries, each ~40 bytes. |
| T13 | **Cross-tenant cookie on a shared browser** | EoP | A student using a shared device still holds a cookie from another student. | **Mitigated by session lifetime (24h default)** + explicit logout. Not closed — a shared-device problem is fundamentally solved by UX (logout button, auto-logout on idle), not by this layer. Tracked in prr-011e (idle logout). |

## 4. DREAD scoring (pre-mitigation)

| Threat | D | R | E | A | D | Total | Priority |
|---|---|---|---|---|---|---|---|
| T1 XSS drain (ship-blocker pre-prr-011) | 10 | 10 | 9 | 9 | 10 | 9.6 | critical |
| T2 Subdomain shadow | 8 | 7 | 7 | 8 | 6 | 7.2 | high |
| T3 CSRF | 7 | 8 | 8 | 9 | 7 | 7.8 | high |
| T4 Rotation race | 8 | 5 | 6 | 3 | 4 | 5.2 | medium |
| T5 Refresh reuse | 7 | 6 | 6 | 3 | 5 | 5.4 | medium |
| T6 Cookie fixation | 6 | 5 | 5 | 3 | 4 | 4.6 | medium |
| T7 ID-token replay | 6 | 3 | 4 | 3 | 3 | 3.8 | low |
| T8 Logout replay | 7 | 7 | 6 | 3 | 5 | 5.6 | medium |
| T10 Middleware bypass | 9 | 5 | 6 | 8 | 5 | 6.6 | high |

All critical and high threats collapse to "single guard+prefix+rotation
must pass" after prr-011 Phase 1B. Architecture tests keep those
invariants from regressing.

## 5. Deferred-to-follow-up (explicit, tracked)

| ID | Defer | Reason | When |
|---|---|---|---|
| prr-011c | CSRF double-submit token | SameSite=Strict is sufficient for Phase 1B. Double-submit adds defence-in-depth against future SameSite relaxations (browser bug, policy change). | Before any cross-site embed / third-party widget integration |
| prr-011d | HS256 → RS256 + JWKS rotation | HS256 is fine for a single-host pilot. Multi-host production wants RS256 so a validator compromise does not let the validator mint. | Before horizontal scale-out |
| prr-011e | Idle-timeout auto-logout | Shared-device UX is UX work, not auth-pipeline work. | Pre-Bagrut if shared-device pilots surface the need |
| prr-011f | Frontend Vue migration to cookie-only (kill `VITE_USE_MOCK_AUTH`, drop IndexedDB persistence, remove Authorization header from Axios) | Sibling task — backend is ready today for the migration | Scheduled as sibling in pre-release-review batch |
| prr-011g | E2E Playwright test verifying cookie is unreadable from the SPA and `/api/me` succeeds without Authorization | Requires the frontend migration of prr-011f | After prr-011f |
| prr-011h | Move `SessionRevocationList` from in-memory to Redis | In-memory is wrong for multi-pod; the interface is already single-line-swappable | Before multi-pod deploy |

## 6. Audit logging contract

Every exchange, refresh, and logout emits a structured log with:

- `event`: `SESSION_EXCHANGE` / `SESSION_REFRESH` / `SESSION_LOGOUT` /
  `SESSION_ROTATION_RACE`
- `uid_prefix`: first 8 chars of Firebase uid (PII minimisation; full uid
  is already elsewhere in correlated traces)
- `jti_prefix`: first 8 chars of the session jti
- `expires_at`: absolute UTC
- `correlation_id`: from `CorrelationIdMiddleware`
- no cookie value ever reaches a log sink

Scrubbing contract inherited from `SessionRiskLogEnricher` +
`PiiDestructuringPolicy` (Serilog pipeline already installed in Program.cs).

## 7. Runbook pointers

- **03:00 Bagrut morning, students report login loop.** Check the rotation-race
  log signal: a spike in `SESSION_ROTATION_RACE` is ambiguous (could be a
  flaky network causing double-refresh in the SPA) and needs a
  front-end check (`useAuth.refresh()` must debounce). Interim mitigation:
  widen the refresh-race grace window from 0 to the already-configured
  `SessionJwt:RotationGraceSeconds` (default 2s) via config push, no
  redeploy.
- **Revocation list OOM.** `SessionRevocationList.Count` is the alert metric.
  Sweep interval is 5 min; if the count climbs over 100k the sweeper is
  falling behind. Scale horizontally or move to Redis (prr-011h).
- **Signing key rotation.** Today manual: set new `SessionJwt:SigningKey`,
  restart hosts. Existing cookies invalidate — users re-login. Follow-up
  prr-011d makes this graceful.

## 8. What this model does NOT claim

- The cookie protects against a compromised browser extension. It doesn't.
- The cookie protects against malicious insider tampering with the
  signing key. It doesn't — see §11 (prr-011d).
- The cookie protects against physical-access attacks on unlocked devices.
  It doesn't — logout UX and idle-timeout handle that (prr-011e).

## References

- prr-011 canonical task body — `/pre-release-review/reviews/tasks.jsonl`
- ADR-0041 — parent auth role, cookie pattern reused for parents
- ADR-0046 — this session cookie design (see `/docs/adr/`)
- `NoLocalStorageAuthTest` — frontend arch gate
- `NoBearerTokenEndpointsTest` — backend arch gate
- `NoJwtInLocalStorageResponseTest` — new in prr-011 Phase 1B; backend DTO gate
