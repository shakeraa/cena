# BKD-001: Firebase JWT Auth Middleware

**Priority:** P0 — gates all admin API endpoints
**Blocked by:** None
**Estimated effort:** 2 days
**Stack:** .NET 9, ASP.NET Core Minimal API, Firebase Admin SDK
**Contract:** `contracts/backend/firebase-auth.md`

---

> **NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic.

## Context

The admin frontend authenticates via Firebase (client-side SDK). The .NET backend must validate Firebase ID tokens (JWTs) on every request, extract custom claims (role, school_id, locale, plan), and enforce authorization policies. The Cena.Actors.Host already runs ASP.NET Core — this task adds auth middleware to it.

## Subtasks

### BKD-001.1: JWT Bearer Authentication Setup

**Files to create/modify:**
- `src/actors/Cena.Actors/Infrastructure/Auth/FirebaseAuthExtensions.cs`
- `src/actors/Cena.Actors.Host/Program.cs` (add auth services)

**Implementation:**
```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var projectId = builder.Configuration["Firebase:ProjectId"];
        options.Authority = $"https://securetoken.google.com/{projectId}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{projectId}",
            ValidateAudience = true,
            ValidAudience = projectId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
```

**Acceptance:**
- [ ] Firebase ID tokens validated via JWKS (RS256, auto-rotating keys)
- [ ] Issuer = `https://securetoken.google.com/{PROJECT_ID}`
- [ ] Audience = `{FIREBASE_PROJECT_ID}`
- [ ] Clock skew tolerance: 30 seconds
- [ ] JWKS cached for 6 hours (default HttpClient caching)
- [ ] Configuration reads `Firebase:ProjectId` from `appsettings.json`

### BKD-001.2: Claims Transformer

**Files to create:**
- `src/actors/Cena.Actors/Infrastructure/Auth/CenaClaimsTransformer.cs`

**Implementation:**
- Register as `IClaimsTransformation`
- Extract Firebase custom claims: `role`, `school_id`, `student_ids`, `locale`, `plan`
- Map to .NET `ClaimsPrincipal` with typed claim names
- Add role claim as `ClaimTypes.Role` for policy-based auth

**Acceptance:**
- [ ] `HttpContext.User.FindFirst("role")` returns the Firebase custom claim
- [ ] `HttpContext.User.IsInRole("SUPER_ADMIN")` works for policy checks
- [ ] Missing claims default safely: role → null (401), locale → "en"

### BKD-001.3: Authorization Policies

**Files to create:**
- `src/actors/Cena.Actors/Infrastructure/Auth/CenaAuthPolicies.cs`

**Policies:**

| Policy Name | Requirement |
|-------------|-------------|
| `ModeratorOrAbove` | role ∈ {MODERATOR, ADMIN, SUPER_ADMIN} |
| `AdminOnly` | role ∈ {ADMIN, SUPER_ADMIN} |
| `SuperAdminOnly` | role = SUPER_ADMIN |
| `SameOrg` | token.school_id matches route {orgId} or role = SUPER_ADMIN |

**Acceptance:**
- [ ] Policies registered in DI via `builder.Services.AddAuthorization()`
- [ ] All `/api/admin/*` endpoints require `ModeratorOrAbove` by default
- [ ] User management requires `AdminOnly`
- [ ] System settings require `SuperAdminOnly`
- [ ] `SameOrg` policy checks school_id claim against route parameter

### BKD-001.4: CORS Configuration

**Files to modify:**
- `src/actors/Cena.Actors.Host/Program.cs`

**Acceptance:**
- [ ] Allow origin: `http://localhost:5174` (dev), `https://admin.cena.edu` (prod)
- [ ] Allow headers: Authorization, Content-Type
- [ ] Allow methods: GET, POST, PUT, DELETE, OPTIONS
- [ ] Credentials: true (for cookie-based auth fallback)
- [ ] CORS applied before auth middleware in pipeline

### BKD-001.5: Token Revocation Check (Optional)

**Files to create:**
- `src/actors/Cena.Actors/Infrastructure/Auth/TokenRevocationMiddleware.cs`

**Acceptance:**
- [ ] Redis-backed revocation list checked per request
- [ ] Key: `revoked:{uid}` with TTL = 1 hour (Firebase token TTL)
- [ ] Banning a user sets the revocation key → immediate 401 on next request
- [ ] Bypass for health check endpoints

## Configuration

```json
// appsettings.json
{
  "Firebase": {
    "ProjectId": "cena-platform"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5174"]
  }
}
```

## Test

- [ ] Request without token → 401
- [ ] Request with expired token → 401
- [ ] Request with valid STUDENT token to admin endpoint → 403
- [ ] Request with valid ADMIN token → 200 + claims accessible in handler
- [ ] CORS preflight from localhost:5174 → 200 with correct headers
- [ ] Wrong audience/issuer → 401
