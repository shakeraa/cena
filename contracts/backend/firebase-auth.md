# Cena Platform — Firebase Authentication Contract

**Layer:** Backend / Auth | **Runtime:** .NET 9 + Python 3.12 (FastAPI)
**Provider:** Firebase Authentication (Google Identity Platform)
**Status:** BLOCKER — no auth contract exists; all APIs are unprotected

---

## 1. Authentication Providers

| Provider | Use Case | Configuration |
|----------|----------|---------------|
| Email/Password | Primary signup for parents/teachers | `createUserWithEmailAndPassword` |
| Google Sign-In | Quick onboarding (teacher accounts) | OAuth 2.0, scopes: `email`, `profile` |

Future consideration: Apple Sign-In (iOS App Store requirement if Google is offered).

---

## 2. JWT Custom Claims

Custom claims are set server-side via Firebase Admin SDK on user creation and role change.

```json
{
  "role": "STUDENT | TEACHER | PARENT | MODERATOR | ADMIN | SUPER_ADMIN",
  "school_id": "sch_abc123",
  "student_ids": ["stu_001", "stu_002"],
  "locale": "he | ar | en",
  "plan": "free | premium",
  "iat": 1711450000,
  "exp": 1711453600
}
```

### Claim Semantics

| Claim | Type | Required | Description |
|-------|------|----------|-------------|
| `role` | enum | Yes | One of: `STUDENT`, `TEACHER`, `PARENT`, `MODERATOR`, `ADMIN`, `SUPER_ADMIN` |
| `school_id` | string | No | Set for teachers; links to school tenant |
| `student_ids` | string[] | No | Set for parents; list of linked student accounts |
| `locale` | string | Yes | UI language preference (he/ar/en) |
| `plan` | string | Yes | Subscription tier for feature gating |

### Role Permissions Matrix

| Resource | STUDENT | PARENT | TEACHER | MODERATOR | ADMIN | SUPER_ADMIN |
|----------|---------|--------|---------|-----------|-------|-------------|
| Own session data | RW | R (linked children) | R (class) | - | RW | RW |
| Knowledge graph | R | R | R | R | RW | RW |
| LLM tutoring | RW | - | - | - | RW | RW |
| Analytics dashboard | - | R (children) | R (class) | R (content) | RW | RW |
| Content moderation | - | - | - | RW | RW | RW |
| Question bank | - | - | R | RW | RW | RW |
| User management | - | - | - | - | RW (own org) | RW (all) |
| Billing / subscription | - | RW | - | - | RW | RW |
| Admin panel | - | - | - | R (content section) | RW (own org) | RW |
| System settings | - | - | - | - | - | RW |
| Audit log | - | - | - | - | R | RW |

---

## 3. Token Lifecycle

| Token | TTL | Storage | Refresh |
|-------|-----|---------|---------|
| Firebase ID Token (JWT) | 1 hour | In-memory (client) | Auto-refresh via Firebase SDK |
| Refresh Token | 7 days | Secure storage (Keychain / EncryptedSharedPreferences) | Exchanged for new ID token |
| Custom Token (server) | 1 hour | Never stored client-side | Used once during server-initiated auth |

### Token Refresh Flow

1. Firebase SDK auto-refreshes ID token 5 minutes before expiry.
2. If refresh token is expired (7 days), user must re-authenticate.
3. On re-auth failure, client navigates to login screen with `SESSION_EXPIRED` code.

---

## 4. .NET Token Validation Middleware

```
Pipeline: HTTP Request -> JwtBearerMiddleware -> ClaimsTransformer -> Controller/Hub
```

### Configuration

| Parameter | Value |
|-----------|-------|
| JWKS Endpoint | `https://www.googleapis.com/service_account/v1/metadata/x509/securetoken@system.gserviceaccount.com` |
| JWKS Cache TTL | 6 hours (Firebase rotates keys daily) |
| Issuer | `https://securetoken.google.com/{PROJECT_ID}` |
| Audience | `{FIREBASE_PROJECT_ID}` |
| Clock Skew | 30 seconds max |
| Algorithm | RS256 |

### Validation Steps (per request)

1. Extract `Authorization: Bearer {token}` header.
2. Decode JWT header; select matching key from cached JWKS by `kid`.
3. Verify RS256 signature against the JWKS public key.
4. Validate `iss` matches `https://securetoken.google.com/{PROJECT_ID}`.
5. Validate `aud` matches `{FIREBASE_PROJECT_ID}`.
6. Validate `exp` > now (with 30s clock skew tolerance).
7. Validate `iat` <= now.
8. Extract custom claims (`role`, `school_id`, `student_ids`, `locale`, `plan`).
9. Map claims to `ClaimsPrincipal` for .NET authorization policies.

### Authorization Policies (.NET)

```
Policy "StudentOnly"      -> RequireClaim("role", "STUDENT")
Policy "ParentOrAbove"    -> RequireClaim("role", ["PARENT", "TEACHER", "MODERATOR", "ADMIN", "SUPER_ADMIN"])
Policy "TeacherOrAbove"   -> RequireClaim("role", ["TEACHER", "MODERATOR", "ADMIN", "SUPER_ADMIN"])
Policy "ModeratorOrAbove" -> RequireClaim("role", ["MODERATOR", "ADMIN", "SUPER_ADMIN"])
Policy "AdminOnly"        -> RequireClaim("role", ["ADMIN", "SUPER_ADMIN"])
Policy "SuperAdminOnly"   -> RequireClaim("role", "SUPER_ADMIN")
Policy "OwnStudent"       -> Custom: token.student_ids contains route param {studentId}
```

---

## 5. SignalR WebSocket Authentication

SignalR does not support `Authorization` headers on WebSocket upgrade.

### Auth Flow

1. Client connects: `wss://api.cena.edu/hub?access_token={JWT}`
2. Server extracts token from query string in `OnConnectedAsync`.
3. Token validated using same JWKS pipeline as REST endpoints.
4. Connection associated with `userId` from token `sub` claim.
5. Token is validated **once per connection** (not per message).
6. On token expiry during active connection: client must disconnect, refresh token, reconnect.

### Connection Groups

| Group Pattern | Description |
|---------------|-------------|
| `student:{studentId}` | Student's personal channel |
| `class:{classId}` | Teacher's class broadcast |
| `parent:{parentId}` | Parent notification channel |

---

## 6. Python FastAPI Auth (LLM ACL Service)

The LLM ACL microservice validates the same Firebase JWT for internal gRPC calls
and any direct HTTP endpoints (health, admin).

### FastAPI Dependency

```
async def verify_firebase_token(token: str) -> FirebaseClaims:
    1. Cache JWKS from same Google endpoint (6-hour TTL).
    2. Decode + verify RS256 signature, iss, aud, exp.
    3. Return FirebaseClaims(uid, role, school_id, locale).
    4. Raise HTTPException(401) on any validation failure.
```

### Inter-Service Auth (gRPC)

- .NET actor cluster -> Python LLM ACL: mTLS (see grpc-mtls.md).
- Service-to-service calls include a `X-Service-Identity` header with the calling service name.
- The LLM ACL trusts calls authenticated via mTLS without requiring a user JWT.

---

## 7. Rate Limiting on Auth Endpoints

| Endpoint | Limit | Window | Key |
|----------|-------|--------|-----|
| `POST /auth/login` | 5 attempts | 1 minute | IP address |
| `POST /auth/register` | 3 attempts | 5 minutes | IP address |
| `POST /auth/reset-password` | 3 attempts | 15 minutes | Email address |
| `POST /auth/refresh` | 10 attempts | 1 minute | User ID |

### Lockout Policy

- After 5 failed login attempts: 15-minute lockout per IP.
- After 10 failed login attempts (cumulative): account locked, requires email verification.
- Rate limit headers returned: `X-RateLimit-Remaining`, `X-RateLimit-Reset`.

### Implementation

- Redis-backed sliding window counter.
- Key pattern: `ratelimit:auth:{endpoint}:{ip_or_uid}`.
- TTL matches the window duration.

---

## 8. Security Considerations

- Firebase ID tokens are **not revocable** mid-TTL (1 hour). For immediate revocation (e.g., account ban), maintain a server-side revocation list checked on each request.
- Custom claims propagate on next token refresh (up to 1 hour delay). Force refresh via `currentUser.getIdToken(true)` after role changes.
- All auth endpoints must be served over HTTPS only.
- CORS: allow only `https://app.cena.edu` and mobile deep link origins.
- CSP headers must prevent token exfiltration via XSS.
