# SEC-001: Firebase Authentication — JWT Validation, Roles, and Token Flow

**Priority:** P0 — blocks ALL authenticated API access
**Blocked by:** INF-001 (VPC), INF-008 (Firebase credentials in Secrets Manager)
**Estimated effort:** 2.5 days
**Contract:** `docs/api-contracts.md` (REST API Section 4), `contracts/REVIEW_security.md` (H-2, H-4)

---

## Context

Firebase Authentication is the identity provider for all Cena users: students (16-18 year old minors), parents, and teachers. The mobile app (React Native) and web app (React PWA) authenticate via Firebase SDK, which issues a JWT ID token. All backend services (.NET actor cluster, Python FastAPI LLM ACL, GraphQL API) must validate this JWT on every request.

The architecture defines four roles: `student`, `parent`, `teacher`, `admin`. Role claims are stored as Firebase custom claims set during onboarding. The security review (`contracts/REVIEW_security.md`) flagged that GraphQL resolvers accept raw `studentId` parameters without enforcement (C-4), and that SignalR WebSocket connections have no rate limiting (H-4). This task establishes the authentication foundation that SEC-002 builds upon.

---

## Subtasks

### SEC-001.1: Firebase Project Setup + Custom Claims Schema

**Files to create/modify:**
- `config/firebase/firebase.json` — Firebase project configuration
- `src/Cena.Infrastructure/Auth/FirebaseClaimsSchema.cs` — custom claims definition
- `src/Cena.Infrastructure/Auth/ClaimsConstants.cs` — claim key constants
- `functions/set-custom-claims/index.ts` — Firebase Cloud Function for setting claims on user creation

**Acceptance:**
- [ ] Firebase project `cena-staging` and `cena-prod` created (separate projects per environment)
- [ ] Authentication providers enabled: Email/Password, Google, Apple (Israel market requires Apple Sign-In for iOS)
- [ ] Custom claims schema:
  ```json
  {
    "role": "student | parent | teacher | admin",
    "school_id": "uuid | null",
    "student_ids": ["uuid"],  // For parent/teacher: which students they can access
    "locale": "he-IL | ar | en-US"
  }
  ```
- [ ] Custom claims set via Cloud Function trigger on `auth.user().onCreate` (not client-side)
- [ ] Default role on signup: `student` (elevated to `parent`/`teacher` via admin action or invite flow)
- [ ] Custom claims total size < 1000 bytes (Firebase limit)
- [ ] Claim `student_ids` for parent/teacher limited to 50 entries (prevents claim bloat)
- [ ] Firebase Admin SDK credentials stored in AWS Secrets Manager (`cena/auth/firebase`)

**Test:**
```typescript
// Firebase Cloud Function test
import { test } from 'firebase-functions-test';
const wrapped = test().wrap(setCustomClaims);

it('sets student role on new user creation', async () => {
  const user = test().auth.makeUserRecord('uid-123', { email: 'student@test.com' });
  await wrapped(user);

  const claims = await admin.auth().getUser('uid-123').then(u => u.customClaims);
  expect(claims.role).toBe('student');
  expect(claims.school_id).toBeNull();
  expect(claims.student_ids).toEqual([]);
});

it('rejects custom claims exceeding 1000 bytes', async () => {
  const bigClaims = { role: 'teacher', student_ids: Array(100).fill('a'.repeat(36)) };
  const size = JSON.stringify(bigClaims).length;
  expect(size).toBeGreaterThan(1000);
  // Function should truncate or reject
});
```

```bash
# Verify Firebase project exists
firebase projects:list | grep -q "cena-staging" && echo "PASS" || echo "FAIL"

# Verify authentication providers
firebase auth:export --project cena-staging /dev/null 2>&1 | grep -q "error" && echo "FAIL" || echo "PASS"
```

**Edge cases:**
- Firebase custom claims propagation delay (up to 1 hour) — client must force token refresh after claim change (`getIdToken(true)`)
- User signs up with email, then links Google account — claims must survive account linking
- Student turns 18 during usage — no COPPA/age-gate change needed (Israeli PPL treats 14+ as capable of consent)

---

### SEC-001.2: JWT Validation Middleware (.NET)

**Files to create/modify:**
- `src/Cena.Infrastructure/Auth/FirebaseJwtValidator.cs` — JWT validation logic
- `src/Cena.Infrastructure/Auth/FirebaseAuthMiddleware.cs` — ASP.NET Core middleware
- `src/Cena.Infrastructure/Auth/CenaIdentity.cs` — parsed identity object
- `src/Cena.Actors.Host/Program.cs` — register middleware

**Acceptance:**
- [ ] Firebase JWT validated on every HTTP request and SignalR connection
- [ ] Validation checks:
  1. Signature verification against Google's public keys (JWKS at `https://www.googleapis.com/robot/v1/metadata/x509/securetoken@system.gserviceaccount.com`)
  2. `iss` claim matches `https://securetoken.google.com/{firebase_project_id}`
  3. `aud` claim matches the Firebase project ID
  4. `exp` not expired (with 5-second clock skew tolerance)
  5. `sub` (user ID) is non-empty
  6. Token is not revoked (optional check against Firebase Auth API for sensitive operations)
- [ ] Parsed identity available throughout request:
  ```csharp
  public record CenaIdentity(
      string UserId,          // Firebase UID
      string Email,
      CenaRole Role,          // From custom claims
      string? SchoolId,       // From custom claims
      string[] StudentIds,    // From custom claims (parent/teacher)
      string Locale           // From custom claims
  );
  ```
- [ ] JWKS keys cached for 1 hour (configurable) with background refresh
- [ ] Unauthenticated requests return `401 Unauthorized` (not `403`)
- [ ] Expired token returns `401` with `WWW-Authenticate: Bearer error="invalid_token", error_description="token expired"`
- [ ] Health check endpoints (`/health/ready`, `/health/live`) exempt from auth

**Test:**
```csharp
[Fact]
public async Task ValidJwt_ParsesIdentityCorrectly()
{
    var token = CreateTestJwt(claims: new
    {
        sub = "firebase-uid-123",
        email = "student@test.com",
        role = "student",
        school_id = (string?)null,
        student_ids = Array.Empty<string>(),
        locale = "he-IL"
    });

    var identity = await _validator.ValidateAndParseAsync(token);

    Assert.Equal("firebase-uid-123", identity.UserId);
    Assert.Equal(CenaRole.Student, identity.Role);
    Assert.Equal("he-IL", identity.Locale);
}

[Fact]
public async Task ExpiredJwt_ReturnsUnauthorized()
{
    var token = CreateTestJwt(expiresAt: DateTime.UtcNow.AddMinutes(-5));
    var response = await _httpClient.SendAsync(
        new HttpRequestMessage(HttpMethod.Get, "/api/v1/profile")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        });
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task MissingToken_ReturnsUnauthorized()
{
    var response = await _httpClient.GetAsync("/api/v1/profile");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task HealthEndpoint_BypassesAuth()
{
    var response = await _httpClient.GetAsync("/health/ready");
    Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task WrongAudience_ReturnsUnauthorized()
{
    var token = CreateTestJwt(audience: "wrong-project-id");
    var identity = await Assert.ThrowsAsync<AuthenticationException>(
        () => _validator.ValidateAndParseAsync(token));
}
```

**Edge cases:**
- Google JWKS endpoint down — use cached keys; log WARNING; if cache expires and JWKS unreachable, fail open for health checks only, fail closed for all other requests
- Clock skew between Firebase and server — 5-second tolerance handles most cases; NTP must be running on ECS tasks
- Custom claims missing from token — user signed up before Cloud Function deployed; treat as `student` role with empty `student_ids`
- Token revocation check adds latency — only check on sensitive operations (role change, data export, account deletion), not on every request

---

### SEC-001.3: SignalR Authentication + WebSocket Rate Limiting

**Files to create/modify:**
- `src/Cena.Actors.Host/Hubs/CenaHub.cs` — SignalR hub with auth
- `src/Cena.Infrastructure/Auth/SignalRAuthHandler.cs` — WebSocket auth handler
- `src/Cena.Infrastructure/RateLimiting/WebSocketRateLimiter.cs` — per-student rate limiter

**Acceptance:**
- [ ] SignalR hub requires authentication (`[Authorize]` attribute)
- [ ] JWT passed via query string on WebSocket upgrade: `?access_token={jwt}` (standard SignalR pattern since WebSocket headers are not supported in browsers)
- [ ] `CenaIdentity` injected into hub context: `Context.User.FindFirst("sub")` available in all hub methods
- [ ] Student can only interact with their own actor — hub method `SubmitAnswer` routes to `StudentActor(Context.User.UserId)`, ignoring any client-provided `studentId`
- [ ] WebSocket rate limiting (fixes `contracts/REVIEW_security.md` H-4):
  - 30 commands/minute per student (Redis-backed sliding window)
  - Rate limit exceeded returns SignalR error: `{ "error": "rate_limit_exceeded", "retry_after_ms": 2000 }`
  - Rate limit counter resets per sliding window, not fixed window
- [ ] Connection limit: max 3 concurrent WebSocket connections per student (prevents multi-tab abuse)
- [ ] Idle timeout: disconnect after 30 minutes of no messages (free server resources)
- [ ] Reconnection: client auto-reconnects with fresh JWT (SignalR automatic reconnect)

**Test:**
```csharp
[Fact]
public async Task SignalR_RejectsUnauthenticatedConnection()
{
    var connection = new HubConnectionBuilder()
        .WithUrl("http://localhost:5000/hub/cena")  // No access_token
        .Build();

    await Assert.ThrowsAsync<HttpRequestException>(
        () => connection.StartAsync());
}

[Fact]
public async Task SignalR_AcceptsAuthenticatedConnection()
{
    var token = CreateTestJwt(sub: "student-123", role: "student");
    var connection = new HubConnectionBuilder()
        .WithUrl("http://localhost:5000/hub/cena",
            options => options.AccessTokenProvider = () => Task.FromResult(token))
        .Build();

    await connection.StartAsync();
    Assert.Equal(HubConnectionState.Connected, connection.State);
    await connection.StopAsync();
}

[Fact]
public async Task SignalR_RateLimitsExcessiveCommands()
{
    var token = CreateTestJwt(sub: "student-123");
    var connection = await CreateAuthenticatedConnection(token);

    // Send 35 commands rapidly (limit is 30/min)
    var errors = new List<string>();
    for (int i = 0; i < 35; i++)
    {
        try
        {
            await connection.InvokeAsync("SubmitAnswer", new { sessionId = "s1", questionId = "q1", answer = "42" });
        }
        catch (HubException ex) when (ex.Message.Contains("rate_limit_exceeded"))
        {
            errors.Add(ex.Message);
        }
    }

    Assert.True(errors.Count >= 5, $"Expected >=5 rate limit errors, got {errors.Count}");
}

[Fact]
public async Task SignalR_StudentCannotAccessOtherStudentActor()
{
    var token = CreateTestJwt(sub: "student-A");
    var connection = await CreateAuthenticatedConnection(token);

    // Even if malicious client sends another student's ID, hub routes to authenticated user's actor
    var result = await connection.InvokeAsync<object>("GetProfile", new { studentId = "student-B" });
    // The hub IGNORES the studentId parameter and uses Context.User.UserId
    // Result should be student-A's profile, not student-B's
}
```

**Edge cases:**
- JWT expires during WebSocket session — SignalR does not re-validate JWT on each message; implement periodic token refresh check (every 5 minutes) or on sensitive operations
- Rate limiter Redis unavailable — fail open (allow requests) but log CRITICAL alert; rate limiting is defense-in-depth, not sole protection
- Student opens 4+ tabs — 4th connection rejected; existing connections remain active; client shows "session active in another tab" message
- SignalR fallback to Server-Sent Events — auth still works (query string token); rate limiting still applies

---

### SEC-001.4: Python FastAPI JWT Validation (LLM ACL)

**Files to create/modify:**
- `services/llm-acl/app/auth/firebase_validator.py` — JWT validation for Python
- `services/llm-acl/app/auth/dependencies.py` — FastAPI dependency injection
- `services/llm-acl/app/middleware/auth_middleware.py` — request middleware
- `services/llm-acl/tests/test_auth.py`

**Acceptance:**
- [ ] gRPC requests from the actor cluster to LLM ACL are authenticated via service-to-service JWT (not Firebase user token)
- [ ] Service-to-service auth: actor cluster signs requests with a shared secret (HMAC-SHA256) or uses mTLS
- [ ] Alternative: if gRPC is internal-only (within VPC, no public endpoint), rely on VPC network isolation + security group rules as the auth boundary
- [ ] If external access is ever needed, Firebase Admin SDK verifies user tokens:
  ```python
  from firebase_admin import auth

  async def verify_firebase_token(token: str) -> dict:
      decoded = auth.verify_id_token(token)
      return {
          "uid": decoded["uid"],
          "role": decoded.get("role", "student"),
          "school_id": decoded.get("school_id"),
      }
  ```
- [ ] LLM ACL enforces per-student token budget (25,000 output tokens/day from `docs/architecture-design.md` Section 3.2.4)
- [ ] Budget check uses `student_id` from authenticated context, not from request body (prevents budget bypass)
- [ ] Health endpoint `/health` exempt from auth
- [ ] Firebase Admin SDK initialized with credentials from Secrets Manager (not environment variable)

**Test:**
```python
import pytest
from unittest.mock import patch, MagicMock

def test_valid_firebase_token_extracts_identity():
    with patch('firebase_admin.auth.verify_id_token') as mock_verify:
        mock_verify.return_value = {
            'uid': 'student-123',
            'role': 'student',
            'school_id': None,
            'email': 'test@example.com'
        }
        identity = verify_firebase_token('valid-token')
        assert identity['uid'] == 'student-123'
        assert identity['role'] == 'student'

def test_expired_token_raises_401():
    with patch('firebase_admin.auth.verify_id_token') as mock_verify:
        mock_verify.side_effect = auth.ExpiredIdTokenError('Token expired')
        with pytest.raises(HTTPException) as exc:
            verify_firebase_token('expired-token')
        assert exc.value.status_code == 401

def test_budget_check_uses_authenticated_student_id():
    """Budget must use the authenticated student_id, not the one in the request body."""
    with patch('app.auth.get_current_user') as mock_user:
        mock_user.return_value = {'uid': 'real-student-123'}
        request = AnswerEvaluationRequest(
            student_id='spoofed-student-456',  # Attacker's attempt
            concept_id='math_derivatives',
            student_answer='f(x) = 2x'
        )
        # The budget check should use 'real-student-123', not 'spoofed-student-456'
        budget_student = get_budget_student_id(request, mock_user.return_value)
        assert budget_student == 'real-student-123'

def test_health_endpoint_no_auth_required(client):
    response = client.get('/health')
    assert response.status_code == 200
```

```bash
# Verify gRPC is not exposed on public endpoint
# LLM ACL should only be reachable from within the VPC (sg-ecs-app)
nmap -p 50051 <llm-acl-private-ip> 2>/dev/null
# Should be reachable from within VPC

nmap -p 50051 <llm-acl-public-dns> 2>/dev/null
# Should be unreachable from outside VPC
```

**Edge cases:**
- Firebase Admin SDK initialization fails (bad credentials) — fail fast on startup, don't serve requests without auth
- gRPC internal auth bypass with network-level security — if an attacker compromises any ECS task in `sg-ecs-app`, they can call LLM ACL without auth; defense-in-depth recommends mTLS even internally
- Token budget race condition — two concurrent requests from same student both check budget before either deducts; use Redis atomic `DECRBY` for budget tracking
- Python `firebase-admin` SDK caches verification keys — ensure cache refreshes on Google key rotation

---

## Integration Test (all subtasks combined)

```csharp
[Fact]
public async Task FullAuthFlow_StudentLoginToActorInteraction()
{
    // 1. Simulate Firebase login (get test JWT)
    var token = await FirebaseTestHelper.CreateCustomToken("test-student-001", new
    {
        role = "student",
        locale = "he-IL"
    });
    var idToken = await FirebaseTestHelper.ExchangeForIdToken(token);

    // 2. REST API accepts the token
    _httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", idToken);
    var profileResponse = await _httpClient.GetAsync("/api/v1/profile");
    Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);

    // 3. SignalR accepts the token
    var hubConnection = new HubConnectionBuilder()
        .WithUrl("http://localhost:5000/hub/cena",
            options => options.AccessTokenProvider = () => Task.FromResult(idToken))
        .Build();
    await hubConnection.StartAsync();
    Assert.Equal(HubConnectionState.Connected, hubConnection.State);

    // 4. Hub routes to correct student actor
    var profile = await hubConnection.InvokeAsync<StudentProfile>("GetProfile");
    Assert.Equal("test-student-001", profile.StudentId);

    // 5. Rate limiting works
    var submitted = 0;
    for (int i = 0; i < 35; i++)
    {
        try
        {
            await hubConnection.InvokeAsync("SubmitAnswer", new { sessionId = "s1", questionId = "q1", answer = "42", responseTimeMs = 5000 });
            submitted++;
        }
        catch (HubException) { break; }
    }
    Assert.True(submitted <= 30, $"Rate limit should kick in at 30, but {submitted} succeeded");

    await hubConnection.StopAsync();
}
```

## Rollback Criteria

If this task fails or introduces instability:
- Disable auth middleware temporarily (staging only) by setting `CENA_AUTH_BYPASS=true` environment variable — allows development to continue
- Never bypass auth in production
- If Firebase project is misconfigured: create a new project; Firebase UIDs are project-scoped, so a fresh project means fresh users
- If rate limiting causes false positives: increase the limit to 100/min temporarily, investigate and tune

## Definition of Done

- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes end-to-end (login -> SignalR -> actor interaction)
- [ ] Firebase custom claims schema documented and enforced
- [ ] JWT validated on every HTTP and WebSocket request (except health checks)
- [ ] WebSocket rate limiting at 30 commands/minute per student
- [ ] Student cannot access another student's actor (hub enforces identity)
- [ ] LLM ACL budget check uses authenticated identity, not request body
- [ ] No tokens, secrets, or credentials in source code
- [ ] PR reviewed by architect and security reviewer
