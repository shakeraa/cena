# REV-004: Secure Diagnostic Endpoints & Add Security Headers

**Priority:** P0 -- CRITICAL (unauthenticated endpoints expose student IDs and can spawn actors)
**Blocked by:** None
**Blocks:** None (can be done independently)
**Estimated effort:** 4 hours
**Source:** System Review 2026-03-28 -- Cyber Officer 1 (Finding 2), Cyber Officer 2 (F-APP-04), Backend Senior (I7)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

Two Actor Host endpoints expose internal system state without authentication:

- `/api/actors/stats` -- returns active actor count, student IDs, session IDs, error messages
- `/api/actors/diag` -- returns cluster topology AND spawns a test actor (DoS vector)

Additionally, neither API host sets security response headers (CSP, HSTS, X-Frame-Options). Combined with the XSS findings (REV-006), the lack of CSP makes stored XSS fully exploitable.

## Architect's Decision

1. **Lock endpoints behind SuperAdminOnly** -- these are operational diagnostics, not general admin features
2. **Make `/api/actors/diag` non-destructive** -- remove the actor spawn test; replace with a read-only cluster health check
3. **Add security headers as middleware** -- apply globally on both hosts using `NetEscapades.AspNetCore.SecurityHeaders` or manual middleware
4. **Do NOT create a custom middleware** when a well-maintained NuGet package exists

## Subtasks

### REV-004.1: Secure Actor Host Diagnostic Endpoints

**File to modify:** `src/actors/Cena.Actors.Host/Program.cs`

**Changes at `/api/actors/stats` (line ~360):**
```csharp
// BEFORE
app.MapGet("/api/actors/stats", () => { ... });

// AFTER
app.MapGet("/api/actors/stats", () => { ... })
   .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);
```

**Changes at `/api/actors/diag` (line ~400):**
```csharp
// BEFORE: spawns a test actor (non-idempotent, no auth)
// AFTER: read-only cluster health check
app.MapGet("/api/actors/diag", (ActorSystem system) =>
{
    var cluster = system.Cluster();
    return Results.Ok(new
    {
        ClusterId = cluster.Config.ClusterName,
        MemberCount = cluster.MemberList.GetAllMembers().Length,
        Members = cluster.MemberList.GetAllMembers().Select(m => new
        {
            m.Address, m.Kinds, m.Id
        }),
        SystemId = system.Id,
        Address = system.Address,
    });
}).RequireAuthorization(CenaAuthPolicies.SuperAdminOnly);
```

**Acceptance:**
- [ ] `/api/actors/stats` returns 401 without valid JWT
- [ ] `/api/actors/stats` returns 403 for non-SUPER_ADMIN roles
- [ ] `/api/actors/diag` no longer spawns actors
- [ ] `/api/actors/diag` returns read-only cluster health info
- [ ] Both endpoints accessible to SUPER_ADMIN users

### REV-004.2: Add Security Response Headers to Both Hosts

**Files to modify:**
- `src/actors/Cena.Actors.Host/Program.cs`
- `src/api/Cena.Api.Host/Program.cs`

**Approach:** Use middleware (no external package dependency for simplicity):
```csharp
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["X-XSS-Protection"] = "0"; // Disabled per OWASP (modern browsers handle CSP)
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none';";

    if (!context.Request.Path.StartsWithSegments("/health"))
    {
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }

    await next();
});
```

**Acceptance:**
- [ ] All API responses include `X-Content-Type-Options: nosniff`
- [ ] All API responses include `X-Frame-Options: DENY`
- [ ] All API responses include `Content-Security-Policy` with `script-src 'self'`
- [ ] All API responses include `Strict-Transport-Security` (except health endpoints)
- [ ] Health check endpoints still return 200 without auth (readiness/liveness probes)

### REV-004.3: Remove AllowAnonymous from Admin System Health

**File to modify:** `src/api/Cena.Admin.Api/AdminApiEndpoints.cs` (line ~264)

```csharp
// BEFORE
}).AllowAnonymous().WithName("GetSystemHealth");

// AFTER -- if it exposes internal component details, require auth
// Move the public health check to a separate, minimal endpoint outside the admin group
}).WithName("GetSystemHealth");
// The parent group already has SuperAdminOnly, so removing AllowAnonymous inherits it
```

**Acceptance:**
- [ ] `/api/admin/system/health` requires SuperAdminOnly auth
- [ ] Public health check remains at `/health` (separate endpoint, minimal info)
