# SEC-009: Firebase Authentication — End-to-End Identity

**Priority:** P0 — BLOCKER (no auth = no production)
**Blocked by:** None (greenfield)
**Blocks:** Every API endpoint, SignalR hub, gRPC ACL, NATS publisher
**Estimated effort:** 5 days
**Contract:** `contracts/frontend/signalr-messages.ts` (CenaHubProxy.start), `contracts/backend/grpc-protos.proto` (TokenBudget.anonymized_student_id)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Firebase Auth is the single identity provider for all Cena clients (React Native iOS/Android, React PWA). Every downstream service (.NET SignalR hub, Python FastAPI LLM ACL, NATS publishers) must validate Firebase JWT tokens independently. Custom claims carry role (`student`, `teacher`, `parent`, `admin`) and optional `schoolId` for tenant isolation. Without this, the platform has zero access control.

## Subtasks

### SEC-009.1: Firebase Project Setup + Custom Claims
**Files:**
- `src/Cena.Infrastructure/Auth/FirebaseClaimsService.cs` — custom claims management
- `src/Cena.Infrastructure/Auth/FirebaseAuthConfig.cs` — configuration
- `scripts/firebase-seed-claims.ts` — admin script to set claims on test users

**Acceptance:**
- [ ] Firebase project configured with Email/Password, Google, and Apple sign-in providers
- [ ] Custom claims schema: `{ role: "student" | "teacher" | "parent" | "admin", schoolId?: string, tier?: "free" | "premium" }`
- [ ] `SetCustomClaimsAsync(uid, claims)` callable from admin endpoints only
- [ ] Claims propagated within 1 hour (Firebase default) or forced refresh via `revokeRefreshTokens`
- [ ] Claims size under 1000 bytes (Firebase limit)
- [ ] Test users seeded: `test-student@cena.app`, `test-teacher@cena.app`, `test-admin@cena.app`

**Test:**
```csharp
[Fact]
public async Task SetCustomClaims_SetsRoleAndSchool()
{
    var service = new FirebaseClaimsService(_firebaseAdmin);
    await service.SetCustomClaimsAsync("uid-1", new CenaClaims
    {
        Role = "student",
        SchoolId = "school-42",
        Tier = "premium"
    });

    var user = await _firebaseAdmin.GetUserAsync("uid-1");
    var claims = user.CustomClaims;
    Assert.Equal("student", claims["role"]);
    Assert.Equal("school-42", claims["schoolId"]);
    Assert.Equal("premium", claims["tier"]);
}

[Fact]
public async Task SetCustomClaims_RejectsInvalidRole()
{
    await Assert.ThrowsAsync<ArgumentException>(() =>
        _service.SetCustomClaimsAsync("uid-2", new CenaClaims { Role = "superadmin" }));
}

[Fact]
public async Task SetCustomClaims_EnforcesMaxSize()
{
    var largeClaims = new CenaClaims
    {
        Role = "student",
        SchoolId = new string('x', 950) // Exceeds 1000 bytes
    };
    await Assert.ThrowsAsync<ClaimsSizeExceededException>(() =>
        _service.SetCustomClaimsAsync("uid-3", largeClaims));
}
```

---

### SEC-009.2: .NET JWT Middleware (ASP.NET Core + Proto.Actor Silo)
**Files:**
- `src/Cena.Api/Auth/FirebaseJwtMiddleware.cs` — JWT validation middleware
- `src/Cena.Api/Auth/CenaAuthorizationPolicies.cs` — role-based policies
- `src/Cena.Api/Auth/ClaimsPrincipalExtensions.cs` — helper extensions

**Acceptance:**
- [ ] Firebase JWT validated on every HTTP/SignalR request using Google's public keys (JWKS)
- [ ] JWKS cached with 6-hour TTL, auto-refresh on cache miss
- [ ] Token `iss` validated: `https://securetoken.google.com/{PROJECT_ID}`
- [ ] Token `aud` validated: matches `FIREBASE_PROJECT_ID` from config
- [ ] Token `exp` validated: reject if expired (clock skew tolerance: 5 minutes)
- [ ] Custom claims extracted into `ClaimsPrincipal`: `role`, `schoolId`, `tier`
- [ ] Authorization policies registered: `RequireStudent`, `RequireTeacher`, `RequireAdmin`, `RequireSchoolMember(schoolId)`
- [ ] 401 returned for missing/invalid/expired tokens with machine-readable `SignalRErrorCode.UNAUTHORIZED`
- [ ] Rate limiting: 100 requests/minute per user (429 on exceed)

**Test:**
```csharp
[Fact]
public async Task ValidFirebaseToken_ExtractsClaimsCorrectly()
{
    var token = GenerateTestFirebaseJwt(
        uid: "student-1",
        claims: new { role = "student", schoolId = "school-42", tier = "premium" }
    );

    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await client.GetAsync("/api/profile");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public async Task ExpiredToken_Returns401()
{
    var token = GenerateTestFirebaseJwt(uid: "student-2", expiresAt: DateTime.UtcNow.AddMinutes(-10));

    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await client.GetAsync("/api/profile");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

    var body = await response.Content.ReadFromJsonAsync<ErrorPayload>();
    Assert.Equal("UNAUTHORIZED", body.Code);
}

[Fact]
public async Task MissingToken_Returns401()
{
    var client = _factory.CreateClient();
    var response = await client.GetAsync("/api/profile");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task StudentToken_CannotAccessTeacherEndpoint()
{
    var token = GenerateTestFirebaseJwt(uid: "student-3", claims: new { role = "student" });

    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await client.GetAsync("/api/teacher/dashboard");
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task TeacherToken_CanAccessTeacherEndpoint()
{
    var token = GenerateTestFirebaseJwt(uid: "teacher-1", claims: new { role = "teacher", schoolId = "school-42" });

    var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await client.GetAsync("/api/teacher/dashboard");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

**Edge cases:**
- JWKS endpoint unreachable → use cached keys, log WARNING; if cache also empty → 503
- Token signed by revoked key → reject, log ALERT (possible compromise)
- Clock skew > 5 minutes → reject, log the skew delta for diagnostics

---

### SEC-009.3: SignalR WebSocket Authentication
**Files:**
- `src/Cena.Api/Hubs/LearningSessionHub.cs` — hub with auth
- `src/Cena.Api/Auth/SignalRTokenProvider.cs` — token extraction from query string

**Acceptance:**
- [ ] SignalR hub requires `[Authorize(Policy = "RequireStudent")]`
- [ ] Token passed via query string: `?access_token={jwt}` (WebSocket limitation — no headers)
- [ ] Token extracted in `OnConnectedAsync`, validated same as HTTP middleware
- [ ] Connection rejected with `HubException("UNAUTHORIZED")` on invalid token
- [ ] User identity (`ClaimsPrincipal`) available in all hub methods via `Context.User`
- [ ] `Context.UserIdentifier` set to Firebase UID for targeted sends
- [ ] Connection auto-closes on token expiry (checked every 5 minutes via background timer)
- [ ] Reconnection requires fresh token (per `ReconnectionStrategy` in signalr-messages.ts)

**Test:**
```csharp
[Fact]
public async Task SignalR_AuthenticatedConnection_Succeeds()
{
    var token = GenerateTestFirebaseJwt(uid: "student-ws-1", claims: new { role = "student" });
    var connection = new HubConnectionBuilder()
        .WithUrl($"{_server}/hub/learning?access_token={token}")
        .Build();

    await connection.StartAsync();
    Assert.Equal(HubConnectionState.Connected, connection.State);
    await connection.StopAsync();
}

[Fact]
public async Task SignalR_InvalidToken_ConnectionRejected()
{
    var connection = new HubConnectionBuilder()
        .WithUrl($"{_server}/hub/learning?access_token=invalid-jwt")
        .Build();

    await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
}

[Fact]
public async Task SignalR_NoToken_ConnectionRejected()
{
    var connection = new HubConnectionBuilder()
        .WithUrl($"{_server}/hub/learning")
        .Build();

    await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
}

[Fact]
public async Task SignalR_ExpiredToken_DisconnectedGracefully()
{
    // Token expires in 10 seconds (test override)
    var token = GenerateTestFirebaseJwt(
        uid: "student-ws-2",
        claims: new { role = "student" },
        expiresAt: DateTime.UtcNow.AddSeconds(10)
    );
    var connection = new HubConnectionBuilder()
        .WithUrl($"{_server}/hub/learning?access_token={token}")
        .Build();

    await connection.StartAsync();
    Assert.Equal(HubConnectionState.Connected, connection.State);

    // Wait for expiry check cycle
    await Task.Delay(TimeSpan.FromSeconds(15));
    Assert.Equal(HubConnectionState.Disconnected, connection.State);
}
```

---

### SEC-009.4: Python FastAPI Authentication (LLM ACL)
**Files:**
- `src/cena_llm/auth/firebase_auth.py` — FastAPI dependency for Firebase JWT validation
- `src/cena_llm/auth/middleware.py` — ASGI middleware for token extraction
- `src/cena_llm/auth/models.py` — Pydantic models for auth claims

**Acceptance:**
- [ ] Firebase JWT validated using `google-auth` library with Google's public keys
- [ ] JWKS cached with `cachetools.TTLCache(maxsize=10, ttl=21600)` (6 hours)
- [ ] FastAPI dependency `get_current_user() -> CenaUser` injects authenticated user
- [ ] gRPC interceptor validates JWT from metadata key `authorization`
- [ ] `CenaUser` model: `uid: str, role: str, school_id: Optional[str], tier: str`
- [ ] `anonymized_student_id` computed as `sha256(uid)[:16]` for LLM calls (matches `TokenBudget.anonymized_student_id` in grpc-protos.proto)
- [ ] 401 JSON response: `{"error": "UNAUTHORIZED", "detail": "Token expired"}`
- [ ] Service-to-service calls authenticated via separate JWT (issued by .NET silo, short-lived 5-minute tokens)

**Test:**
```python
@pytest.mark.asyncio
async def test_valid_firebase_token_extracts_user(app_client, valid_firebase_token):
    response = await app_client.get(
        "/api/v1/health",
        headers={"Authorization": f"Bearer {valid_firebase_token}"}
    )
    assert response.status_code == 200

@pytest.mark.asyncio
async def test_expired_token_returns_401(app_client, expired_firebase_token):
    response = await app_client.get(
        "/api/v1/health",
        headers={"Authorization": f"Bearer {expired_firebase_token}"}
    )
    assert response.status_code == 401
    body = response.json()
    assert body["error"] == "UNAUTHORIZED"

@pytest.mark.asyncio
async def test_missing_token_returns_401(app_client):
    response = await app_client.get("/api/v1/health")
    assert response.status_code == 401

@pytest.mark.asyncio
async def test_student_role_cannot_access_admin_endpoint(app_client, student_firebase_token):
    response = await app_client.post(
        "/api/v1/admin/retrain",
        headers={"Authorization": f"Bearer {student_firebase_token}"}
    )
    assert response.status_code == 403

@pytest.mark.asyncio
async def test_anonymized_id_is_deterministic(app_client, valid_firebase_token):
    """The anonymized_student_id in TokenBudget must be stable across requests."""
    r1 = await app_client.get(
        "/api/v1/budget",
        headers={"Authorization": f"Bearer {valid_firebase_token}"}
    )
    r2 = await app_client.get(
        "/api/v1/budget",
        headers={"Authorization": f"Bearer {valid_firebase_token}"}
    )
    assert r1.json()["anonymized_student_id"] == r2.json()["anonymized_student_id"]
```

---

## Integration Test (full auth flow)

```csharp
[Fact]
public async Task FullAuthFlow_Login_SignalR_gRPC()
{
    // 1. Authenticate with Firebase (emulator)
    var firebaseToken = await _firebaseEmulator.SignInWithEmailPassword(
        "test-student@cena.app", "test-password-123"
    );
    Assert.NotNull(firebaseToken.IdToken);

    // 2. Connect SignalR with the token
    var hubConnection = new HubConnectionBuilder()
        .WithUrl($"{_server}/hub/learning?access_token={firebaseToken.IdToken}")
        .Build();
    await hubConnection.StartAsync();
    Assert.Equal(HubConnectionState.Connected, hubConnection.State);

    // 3. Start a session (requires authenticated identity)
    var sessionStarted = new TaskCompletionSource<SessionStartedPayload>();
    hubConnection.On<SessionStartedPayload>("SessionStarted", p => sessionStarted.SetResult(p));

    await hubConnection.InvokeAsync("StartSession", new StartSessionPayload
    {
        SubjectId = "math",
        ConceptId = null,
        Device = new DeviceContext { Platform = "web", ScreenWidth = 1024, ScreenHeight = 768, Locale = "he-IL" }
    });

    var session = await sessionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Assert.NotNull(session.SessionId);

    // 4. Verify Python ACL also accepts the same token (gRPC)
    var grpcChannel = GrpcChannel.ForAddress(_aclServer);
    var client = new SocraticTutorService.SocraticTutorServiceClient(grpcChannel);
    var metadata = new Grpc.Core.Metadata { { "authorization", $"Bearer {firebaseToken.IdToken}" } };

    // This should succeed without UNAUTHENTICATED error
    var response = await client.GenerateSocraticQuestionAsync(
        new SocraticQuestionRequest { ConceptId = "algebra-1", Language = ContentLanguage.Hebrew },
        metadata
    );
    Assert.NotNull(response.QuestionText);

    await hubConnection.StopAsync();
}
```

## Edge Cases
- Firebase emulator diverges from production → use real Firebase in staging CI
- Token refresh race: client refreshes while mid-request → server accepts both old and new token within 5-minute overlap
- Multiple devices: same student authenticated on phone and web → both SignalR connections valid, events deduplicated by `correlationId`
- Revoked user: admin revokes access → all active SignalR connections closed within 5 minutes (next expiry check cycle)

## Rollback Criteria
- If Firebase Auth causes >5% login failure rate: fall back to anonymous sessions with rate-limited access
- If JWKS fetch latency >2s: increase cache TTL to 24 hours
- If Python JWT validation adds >50ms P95: switch to pre-validated service-to-service tokens for internal gRPC calls

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes end-to-end
- [ ] `dotnet test --filter "Category=FirebaseAuth"` → 0 failures
- [ ] `pytest tests/auth/ -v` → 0 failures
- [ ] No secrets in source code (verified via `gitleaks detect`)
- [ ] Firebase project ID loaded from environment variable, not hardcoded
- [ ] Login latency P95 < 500ms (Firebase round-trip + token validation)
- [ ] SignalR reconnection with fresh token succeeds within 3 seconds
- [ ] PR reviewed by architect (you)
