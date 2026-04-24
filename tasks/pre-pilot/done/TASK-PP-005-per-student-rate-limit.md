# PP-005: Add Per-Student API Rate Limit (Tier 3)

- **Priority**: High — prevents individual abuse within school-level caps
- **Complexity**: Senior engineer — pattern already established in Tier 1/2
- **Source**: Expert panel review § Rate Limiting (Ran)

## Problem

`RateLimitDegradationMiddleware` in `src/actors/Cena.Actors/RateLimit/RateLimitDegradationMiddleware.cs` implements Tier 1 (photo: 10/hr per student), Tier 2 (classroom: 500/min per school), and Tier 4 (global cost circuit breaker). Tier 3 — per-student per-minute rate limit for non-photo API calls — is missing.

Currently a single student can send unlimited tutor requests within the school-level cap. This means:
- A student running an automated script could exhaust the school's 500/min budget alone
- A malfunctioning client that retry-loops could consume disproportionate LLM/CAS resources
- No per-student visibility in rate limit analytics

## Scope

### 1. Add Tier 3 check in middleware

Between the Tier 2 classroom check (line 66) and the Tier 4 cost breaker check (line 86), add:

```csharp
// Tier 3: Per-student request limit (60/min)
var studentResult = await _rateLimit.TryAcquireAsync(
    studentId, "student-api", capacity: 60, refillRatePerSecond: 1);
if (!studentResult.Allowed)
{
    _logger.LogWarning("Student rate limit exceeded for {StudentId}", studentId);
    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
    context.Response.Headers["Retry-After"] = "60";
    await context.Response.WriteAsJsonAsync(new
    {
        error = "Too many requests. Please slow down.",
        degraded = true,
        cooldownSeconds = 60
    });
    return;
}
```

### 2. Tune the limit

- 60 requests/minute is approximately 1 per second — sufficient for normal usage (a student submits an answer, gets a response, reads it, submits another)
- Photo endpoints are already separately limited at Tier 1, so Tier 3 only affects non-photo paths
- The frontend should implement exponential backoff when receiving 429

### 3. Frontend handling

Ensure the student Vue app handles 429 responses gracefully:
- Show a brief "Please wait a moment" message
- Disable the submit button for the `cooldownSeconds` duration
- Do not lose the student's typed input during the cooldown

## Files to Modify

- `src/actors/Cena.Actors/RateLimit/RateLimitDegradationMiddleware.cs` — add Tier 3 check
- `src/student/full-version/src/composables/useApiClient.ts` (or equivalent) — handle 429 with backoff
- `src/actors/Cena.Actors.Tests/RateLimit/RateLimitMiddlewareTests.cs` — NEW: test Tier 3 enforcement

## Acceptance Criteria

- [ ] Per-student rate limit of 60/min enforced in middleware
- [ ] Returns 429 with Retry-After header and JSON body
- [ ] Does not affect photo endpoints (already covered by Tier 1)
- [ ] Frontend handles 429 gracefully without losing user input
- [ ] Logged with student ID for observability
- [ ] Test verifies enforcement at boundary (request 60 passes, request 61 is rejected)
