# ADR-0046 — HttpOnly SameSite=Strict session cookie (prr-011)

- **Status**: Accepted
- **Date**: 2026-04-20
- **Decision Makers**: Shaker (project owner), persona-redteam (pre-release review)
- **Task**: prr-011 (Phase 1B backend hardening)
- **Related**: [ADR-0001](0001-multi-institute-enrollment.md), [ADR-0041](0041-parent-auth-role-age-bands.md), `docs/security/session-cookie-threat-model.md`

---

## Context

Cena's pre-release-review red-team lens (2026-04-20) rated "session JWT in
browser-accessible storage" as the platform's single highest-CVSS standing
risk before launch. Two code paths contributed:

- **Production**: `src/student/full-version/src/plugins/firebase.ts` relies
  on Firebase Auth's default persistence (IndexedDB), which is
  synchronously readable from JavaScript. Any XSS that clears our CSP —
  through a dependency compromise, a markdown-renderer bug, or a trusted-
  type slip — exfiltrates every student's session in one fetch.
- **Mock path**: `src/student/full-version/src/stores/authStore.ts:40`
  writes a mock token to `localStorage` behind a dev gate. A deployment-
  config mistake that leaves the gate true in prod was a real, recently-
  exploited failure mode at other edtech platforms.

Firebase Auth cannot itself emit an httpOnly cookie. Closing this class of
attack therefore requires a BFF (backend-for-frontend) seam that trades a
short-lived Firebase ID token for an httpOnly cookie carrying a server-
minted session token. This ADR locks the design of that seam.

The full threat model — STRIDE, DREAD, pre/post-mitigation, explicit
deferrals — lives at [`docs/security/session-cookie-threat-model.md`](../security/session-cookie-threat-model.md)
and is the authoritative source for what this cookie does and does not
protect against.

## Decision

### §1 — Cookie attributes are non-negotiable

Cookie name is **`__Host-cena_session`**. The `__Host-` prefix is browser-
enforced: cookies carrying it MUST have `Secure`, MUST have `Path=/`, and
MUST NOT carry `Domain`. This kills the subdomain-shadowing class
(T2 in the threat model) at the browser layer, not at the application
layer.

Attributes on every `Set-Cookie`:

| Attribute | Value | Why |
|---|---|---|
| name | `__Host-cena_session` | Browser-enforced anti-shadow |
| value | server-minted HS256 JWT | Self-contained; jti enables revocation |
| HttpOnly | true | Defeats `document.cookie` read-out |
| Secure | true | `__Host-` mandates it; also correct on principle |
| SameSite | `Strict` | Defeats CSRF on authenticated POSTs without a separate token |
| Path | `/` | `__Host-` mandates it |
| Domain | (omitted) | `__Host-` forbids it |
| Expires / Max-Age | 24h default, `SessionJwt:LifetimeHours` configurable | Balances convenience (one cookie per school day) vs. exfil exposure |
| IsEssential | true | Survives cookie-consent filtering |

Logout sends a deletion `Set-Cookie` with the same attributes (minus
`Expires` in the past) — browsers will only honour a deletion that matches
the original attribute set.

### §2 — Three-endpoint surface, terminal only

The BFF exposes exactly three endpoints, all at `/api/auth/session`:

- `POST /api/auth/session` — exchange. Accepts `Authorization: Bearer
  <Firebase ID token>` ONE TIME. Verifies the ID token via Firebase Admin
  SDK. Mints a server session JWT. Sets `__Host-cena_session`. Response
  body carries `{ userId, expiresAt }` — NEVER the JWT itself.
- `POST /api/auth/session/refresh` — rotates the session JWT in place.
  Reads the cookie, re-validates signature + expiry + revocation, mints a
  new jti, revokes the old jti, sets the new cookie. Rotation-race
  detection (see §4) is in this handler.
- `POST /api/auth/session/logout` — clears the cookie and marks the
  current jti revoked. Idempotent.

No other endpoint on the platform is allowed to read the `Authorization`
header. The `NoBearerTokenEndpointsTest` architecture gate enforces this
with a single-entry allowlist (`SessionExchangeEndpoint.cs`).

No endpoint anywhere is allowed to return the session JWT in a response
body. The new `NoJwtInLocalStorageResponseTest` architecture gate scans
auth DTOs for string properties named `access_token`, `id_token`, `jwt`,
`session_token`, `sessionJwt`, or `bearer` and fails CI on a match.

### §3 — Middleware precedence is fixed and asserted

Pipeline order for `Cena.Student.Api.Host` (exact, see `Program.cs`):

1. Correlation-ID middleware
2. Global exception handler
3. Concurrency-conflict middleware
4. Security response-headers middleware
5. CORS
6. `UseAuthentication()` — Firebase JwtBearer scheme (legacy path)
7. `CookieAuthMiddleware` — `__Host-cena_session` cookie
8. `UseAuthorization()`
9. `TokenRevocationMiddleware` — Redis UID revocation
10. Consent + FERPA audit + rate limiting
11. Terminal endpoints

`CookieAuthMiddleware` runs AFTER `UseAuthentication()` on purpose
during the transition window: if a request arrives with BOTH a Firebase
bearer AND a cookie, the bearer-scheme has already attached a principal
and the cookie middleware is a no-op. Once Scope A of prr-011 ships (front-
end stops sending `Authorization: Bearer` for non-exchange calls), the
bearer path shrinks to just the exchange endpoint. **Code comment
`// WHY: cookie runs after bearer for transition; single source of truth
post-migration is cookie`** lives in `CookieAuthMiddleware.InvokeAsync` so
the next reader does not have to derive this from first principles.

### §4 — Rotation-race detection

Refresh must be atomic under concurrent-rotation adversary conditions
(T4 in the threat model).

Mechanism:

1. Read the presented cookie's jti from the current session JWT.
2. Validate signature + expiry as usual.
3. If the jti is on `SessionRevocationList` AND a successor jti exists
   (tracked via an in-memory `jti → successor` map kept alongside the
   revocation list with the same lifetime), this is a rotation-race —
   either an attacker is replaying the pre-rotation cookie, or a double-
   refresh victim has raced themselves. Response:
   - Revoke the successor too (it is suspect).
   - Emit `SESSION_ROTATION_RACE` structured log.
   - 401 with a `CENA_AUTH_SESSION_RACE` error code.
   - Set-Cookie deletion.
4. If the jti is fresh and valid, mint a new jti, add old → new mapping,
   revoke the old jti (with the old exp as the revocation window — no
   shorter), set the new cookie, return 204.

A configurable rotation-grace window (`SessionJwt:RotationGraceSeconds`,
default 0) exists so an ops team can widen the window during a post-
incident stabilisation (e.g. fix a double-refresh SPA bug without logging
users out). In normal operation the grace is zero — exact rotation.

### §5 — In-memory revocation for Phase 1B

`SessionRevocationList` is an in-memory `ConcurrentDictionary<jti,
expiry>` with a 5-minute sweeper (`SessionRevocationListCleanupService`).
This is correct for the single-host pilot and wrong for horizontally-
scaled production. The interface is deliberately a single type so the
port to Redis (follow-up `prr-011h`) is a one-line DI swap — no consumer
re-wiring.

### §6 — HS256 signing, rotation is manual

The server session JWT is HS256-signed. The signing key comes from:

1. `SessionJwt:SigningKey` configuration (production path), OR
2. `CENA_SESSION_JWT_SIGNING_KEY` environment variable (container override),
3. Dev-only fallback derived from machine name (local dev only — a
   test-environment deploy without a real key still boots).

The key is SHA-256'd to produce a stable 32-byte HS256 input regardless of
input length. Rotating the key invalidates every outstanding cookie —
users re-login. Multi-host-graceful rotation (RS256 + JWKS) is follow-up
`prr-011d`; tracked explicitly in the threat model §5.

## Consequences

### Positive

- XSS session-theft class is closed at the browser layer. No dependency
  XSS, markdown-renderer XSS, or embed XSS can read the cookie.
- Subdomain shadowing is closed at the browser layer via `__Host-`.
- CSRF on authenticated POSTs is closed at the browser layer via
  SameSite=Strict.
- Refresh-rotation race is closed at the application layer via jti
  revocation + successor tracking.
- Logout is authoritative server-side — an exfil'd cookie is dead the
  moment the victim clicks logout.
- Every mutation is still attributable: the Firebase uid survives into
  the session JWT as `sub`, so ADR-0001 tenant scoping and ADR-0041
  parent authorisation continue to function.

### Negative

- The BFF is a new moving part. We now run ID-token verification in
  .NET as well as on the Firebase side. Follow-up `prr-011d` makes this
  easier to operate under key rotation.
- In-memory revocation is a one-host assumption. Must move to Redis
  before multi-pod deploy (`prr-011h`).
- Users on aggressive cookie-clearing browsers will log in more often.
  Acceptable; modern browsers default to retaining httpOnly session
  cookies across restarts.
- Front-end migration (stopping `Authorization: Bearer` on non-exchange
  calls, removing IndexedDB persistence) is a separate sibling task —
  prr-011 backend alone does not improve student-facing security until
  the sibling ships.

### Neutral

- The existing `TokenRevocationMiddleware` (Redis, keyed on Firebase
  uid) stays. It handles admin "ban this user" and runs alongside the
  in-memory jti revocation. The two lists answer different questions
  ("is this uid banned?" vs. "is this session invalidated?"); neither
  subsumes the other.

## References

- [prr-011 canonical task body](../../pre-release-review/reviews/tasks.jsonl)
- [docs/security/session-cookie-threat-model.md](../security/session-cookie-threat-model.md)
- [ADR-0041](0041-parent-auth-role-age-bands.md) — parent cookies reuse this design
- [ADR-0001](0001-multi-institute-enrollment.md) — tenant scoping survives through the cookie
- `src/api/Cena.Student.Api.Host/Auth/SessionExchangeEndpoint.cs`
- `src/api/Cena.Student.Api.Host/Auth/CookieAuthMiddleware.cs`
- `src/shared/Cena.Infrastructure/Auth/SessionRevocationList.cs`
- `src/actors/Cena.Actors.Tests/Architecture/NoLocalStorageAuthTest.cs`
- `src/actors/Cena.Actors.Tests/Architecture/NoBearerTokenEndpointsTest.cs`
- `src/actors/Cena.Actors.Tests/Architecture/NoJwtInLocalStorageResponseTest.cs`
- `src/actors/Cena.Actors.Tests/Auth/SessionExchangeTests.cs`
