# SEC-001: Firebase Auth — Email/Password + Google Sign-In, JWT Roles, Token Validation, Refresh

**Priority:** P0 — blocks all authenticated endpoints
**Blocked by:** INF-001 (VPC), INF-002 (RDS)
**Estimated effort:** 3 days
**Contract:** `contracts/frontend/graphql-schema.graphql` (role-based queries), `contracts/backend/grpc-protos.proto` (JWT claims)

---

## Context

Cena serves Israeli high school students (ages 16-18), teachers, and parents. Firebase Authentication provides the identity layer. All API access (GraphQL, SignalR WebSocket, gRPC) must validate Firebase ID tokens server-side. Roles (`STUDENT`, `TEACHER`, `PARENT`, `ADMIN`) are stored as custom claims. Token refresh must be seamless to avoid session interruption during 25-minute learning sessions.

## Subtasks

### SEC-001.1: Firebase Project Configuration + Auth Providers

**Files to create/modify:**
- `infra/terraform/modules/firebase/main.tf` — Firebase project with Auth enabled
- `config/firebase/auth-config.json` — provider configuration
- `src/Cena.Web/appsettings.json` — Firebase project ID, API key (non-secret)

**Acceptance:**
- [ ] Email/password provider enabled with email verification required
- [ ] Google Sign-In provider enabled with OAuth 2.0 client
- [ ] Password policy: minimum 8 characters, at least 1 uppercase, 1 number
- [ ] Account linking enabled (email+Google same user = merged)
- [ ] Multi-tenancy disabled (single Firebase project for all schools)
- [ ] Rate limiting: 100 sign-ups per hour per IP (Firebase default + custom Cloud Function enforcement)
- [ ] Email templates localized: Hebrew (primary), Arabic, English

**Test:**
```bash
# Verify Firebase project config
firebase auth:export --format=json /tmp/auth-test.json --project cena-prod
# Assert: providers include "password" and "google.com"

# Verify email/password signup
curl -X POST "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=${FIREBASE_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{"email":"test@cena.edu","password":"Test1234","returnSecureToken":true}'
# Assert: 200 OK, response contains idToken
```

**Edge cases:**
- User signs up with email, later tries Google with same email -> account linking flow, not duplicate
- Password reset during active session -> existing tokens remain valid until expiry (1 hour)
- Firebase Auth service degradation -> app shows offline mode, queues events locally

---

### SEC-001.2: Custom Claims — Role Assignment

**Files to create/modify:**
- `src/Cena.Functions/SetCustomClaims.cs` — Firebase Admin SDK Cloud Function
- `src/Cena.Web/Services/ClaimsService.cs` — server-side claims reader

**Acceptance:**
- [ ] Custom claims set on user creation: `{ role: "student", schoolId: "<uuid>", gradeLevel: <int> }`
- [ ] Teacher claim includes `classRoomIds: string[]` (max 10 classrooms)
- [ ] Parent claim includes `childIds: string[]` (max 5 children)
- [ ] Admin claim: `{ role: "admin", permissions: ["content_review", "user_management"] }`
- [ ] Claims propagate within 1 hour (Firebase limitation) — force refresh on role change
- [ ] Claims size < 1000 bytes (Firebase limit)
- [ ] Role assignment requires ADMIN caller or automated enrollment pipeline
- [ ] Claims cannot be set client-side (server-only via Admin SDK)

**Test:**
```csharp
[Fact]
public async Task SetCustomClaims_SetsStudentRole()
{
    var uid = await CreateTestFirebaseUser("student@test.com");
    await _claimsService.SetStudentClaims(uid, schoolId: "school-1", gradeLevel: 11);

    var user = await _firebaseAdmin.GetUserAsync(uid);
    var claims = user.CustomClaims;

    Assert.Equal("student", claims["role"]);
    Assert.Equal("school-1", claims["schoolId"]);
    Assert.Equal(11, (int)(long)claims["gradeLevel"]);
}

[Fact]
public async Task SetCustomClaims_RejectsOversizedPayload()
{
    var uid = await CreateTestFirebaseUser("teacher@test.com");
    var tooManyClassrooms = Enumerable.Range(0, 50).Select(i => Guid.NewGuid().ToString()).ToList();

    await Assert.ThrowsAsync<ArgumentException>(() =>
        _claimsService.SetTeacherClaims(uid, tooManyClassrooms));
}
```

**Edge cases:**
- Claims size approaches 1000 bytes with many classrooms -> reject and log
- Race condition: two admin calls setting claims simultaneously -> last write wins (Firebase behavior), log warning
- User exists in Firebase but not in Marten -> create Marten aggregate on first authenticated request

---

### SEC-001.3: Server-Side JWT Validation Middleware

**Files to create/modify:**
- `src/Cena.Web/Middleware/FirebaseAuthMiddleware.cs`
- `src/Cena.Web/Extensions/AuthenticationExtensions.cs`
- `src/Cena.Actors.Host/Program.cs` — register middleware

**Acceptance:**
- [ ] Every HTTP/GraphQL/SignalR request validates the `Authorization: Bearer <idToken>` header
- [ ] Validation uses Firebase Admin SDK `VerifyIdTokenAsync()` with `checkRevoked: true`
- [ ] Extracted claims mapped to `ClaimsPrincipal`: `sub` (Firebase UID), `role`, `schoolId`, `gradeLevel`
- [ ] Token signature verified against Google's public keys (auto-rotated by SDK)
- [ ] Token expiry checked: reject expired tokens with 401
- [ ] Revoked tokens rejected (check against Firebase revocation list)
- [ ] Invalid/malformed tokens -> 401 with JSON error body `{ "error": "invalid_token", "message": "..." }`
- [ ] `iss` must be `https://securetoken.google.com/<project-id>`
- [ ] `aud` must match the Firebase project ID
- [ ] Clock skew tolerance: 5 minutes (server-side)
- [ ] Anonymous requests allowed only on `/health/*` and `/auth/signup`
- [ ] SignalR WebSocket: token passed as query param `?access_token=<token>` on initial handshake

**Test:**
```csharp
[Fact]
public async Task Middleware_RejectsExpiredToken()
{
    var expiredToken = GenerateTestJwt(expiresAt: DateTime.UtcNow.AddHours(-1));
    _httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", expiredToken);

    var response = await _httpClient.GetAsync("/graphql");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

    var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
    Assert.Equal("invalid_token", body.Error);
}

[Fact]
public async Task Middleware_ExtractsCustomClaims()
{
    var token = await GetValidFirebaseToken("student@test.com");
    _httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);

    var response = await _httpClient.GetAsync("/api/me");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var profile = await response.Content.ReadFromJsonAsync<StudentProfile>();
    Assert.Equal("student", profile.Role);
}

[Fact]
public async Task Middleware_AllowsHealthCheckWithoutAuth()
{
    var response = await _httpClient.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

**Edge cases:**
- Google public key rotation during request -> SDK handles automatically via cache
- Firebase Auth outage -> cache valid public keys locally, degrade gracefully (allow valid cached tokens for 15 min)
- Token revoked mid-session -> next heartbeat fails auth, SignalR disconnects, client triggers re-auth
- Multiple simultaneous requests with same token -> no contention (stateless validation)

---

### SEC-001.4: Token Refresh — Seamless Client-Side Renewal

**Files to create/modify:**
- `src/mobile/lib/core/services/auth_service.dart` — Flutter auth service
- `src/mobile/lib/core/interceptors/auth_interceptor.dart` — HTTP interceptor
- `src/mobile/lib/core/services/websocket_service.dart` — SignalR reconnect with new token

**Acceptance:**
- [ ] Firebase ID token auto-refreshes 5 minutes before expiry (Firebase SDK default)
- [ ] HTTP interceptor attaches fresh token to every request
- [ ] If token refresh fails (offline) -> queue requests, retry on reconnection
- [ ] SignalR WebSocket: on token refresh, close and reconnect with new token within 2 seconds
- [ ] No visible interruption during 25-minute learning session (token expires every 60 min)
- [ ] Refresh token stored in secure storage (iOS Keychain / Android Keystore)
- [ ] Refresh token revocation on logout clears all local state
- [ ] Force refresh triggered on 401 response (exactly once, then fail to login screen)

**Test:**
```dart
testWidgets('Token refresh does not interrupt active session', (tester) async {
  final authService = MockAuthService();
  when(authService.currentToken).thenAnswer((_) async => 'new-token');

  await tester.pumpWidget(SessionScreen(authService: authService));
  await tester.pump(Duration(minutes: 55));

  expect(find.byType(QuestionCard), findsOneWidget);
  verify(authService.refreshToken()).called(greaterThanOrEqualTo(1));
});

testWidgets('401 triggers re-auth flow', (tester) async {
  final httpClient = MockHttpClient();
  when(httpClient.get(any)).thenAnswer((_) async =>
    Response('{"error":"invalid_token"}', 401));

  await tester.pumpWidget(App(httpClient: httpClient));
  await tester.pump();

  expect(find.byType(LoginScreen), findsOneWidget);
});
```

**Edge cases:**
- Device clock 30+ minutes ahead -> token appears expired client-side, force refresh
- Airplane mode for >24 hours -> refresh token may expire (Firebase: 30 days), show re-login
- Multiple tabs/windows -> share token via secure storage, avoid concurrent refresh races
- Firebase refresh endpoint rate limited -> exponential backoff (1s, 2s, 4s) up to 30s

---

## Integration Test (all subtasks combined)

```csharp
[Fact]
public async Task FullAuthFlow_SignupToAuthenticatedQuery()
{
    // 1. Sign up with email/password
    var uid = await SignUpWithEmail("student@bagrut.edu", "SecurePass1!");

    // 2. Set custom claims
    await _claimsService.SetStudentClaims(uid, schoolId: "school-1", gradeLevel: 11);

    // 3. Get ID token
    var token = await SignInAndGetToken("student@bagrut.edu", "SecurePass1!");

    // 4. Query GraphQL with token
    _httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", token);
    var response = await _httpClient.PostAsJsonAsync("/graphql", new {
        query = "{ myProfile { displayName gradeLevel } }"
    });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    // 5. Verify role-based access
    var forbiddenResponse = await _httpClient.PostAsJsonAsync("/graphql", new {
        query = "{ classOverview(classRoomId: \"some-id\") { name } }"
    });
    var body = await forbiddenResponse.Content.ReadAsStringAsync();
    Assert.Contains("FORBIDDEN", body);
}
```

## Rollback Criteria
If Firebase Auth introduces instability:
- Fall back to JWT-only validation without revocation checks (remove `checkRevoked: true`)
- Disable Google Sign-In, keep email/password only
- Acceptable temporary state: authentication works but role enforcement is coarse-grained

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=FirebaseAuth"` -> 0 failures
- [ ] Staging: full signup -> login -> authenticated GraphQL query -> token refresh cycle works
- [ ] Penetration test: expired/malformed/revoked tokens all return 401
- [ ] PR reviewed by architect
