# Iteration 05: Rate Limiting Patterns for Image Upload APIs

**Date:** 2026-04-12
**Series:** Student Screenshot Question Analyzer -- Defense-in-Depth Security
**Iteration:** 5 of 10
**Focus:** Rate limiting and abuse prevention for the student photo upload endpoint
**Security Score Contribution:** 15 / 100 points

---

## Executive Summary

The Cena screenshot analyzer lets students photograph math/physics questions and send them to Gemini 2.5 Flash for OCR extraction at ~$0.002 per image. Without multi-tier rate limiting, a single bot loop can burn $2,000 in hours. This article analyzes five abuse scenarios, compares six rate limiting algorithms, presents a four-tier defense architecture, provides production ASP.NET implementation code that integrates with Cena's existing rate limiting middleware, and covers client-side UX, monitoring, anti-bot measures, and cost protection math.

**Cena-specific context:** The Student API Host already has seven rate limiting policies registered via `AddRateLimiter()` in `Program.cs` (api, ai, tutor, password-reset, gdpr-export, gdpr-erasure, tutor-global, tutor-tenant). The screenshot upload endpoint needs its own dedicated multi-tier policy that combines per-student token bucket, per-institute sliding window, per-session fixed window, and a global cost-based circuit breaker. The existing `AiTokenBudgetService` (Redis-backed, FIND-sec-015) provides the cost tracking foundation but does not cover image-specific abuse vectors like duplicate flooding or credential sharing.

---

## 1. Abuse Scenarios

### 1.1 Single Student Flooding (Upload Loop)

**Attack:** A student (or script using a student's credentials) uploads images in a tight loop. This is the most common abuse pattern -- a curiosity-driven student writing a `while true` loop, or a malicious actor running a bot.

**Impact:**
- **Cost:** At $0.002/image, 1,000 images/minute = $2/minute = $120/hour = $2,880/day.
- **Latency:** Gemini API queuing degrades response time for all other students.
- **Quota exhaustion:** Gemini 2.5 Flash has per-project RPM limits. One student can exhaust the entire project's quota.

**Detection signals:**
- More than 5 uploads in 60 seconds from a single student ID.
- More than 30 uploads in any rolling hour.
- Upload cadence faster than human capability (~2 seconds between photos including camera focus time).

**Realistic baseline:** A diligent student working through a problem set photographs approximately 1 question every 2-3 minutes. Peak burst: 3 questions in rapid succession when starting a session. No legitimate student uploads 5 photos in 60 seconds sustained.

### 1.2 Distributed Attack Across Accounts

**Attack:** An attacker creates or compromises multiple student accounts and distributes upload requests across them to stay under per-student limits.

**Impact:**
- Per-student rate limits are bypassed.
- Total cost scales linearly with the number of compromised accounts.
- 50 accounts at 5 images/minute each = 250 images/minute = $0.50/minute = $30/hour.

**Detection signals:**
- Spike in total upload volume across the institute.
- Multiple accounts uploading from the same IP address or device fingerprint.
- Accounts created recently (< 24h) that immediately start uploading.
- Upload patterns that are suspiciously uniform (identical inter-request intervals).

### 1.3 Cost Amplification

**Attack:** Each uploaded image triggers a Gemini API call (~$0.002), CAS validation pipeline (MathNet + SymPy sidecar), and potentially a follow-up tutoring LLM call (Claude, ~$0.003-0.015). A single upload can cascade into $0.02+ of downstream processing.

**Impact:**
- Amplification factor of ~10x: $0.002 image cost triggers $0.02 total pipeline cost.
- 100,000 images = $2,000 in direct Gemini costs + $15,000-$20,000 in downstream LLM tutoring if each triggers a follow-up.
- The existing `AiTokenBudgetService` caps tutor tokens but does NOT cap Gemini vision calls.

**Detection signals:**
- Global Gemini API spend exceeding daily budget projections.
- Ratio of uploads-to-tutoring-sessions deviating from historical baseline (normally ~60% of uploads lead to a tutoring interaction).

### 1.4 Scraping / Data Harvesting via the Upload Endpoint

**Attack:** An attacker uploads images not to get tutoring help but to extract the OCR pipeline's structured output (LaTeX expressions, question metadata) for building a competing question bank.

**Impact:**
- Intellectual property theft: Cena's structured question format and metadata schema exposed.
- Exam content leakage: if students upload real exam papers, the structured output constitutes an answer key.
- Compliance risk: FERPA/GDPR implications if scraped data includes student-identifiable metadata.

**Detection signals:**
- Student downloads structured responses but never enters a tutoring session.
- Bulk uploads of already-digital images (screenshots of PDFs rather than photos of paper).
- Response bodies are consumed programmatically (no subsequent UI interaction events via SignalR).

### 1.5 Account Sharing / Credential Stuffing

**Attack:** Multiple people share a single student account, or stolen credentials from data breaches are used to create sessions and upload images.

**Impact:**
- Rate limits per-student are consumed by unauthorized users.
- Legitimate student is locked out of their own quota.
- Compliance violation: COPPA/FERPA require individual student identity tracking.

**Detection signals:**
- Concurrent sessions from geographically distant IP addresses ("impossible travel").
- Device fingerprint diversity: > 3 distinct devices per student within 24 hours.
- Login patterns at unusual hours for the student's timezone.
- Credential stuffing: high rate of failed login attempts followed by successful ones across many accounts.

**Educational platform reference:** The e-learning platform Baims discovered one account shared among 47 users; Sketchy converted 20% of account sharers to paying customers by implementing device limits. For Cena, the concern is less about revenue and more about per-student rate limit fairness and FERPA compliance.

---

## 2. Rate Limiting Algorithms Comparison

### 2.1 Fixed Window

**Mechanism:** Divide time into fixed intervals (e.g., 1-minute windows). Count requests per window. Reject when count exceeds limit.

| Attribute | Value |
|---|---|
| Memory | O(1) per key -- single counter |
| Accuracy | Low -- burst at window boundary allows 2x limit |
| Complexity | Trivial |
| Distributed | Easy -- single atomic increment |

**Boundary burst problem:** A student can upload 5 images at 0:59 and 5 more at 1:01, achieving 10 uploads in 2 seconds while respecting a "5 per minute" limit. This is the primary weakness.

**Cena usage:** Already used for the `api`, `ai`, `tutor`, `password-reset`, `gdpr-export`, `gdpr-erasure` policies in `Program.cs`. Adequate for those endpoints but insufficient for cost-sensitive image uploads.

### 2.2 Sliding Window Log

**Mechanism:** Store a timestamp for every request. On each new request, discard timestamps older than the window, count remaining. Reject if count exceeds limit.

| Attribute | Value |
|---|---|
| Memory | O(N) per key -- one entry per request |
| Accuracy | Perfect -- no boundary issues |
| Complexity | Moderate |
| Distributed | Redis sorted sets (ZADD/ZREMRANGEBYSCORE/ZCARD) |

**Cena usage:** SEC-007 originally specified this approach using Redis sorted sets with Lua scripts for atomicity. Memory cost is acceptable for the upload endpoint because volumes are inherently limited (students cannot physically take photos faster than ~1/second).

### 2.3 Sliding Window Counter

**Mechanism:** Hybrid of fixed window and sliding window. Maintain counters for the current and previous windows. Interpolate based on the position within the current window.

| Attribute | Value |
|---|---|
| Memory | O(1) per key -- two counters |
| Accuracy | High -- approximate but no boundary burst |
| Complexity | Low |
| Distributed | Two atomic counters |

**Formula:** `estimated_count = prev_window_count * ((window_size - elapsed) / window_size) + current_window_count`

**Cena usage:** The `tutor-tenant` policy already uses `AddSlidingWindowLimiter` with 6 segments. This is appropriate for the per-institute tier of the upload limiter.

### 2.4 Token Bucket

**Mechanism:** A bucket holds tokens. Tokens are added at a fixed rate up to a maximum capacity. Each request consumes one token. Requests are rejected when the bucket is empty.

| Attribute | Value |
|---|---|
| Memory | O(1) per key -- token count + last refill timestamp |
| Accuracy | High -- allows controlled bursts |
| Complexity | Low |
| Distributed | Atomic compare-and-set or Redis Lua script |
| Burst handling | Configurable -- bucket capacity = max burst size |

**Key advantage for Cena:** A student starting a homework session may photograph 3 questions in quick succession (burst), then settle into 1 every 2-3 minutes. Token bucket naturally accommodates this: the bucket fills up during idle time, allowing short bursts. A fixed window would either reject the initial burst or allow sustained high throughput.

**Parameters for the upload use case:**
- `TokenLimit` (bucket capacity): 5 -- allows a burst of up to 5 rapid uploads.
- `TokensPerPeriod`: 1 -- refills 1 token per period.
- `ReplenishmentPeriod`: 12 seconds -- steady-state rate of 5 per minute.

### 2.5 Leaky Bucket

**Mechanism:** Requests enter a queue (bucket) and are processed at a fixed rate. If the queue is full, new requests are rejected. Output rate is perfectly smooth.

| Attribute | Value |
|---|---|
| Memory | O(queue size) |
| Accuracy | Perfect smoothing |
| Complexity | Moderate -- requires queue management |
| Distributed | Harder -- needs distributed queue |
| Burst handling | Queued, not rejected -- adds latency |

**Cena consideration:** Leaky bucket would queue student uploads rather than rejecting them, adding latency instead of returning 429. This is better UX ("your photo is being processed, please wait") but adds complexity. The queue approach is recommended for the client-side pattern but not for the server-side rate limiter.

### 2.6 Adaptive Rate Limiting

**Mechanism:** Dynamically adjust rate limits based on real-time system load, cost metrics, or anomaly detection. Machine learning models analyze traffic patterns and modify thresholds.

| Attribute | Value |
|---|---|
| Memory | Varies -- requires historical data |
| Accuracy | Highest -- adjusts to actual conditions |
| Complexity | High -- requires ML pipeline |
| Distributed | Requires centralized decision service |

**Cena consideration:** Adaptive limiting is a Phase 2 enhancement. For Phase 1, static limits with a cost-based circuit breaker achieve 90% of the protection. The adaptive layer would detect anomalies like "upload volume is 3x normal for this time of day" and temporarily tighten limits.

### 2.7 Algorithm Recommendation for Cena

**Selected: Token Bucket (per-student) + Sliding Window Counter (per-institute) + Cost Circuit Breaker (global).**

Rationale:

1. **Token bucket per-student** because students exhibit bursty behavior (photograph several questions at session start, then slow down). Token bucket naturally allows this while enforcing a steady-state rate. The existing Cena `GlobalRateLimiter` in `Cena.LlmAcl` already implements a sliding window for LLM tokens; the upload endpoint needs a complementary token bucket for request count.

2. **Sliding window counter per-institute** because it provides smooth multi-tenant isolation without the boundary burst problem. Already proven in the `tutor-tenant` policy with 6 segments per window.

3. **Cost circuit breaker global** because neither token bucket nor sliding window protects against aggregate cost exceeding budget. The circuit breaker monitors total Gemini API spend and trips when daily cost exceeds a configurable threshold, halting ALL uploads platform-wide until manually reset or the next budget period.

---

## 3. Multi-Tier Rate Limiting Architecture

```
                                    +------------------+
                                    |  Global Circuit  |
                                    |  Breaker         |
                                    |  ($X/day max)    |
                                    +--------+---------+
                                             |
                                    +--------v---------+
                                    | Per-Institute    |
                                    | Sliding Window   |
                                    | (Y/hour)         |
                                    +--------+---------+
                                             |
                                    +--------v---------+
                                    | Per-Session      |
                                    | Fixed Window     |
                                    | (Z/session)      |
                                    +--------+---------+
                                             |
                                    +--------v---------+
                                    | Per-Student      |
                                    | Token Bucket     |
                                    | (burst + rate)   |
                                    +--------+---------+
                                             |
                                    +--------v---------+
                                    | Anti-Bot Layer   |
                                    | (fingerprint,    |
                                    |  duplicate det.) |
                                    +------------------+
```

### Tier 1: Per-Student Token Bucket

| Parameter | Value | Rationale |
|---|---|---|
| Bucket capacity | 5 | Allows burst of 5 photos at session start |
| Refill rate | 1 token / 12 seconds | Steady-state: 5 per minute |
| Hourly cap | 30 | Hard cap regardless of bucket state |
| Daily cap | 100 | Prevents sustained abuse across sessions |

**Why 5/minute:** A student photographing problems from a textbook can realistically take 1 photo every 10-15 seconds at peak speed (aim camera, focus, capture, verify preview). 5 per minute allows headroom above physical capability. Any student sustaining > 5/minute is either automated or sharing credentials.

**Why 30/hour:** A 1-hour study session with intense problem-solving might cover 20-25 problems. 30 provides headroom for retakes (blurry photo, wrong page). Exceeding 30 in an hour signals abnormal usage.

**Why 100/day:** Even the most dedicated student studying for a major exam would not photograph more than 60-80 problems in a day. 100 is generous enough to never impact legitimate use while capping daily cost at $0.20 per student.

### Tier 2: Per-Institute Sliding Window

| Parameter | Value | Rationale |
|---|---|---|
| Window | 1 hour |  |
| Segments | 6 (10-minute segments) | Smoother limiting than single window |
| Limit | 500 / hour (default) | Configurable per institute |
| Configurable | Yes -- via `appsettings.json` per tenant | Larger schools get higher limits |

**Scaling formula:** `base_limit = 500`, `per_student_addition = 2`. An institute with 200 enrolled students gets `500 + (200 * 2) = 900` uploads/hour. This prevents a single compromised institute from exhausting the global budget while scaling with legitimate usage.

### Tier 3: Per-Session Fixed Window

| Parameter | Value | Rationale |
|---|---|---|
| Window | Session duration | Tied to active tutoring session |
| Limit | 20 | No session needs more than 20 photos |

**Why session-scoped:** Prevents credential sharing across sessions. Each authenticated session (tied to a SignalR connection) gets its own counter. If a student logs out and back in, the per-student token bucket still applies, but the session counter resets.

### Tier 4: Global Cost Circuit Breaker

| Parameter | Value | Rationale |
|---|---|---|
| Daily budget | $50 (configurable) | 25,000 images/day at $0.002 each |
| Warning threshold | 80% ($40) | Alert ops, do not block |
| Trip threshold | 100% ($50) | Block all uploads platform-wide |
| Half-open after | 1 hour | Allow 10 test uploads to check if abuse stopped |
| Manual override | Admin API endpoint | Ops can reset or increase budget |

**Why $50/day:** Normal daily volume projection: 500 students * 10 images/day = 5,000 images = $10/day. The $50 cap is 5x normal, providing ample headroom for growth while preventing runaway costs. In production, this should be tied to the billing alert in the cloud provider dashboard.

---

## 4. Recommended Limits Summary

| Tier | Scope | Limit | Window | Algorithm |
|---|---|---|---|---|
| Per-Student (burst) | Student ID | 5 uploads | per minute | Token Bucket |
| Per-Student (hourly) | Student ID | 30 uploads | per hour | Sliding Window Counter |
| Per-Student (daily) | Student ID | 100 uploads | per day | Fixed Window |
| Per-Session | Session ID | 20 uploads | per session | Fixed Window |
| Per-Institute | School ID | 500+ uploads | per hour | Sliding Window (6 segments) |
| Global cost | Platform | $50 | per day | Circuit Breaker |

---

## 5. Implementation in .NET

### 5.1 Token Bucket Per-Student Limiter

This integrates with the existing `AddRateLimiter()` block in `src/api/Cena.Student.Api.Host/Program.cs`.

```csharp
// ---- Screenshot Upload: Per-Student Token Bucket ----
// Students photograph math/physics questions; allow burst of 5 at session
// start, refill 1 token every 12 seconds (steady-state 5/min).
options.AddPolicy("screenshot-upload", httpContext =>
{
    var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? httpContext.User.FindFirstValue("sub")
        ?? "anonymous";

    return RateLimitPartition.GetTokenBucketLimiter(
        $"screenshot:{userId}",
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 5,                                  // Max burst
            TokensPerPeriod = 1,                             // Refill rate
            ReplenishmentPeriod = TimeSpan.FromSeconds(12),  // 5 tokens/min
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 2,  // Queue up to 2 requests instead of rejecting
            AutoReplenishment = true,
        });
});
```

**Why QueueLimit = 2:** Instead of immediately returning 429 for the 6th photo, queue up to 2 requests. This means the student sees "processing..." for ~12 seconds rather than an error. Better UX for borderline cases where a student is slightly over the burst limit.

### 5.2 Sliding Window Per-Institute Limiter

```csharp
// ---- Screenshot Upload: Per-Institute Sliding Window ----
// Prevents one school from exhausting the global Gemini budget.
// Default: 500/hour with 10-minute segments for smooth limiting.
var screenshotInstituteLimit = builder.Configuration
    .GetValue<int>("Cena:Screenshot:InstituteLimitPerHour", 500);

options.AddPolicy("screenshot-institute", httpContext =>
{
    var schoolId = httpContext.User.FindFirstValue("school_id") ?? "no-school";

    return RateLimitPartition.GetSlidingWindowLimiter(
        $"screenshot-institute:{schoolId}",
        _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = screenshotInstituteLimit,
            Window = TimeSpan.FromHours(1),
            SegmentsPerWindow = 6,  // 10-minute segments
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,         // Reject at institute level -- student should retry
            AutoReplenishment = true,
        });
});
```

### 5.3 Cost-Based Global Circuit Breaker

The circuit breaker is not a standard ASP.NET rate limiter. It is a custom middleware that wraps the upload endpoint and checks cumulative daily Gemini spend via Redis.

```csharp
// =============================================================================
// ScreenshotCostCircuitBreaker.cs
// Global cost-based circuit breaker for the screenshot upload pipeline.
// Checks cumulative daily Gemini vision spend and blocks all uploads
// when the daily budget is exceeded.
// =============================================================================

using StackExchange.Redis;

namespace Cena.Infrastructure.Ai;

public enum CircuitState { Closed, Open, HalfOpen }

public sealed class ScreenshotCostCircuitBreaker
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ScreenshotCostCircuitBreaker> _logger;
    private readonly decimal _dailyBudgetUsd;
    private readonly decimal _warningThresholdPct;
    private readonly int _halfOpenTestLimit;
    private readonly TimeSpan _halfOpenCooldown;
    private readonly TimeSpan _keyTtl = TimeSpan.FromHours(25);

    // Cost per Gemini 2.5 Flash vision call (input image + output tokens)
    private const decimal CostPerImageUsd = 0.002m;

    public ScreenshotCostCircuitBreaker(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<ScreenshotCostCircuitBreaker> logger)
    {
        _redis = redis;
        _logger = logger;
        _dailyBudgetUsd = configuration.GetValue<decimal>(
            "Cena:Screenshot:DailyBudgetUsd", 50m);
        _warningThresholdPct = configuration.GetValue<decimal>(
            "Cena:Screenshot:WarningThresholdPct", 0.80m);
        _halfOpenTestLimit = configuration.GetValue<int>(
            "Cena:Screenshot:HalfOpenTestLimit", 10);
        _halfOpenCooldown = TimeSpan.FromMinutes(
            configuration.GetValue<int>(
                "Cena:Screenshot:HalfOpenCooldownMinutes", 60));
    }

    public async Task<(bool Allowed, CircuitState State, string? Reason)>
        TryAcquireAsync(CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var today = DateTime.UtcNow.ToString("yyyyMMdd");

        var countKey = $"cena:screenshot:cost:count:{today}";
        var tripKey = $"cena:screenshot:cost:tripped:{today}";
        var halfOpenKey = $"cena:screenshot:cost:halfopen:{today}";

        // Check if circuit is tripped
        var tripped = await db.StringGetAsync(tripKey);
        if (tripped.HasValue)
        {
            // Check half-open state
            var halfOpenCount = await db.StringGetAsync(halfOpenKey);
            if (halfOpenCount.TryParse(out long hoCount) && hoCount < _halfOpenTestLimit)
            {
                // Allow limited test traffic
                await db.StringIncrementAsync(halfOpenKey);
                _logger.LogInformation(
                    "Screenshot circuit breaker HALF-OPEN: allowing test request {N}/{Limit}",
                    hoCount + 1, _halfOpenTestLimit);
                return (true, CircuitState.HalfOpen, null);
            }

            return (false, CircuitState.Open,
                $"Daily screenshot budget of ${_dailyBudgetUsd} exceeded. " +
                $"Service will resume at midnight UTC or when manually reset.");
        }

        // Increment counter and check budget
        var count = await db.StringIncrementAsync(countKey);
        await db.KeyExpireAsync(countKey, _keyTtl);

        var currentCostUsd = count * CostPerImageUsd;

        // Warning threshold
        if (currentCostUsd >= _dailyBudgetUsd * _warningThresholdPct &&
            currentCostUsd < _dailyBudgetUsd)
        {
            _logger.LogWarning(
                "Screenshot cost WARNING: ${Current:F2} / ${Budget:F2} " +
                "({Pct:P0} of daily budget). {Count} images today.",
                currentCostUsd, _dailyBudgetUsd,
                currentCostUsd / _dailyBudgetUsd, count);
        }

        // Trip threshold
        if (currentCostUsd >= _dailyBudgetUsd)
        {
            await db.StringSetAsync(tripKey, "1", _keyTtl);
            // Set half-open counter to reset after cooldown
            await db.StringSetAsync(halfOpenKey, "0", _halfOpenCooldown);

            _logger.LogCritical(
                "Screenshot circuit breaker TRIPPED: ${Current:F2} exceeds " +
                "daily budget of ${Budget:F2}. {Count} images processed. " +
                "All uploads blocked until reset.",
                currentCostUsd, _dailyBudgetUsd, count);

            return (false, CircuitState.Open,
                $"Daily screenshot budget of ${_dailyBudgetUsd} exceeded.");
        }

        return (true, CircuitState.Closed, null);
    }

    /// <summary>
    /// Admin endpoint: manually reset the circuit breaker (e.g., after
    /// increasing budget or confirming abuse has stopped).
    /// </summary>
    public async Task ResetAsync(string adminUserId)
    {
        var db = _redis.GetDatabase();
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var tripKey = $"cena:screenshot:cost:tripped:{today}";

        await db.KeyDeleteAsync(tripKey);
        _logger.LogWarning(
            "Screenshot circuit breaker manually RESET by admin {AdminId}",
            adminUserId);
    }

    public async Task<ScreenshotBudgetStatus> GetStatusAsync(
        CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var today = DateTime.UtcNow.ToString("yyyyMMdd");

        var count = await db.StringGetAsync($"cena:screenshot:cost:count:{today}");
        var tripped = await db.StringGetAsync($"cena:screenshot:cost:tripped:{today}");

        var imageCount = count.TryParse(out long c) ? c : 0;
        var currentCostUsd = imageCount * CostPerImageUsd;

        return new ScreenshotBudgetStatus(
            ImageCount: imageCount,
            CurrentCostUsd: currentCostUsd,
            DailyBudgetUsd: _dailyBudgetUsd,
            BudgetUsedPct: _dailyBudgetUsd > 0
                ? (double)(currentCostUsd / _dailyBudgetUsd)
                : 0,
            State: tripped.HasValue ? CircuitState.Open : CircuitState.Closed,
            Date: today);
    }
}

public record ScreenshotBudgetStatus(
    long ImageCount,
    decimal CurrentCostUsd,
    decimal DailyBudgetUsd,
    double BudgetUsedPct,
    CircuitState State,
    string Date);
```

### 5.4 Composite Middleware for the Upload Endpoint

Wiring all three tiers together on the endpoint:

```csharp
// In Program.cs or the endpoint mapping file:
var screenshotGroup = app.MapGroup("/api/me/screenshot")
    .RequireAuthorization()
    .RequireRateLimiting("screenshot-upload")       // Tier 1: per-student token bucket
    .RequireRateLimiting("screenshot-institute");    // Tier 2: per-institute sliding window

screenshotGroup.MapPost("/analyze", async (
    IFormFile photo,
    HttpContext httpContext,
    ScreenshotCostCircuitBreaker circuitBreaker,
    IScreenshotAnalyzer analyzer,
    CancellationToken ct) =>
{
    // Tier 4: Global cost circuit breaker
    var (allowed, state, reason) = await circuitBreaker.TryAcquireAsync(ct);
    if (!allowed)
    {
        httpContext.Response.Headers["Retry-After"] = "3600"; // 1 hour
        return Results.Json(new
        {
            error = "screenshot_budget_exceeded",
            message = reason,
            retryAfterSeconds = 3600,
            circuitState = state.ToString().ToLowerInvariant()
        }, statusCode: 429);
    }

    // Validate file
    if (photo.Length == 0)
        return Results.BadRequest(new { error = "empty_file" });

    if (photo.Length > 10 * 1024 * 1024) // 10 MB max
        return Results.BadRequest(new { error = "file_too_large", maxBytes = 10 * 1024 * 1024 });

    var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "image/png", "image/jpeg", "image/webp" };
    if (!allowedTypes.Contains(photo.ContentType))
        return Results.BadRequest(new { error = "invalid_content_type",
            allowed = allowedTypes });

    var result = await analyzer.AnalyzeAsync(photo.OpenReadStream(),
        photo.ContentType, ct);
    return Results.Ok(result);
});
```

### 5.5 DI Registration

```csharp
// In Program.cs service registration block:
builder.Services.AddSingleton<ScreenshotCostCircuitBreaker>();
```

### 5.6 appsettings.json Configuration

```json
{
  "Cena": {
    "Screenshot": {
      "DailyBudgetUsd": 50,
      "WarningThresholdPct": 0.80,
      "HalfOpenTestLimit": 10,
      "HalfOpenCooldownMinutes": 60,
      "InstituteLimitPerHour": 500,
      "StudentBurstLimit": 5,
      "StudentTokensPerPeriod": 1,
      "StudentReplenishmentSeconds": 12
    }
  }
}
```

---

## 6. Client-Side UX

### 6.1 Communicating Rate Limits to Students

**Response headers on every upload response:**

```
X-RateLimit-Limit: 5
X-RateLimit-Remaining: 3
X-RateLimit-Reset: 1712937600
X-RateLimit-Policy: screenshot-upload
```

**Client-side behavior in the Vue 3 student app:**

```typescript
// In useScreenshotUpload.ts composable
const uploadPhoto = async (file: File) => {
  const response = await api.post('/api/me/screenshot/analyze', formData);

  // Parse rate limit headers
  const remaining = parseInt(
    response.headers['x-ratelimit-remaining'] ?? '5');
  const resetAt = parseInt(
    response.headers['x-ratelimit-reset'] ?? '0');

  if (remaining <= 1) {
    // Show gentle warning before hitting the limit
    showToast({
      message: t('screenshot.rateLimitWarning'),  // "You have 1 photo remaining. Take a moment."
      type: 'info',
      duration: 5000,
    });
  }

  if (response.status === 429) {
    const retryAfter = parseInt(
      response.headers['retry-after'] ?? '60');

    // Show countdown timer, not just an error
    showUploadCooldown(retryAfter);
    // "You can upload another photo in 45 seconds"

    // Queue the photo for automatic retry
    queueForRetry(file, retryAfter);
    return;
  }

  return response.data;
};
```

### 6.2 Graceful Degradation: Queue, Not Reject

For the student-facing app, rate limiting should feel like a pause, not a wall.

**Strategy:**
1. When a student hits the per-student rate limit, the UI shows "Your photo is queued and will be processed shortly" with a countdown timer.
2. The client holds the photo in local storage (IndexedDB / sqflite on Flutter mobile).
3. When the `Retry-After` period elapses, the client automatically retries.
4. If the global circuit breaker has tripped, the UI shows "Our photo analysis service is temporarily at capacity. Your photo has been saved and will be processed when service resumes."

**Mobile (Flutter) consideration:** On the Flutter mobile app, photos are stored in the device's camera roll. The app should not delete the photo from the queue until the server confirms processing. Offline queue (from the existing `SyncManager`) can hold upload requests for when connectivity returns.

### 6.3 Localization of Error Messages

All rate limit messages must be localized in English, Arabic, and Hebrew per the Cena language strategy:

| Key | EN | AR | HE |
|---|---|---|---|
| `screenshot.rateLimitWarning` | You have {n} photos remaining this minute. | لديك {n} صور متبقية هذه الدقيقة. | נותרו לך {n} תמונות הדקה. |
| `screenshot.rateLimitExceeded` | Please wait {seconds}s before uploading another photo. | يرجى الانتظار {seconds} ثانية قبل تحميل صورة أخرى. | אנא המתן {seconds} שניות לפני העלאת תמונה נוספת. |
| `screenshot.budgetExceeded` | Photo analysis is temporarily at capacity. Your photo has been saved. | تحليل الصور في طاقته القصوى مؤقتاً. تم حفظ صورتك. | שירות ניתוח התמונות עמוס זמנית. התמונה שלך נשמרה. |

---

## 7. Monitoring and Alerting

### 7.1 Metrics to Track

| Metric | Type | Labels | Purpose |
|---|---|---|---|
| `cena_screenshot_upload_total` | Counter | `student_id`, `school_id`, `status` (ok/rejected/queued) | Volume tracking |
| `cena_screenshot_ratelimit_rejected_total` | Counter | `tier` (student/institute/global), `school_id` | Abuse detection |
| `cena_screenshot_cost_usd_daily` | Gauge | (none) | Cost monitoring |
| `cena_screenshot_cost_budget_pct` | Gauge | (none) | Budget utilization |
| `cena_screenshot_circuit_state` | Gauge | (none) | 0=closed, 1=half-open, 2=open |
| `cena_screenshot_duplicate_detected_total` | Counter | `student_id` | Duplicate flooding |
| `cena_screenshot_latency_ms` | Histogram | `step` (upload/gemini/cas) | Latency per pipeline stage |

### 7.2 Alert Rules

Add to `config/prom-rules/cena-security.yml`:

```yaml
- name: cena_screenshot_cost_protection
  interval: 30s
  rules:
    # WARNING: Daily screenshot budget approaching limit
    - alert: CenaScreenshotBudgetWarning
      expr: |
        cena_screenshot_cost_budget_pct > 0.8
      for: 0m
      labels:
        severity: warning
        category: cost
      annotations:
        summary: "Screenshot budget at {{ $value | humanizePercentage }}"
        description: |
          Daily screenshot processing budget is at {{ $value | humanizePercentage }}.
          Consider investigating for abuse or increasing the budget.

    # CRITICAL: Circuit breaker tripped
    - alert: CenaScreenshotCircuitBreakerOpen
      expr: |
        cena_screenshot_circuit_state == 2
      for: 0m
      labels:
        severity: critical
        category: cost
        paged: "true"
      annotations:
        summary: "Screenshot circuit breaker OPEN -- all uploads blocked"
        description: |
          The global screenshot cost circuit breaker has tripped.
          Daily budget exceeded. All student photo uploads are blocked.
          Manual reset available via admin API.

    # WARNING: Single student hitting rate limits repeatedly
    - alert: CenaScreenshotStudentAbuse
      expr: |
        sum by (student_id) (
          rate(cena_screenshot_ratelimit_rejected_total{tier="student"}[5m])
        ) > 10/60
      for: 5m
      labels:
        severity: warning
        category: security
      annotations:
        summary: "Student {{ $labels.student_id }} hitting screenshot rate limits"
        description: |
          A single student is being rate-limited more than 10 times per minute.
          Possible bot or credential compromise. Consider temporary suspension.

    # CRITICAL: Institute-level rate limit saturation
    - alert: CenaScreenshotInstituteSaturation
      expr: |
        sum by (school_id) (
          rate(cena_screenshot_ratelimit_rejected_total{tier="institute"}[10m])
        ) > 5/60
      for: 10m
      labels:
        severity: critical
        category: security
      annotations:
        summary: "Institute {{ $labels.school_id }} saturating screenshot limits"
        description: |
          An entire institute is hitting screenshot rate limits.
          Possible coordinated attack or legitimate high-usage event.
```

### 7.3 Grafana Dashboard Panels

1. **Screenshot Volume** -- Timeseries of `cena_screenshot_upload_total` by status.
2. **Cost Burn Rate** -- Gauge showing `cena_screenshot_cost_usd_daily` with thresholds at $40 (yellow) and $50 (red).
3. **Top 10 Uploaders** -- Table of students ranked by upload count in the last hour.
4. **Rate Limit Rejections** -- Stacked bar by tier (student/institute/global).
5. **Circuit Breaker State** -- State timeline showing closed/half-open/open transitions.
6. **Duplicate Detection Rate** -- Percentage of uploads flagged as duplicates.

---

## 8. Anti-Bot Measures

### 8.1 Image Fingerprinting (Duplicate Detection)

**Mechanism:** Compute a perceptual hash (pHash) of each uploaded image. Compare against recent uploads from the same student and across the institute. Identical or near-identical images are rejected without incurring a Gemini API call.

**Implementation approach:**

```csharp
public sealed class ImageFingerprintService
{
    private readonly IConnectionMultiplexer _redis;
    private const int HashBitLength = 64;
    private const int HammingDistanceThreshold = 5; // Images with distance <= 5 are "same"

    /// <summary>
    /// Check if this image (or a near-duplicate) was recently uploaded.
    /// Returns true if duplicate detected, false if novel.
    /// </summary>
    public async Task<(bool IsDuplicate, string? OriginalUploadId)>
        CheckDuplicateAsync(
            string studentId, byte[] imageBytes, string uploadId,
            CancellationToken ct = default)
    {
        var hash = ComputePerceptualHash(imageBytes);
        var db = _redis.GetDatabase();
        var key = $"cena:screenshot:phash:{studentId}";

        // Get all recent hashes for this student (last 24h)
        var entries = await db.HashGetAllAsync(key);

        foreach (var entry in entries)
        {
            var storedHash = (ulong)(long)entry.Value;
            var distance = HammingDistance(hash, storedHash);
            if (distance <= HammingDistanceThreshold)
            {
                return (true, entry.Name.ToString());
            }
        }

        // Store this hash
        await db.HashSetAsync(key, uploadId, (long)hash);
        await db.KeyExpireAsync(key, TimeSpan.FromHours(24));

        return (false, null);
    }

    private static ulong ComputePerceptualHash(byte[] imageBytes)
    {
        // Use average hash (aHash) for speed:
        // 1. Resize to 8x8 grayscale
        // 2. Compute mean pixel value
        // 3. Set bit 1 if pixel > mean, 0 otherwise
        // Production: use a library like ImageSharp + pHash algorithm
        // Placeholder showing the algorithm structure:
        using var image = Image.Load<L8>(imageBytes);
        image.Mutate(x => x.Resize(8, 8));

        var pixels = new byte[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                pixels[y * 8 + x] = image[x, y].PackedValue;

        var mean = pixels.Average(p => (double)p);
        ulong hash = 0;
        for (int i = 0; i < 64; i++)
        {
            if (pixels[i] > mean)
                hash |= (1UL << i);
        }

        return hash;
    }

    private static int HammingDistance(ulong a, ulong b)
    {
        var xor = a ^ b;
        return BitOperations.PopCount(xor);
    }
}
```

**Cost savings:** If 10% of uploads are duplicates (re-uploads of blurry photos), fingerprinting saves $0.0002/duplicate by avoiding the Gemini call. At scale: 10,000 uploads/day * 10% duplicate rate * $0.002 = $2/day saved.

### 8.2 CAPTCHA Triggers

**Progressive challenge escalation:**

| Condition | Action |
|---|---|
| Normal usage (< 3 uploads/min) | No challenge |
| Elevated usage (3-5 uploads/min) | Invisible reCAPTCHA v3 score check |
| High usage (> 5 uploads/min, rate limited) | Interactive CAPTCHA before next upload |
| Repeated rate limit hits (> 10 in 5 min) | Temporary 15-minute upload suspension + CAPTCHA to resume |

**Implementation note:** CAPTCHA should be implemented on the client side (Vue 3 / Flutter) and validated server-side. The server endpoint accepts an optional `captcha_token` parameter. When the student is in the "high usage" tier, the server rejects requests without a valid CAPTCHA token.

**COPPA consideration:** For students under 13, CAPTCHA alternatives should be used (simple math puzzle: "What is 3+4?" which is ironic for a math tutoring platform but effective). Google reCAPTCHA's privacy policy may conflict with COPPA restrictions on third-party tracking.

### 8.3 Device Fingerprinting Considerations

**What to track:**
- User-Agent string.
- Screen resolution and color depth (from client telemetry, already collected for UX analytics).
- Timezone offset.
- Number of concurrent sessions per device fingerprint.

**What NOT to track (privacy):**
- Canvas fingerprinting (GDPR Article 5(1)(c) -- data minimization).
- WebGL renderer strings.
- Installed fonts or plugins.

**Decision:** For Cena, device fingerprinting should be lightweight (User-Agent + timezone + screen resolution) and used only for anomaly detection (flagging when one account is used from 5+ distinct device profiles in 24 hours), not for blocking. The GDPR-K and COPPA constraints for minors limit the fingerprinting techniques available.

---

## 9. Cost Protection Math

### 9.1 Normal Operations

| Metric | Value |
|---|---|
| Enrolled students | 500 (Phase 1 pilot) |
| Daily active students | 200 (40% DAU) |
| Avg uploads/student/day | 8 |
| Daily upload volume | 1,600 images |
| Cost per image (Gemini) | $0.002 |
| **Daily Gemini cost** | **$3.20** |
| **Monthly Gemini cost** | **$96** (30 days) |

### 9.2 Growth Projections

| Students | DAU | Daily Uploads | Daily Cost | Monthly Cost |
|---|---|---|---|---|
| 500 | 200 | 1,600 | $3.20 | $96 |
| 2,000 | 800 | 6,400 | $12.80 | $384 |
| 10,000 | 4,000 | 32,000 | $64.00 | $1,920 |
| 50,000 | 20,000 | 160,000 | $320.00 | $9,600 |

### 9.3 Attack Scenarios Without Rate Limiting

| Scenario | Volume | Duration | Cost | Outcome |
|---|---|---|---|---|
| Single bot loop | 60 img/min | 1 hour | $7.20 | Noticeable but manageable |
| Single bot loop | 60 img/min | 24 hours | $172.80 | Exceeds monthly budget |
| Distributed (50 accounts) | 250 img/min | 1 hour | $30.00 | 10x normal daily cost in 1 hour |
| Distributed (50 accounts) | 250 img/min | 24 hours | $720.00 | Platform-threatening |
| Mass bot attack (1M images) | varies | varies | **$2,000** | Catastrophic -- exceeds annual budget |

### 9.4 Attack Scenarios WITH Rate Limiting

| Scenario | Without Limits | With Limits | Savings |
|---|---|---|---|
| Single bot (1 hour) | $7.20 | $0.60 (5/min cap) | 92% |
| Single bot (24 hours) | $172.80 | $0.20 (100/day cap) | 99.9% |
| Distributed 50 accounts (1 hour) | $30.00 | $1.00 (institute cap) | 97% |
| Mass attack (1M images) | $2,000 | $50 (circuit breaker) | **97.5%** |

### 9.5 Circuit Breaker Break-Even

The circuit breaker at $50/day caps maximum exposure to $50 regardless of attack volume. Break-even analysis:

- **Without circuit breaker:** A 24-hour distributed attack costs $720. Annual risk (assuming 1 incident/month): $8,640.
- **With circuit breaker:** Maximum cost per incident: $50. Annual risk: $600.
- **Engineering cost of implementation:** ~2 developer-days = ~$2,000 one-time.
- **ROI payback period:** 3 months (one prevented incident pays for the implementation).

---

## 10. Security Score Contribution

| Defense Layer | Points | Rationale |
|---|---|---|
| Per-student token bucket rate limiter | 3 | Prevents single-student flooding; burst-friendly algorithm |
| Per-institute sliding window | 2 | Multi-tenant isolation; prevents cross-tenant budget exhaustion |
| Per-session fixed window | 1 | Limits credential sharing impact within a session |
| Global cost circuit breaker | 4 | Prevents catastrophic cost overrun; the most critical defense |
| Image fingerprinting (duplicate detection) | 2 | Reduces waste; detects bot patterns |
| CAPTCHA progressive escalation | 1 | Deters casual bots without impacting normal UX |
| Monitoring and alerting | 1 | Enables rapid incident response |
| Client-side graceful degradation | 1 | Prevents user frustration; queues instead of rejecting |
| **Total** | **15** | **Out of 100 cumulative across all 10 iterations** |

---

## 11. Integration with Existing Cena Infrastructure

### 11.1 Relationship to Existing Rate Limiting

The screenshot upload rate limiting policies are **additive** to the existing seven policies in `Program.cs`. They do not replace or modify:
- `api` (100 req/min) -- general API protection still applies to the upload endpoint.
- `ai` (10 req/min) -- applies to AI generation endpoints, NOT to the screenshot upload (different policy).
- `tutor` / `tutor-global` / `tutor-tenant` -- applies to tutor messaging, NOT to screenshot upload.

The upload endpoint is protected by the `api` policy PLUS the `screenshot-upload` token bucket PLUS the `screenshot-institute` sliding window PLUS the custom `ScreenshotCostCircuitBreaker` middleware. Requests must pass ALL tiers.

### 11.2 Relationship to AiTokenBudgetService

The existing `AiTokenBudgetService` (FIND-sec-015) tracks **LLM token budgets** (global: 10M tokens/day, per-tenant: 500K tokens/day). The `ScreenshotCostCircuitBreaker` tracks **Gemini vision API call budgets** (dollar-denominated, not token-denominated). They are complementary:

- A screenshot upload consumes Gemini vision budget (circuit breaker).
- If the screenshot result triggers a follow-up tutoring interaction, that consumes LLM token budget (`AiTokenBudgetService`).
- Both must pass for the full pipeline to execute.

### 11.3 Relationship to GlobalRateLimiter (LLM ACL)

The `GlobalRateLimiter` in `Cena.LlmAcl.Tracking` uses in-memory sliding windows for LLM request rate limiting (500K tokens/min for Haiku, 100K for Sonnet, 50K for Opus). This is orthogonal to the screenshot rate limiting. The screenshot pipeline calls Gemini (not through the LLM ACL gateway), so the `GlobalRateLimiter` does not apply.

---

## 12. Testing Strategy

### 12.1 Unit Tests

```csharp
[Fact]
public async Task TokenBucket_AllowsBurstThenThrottles()
{
    // Arrange: 5-token bucket, 1 token/12s refill
    var limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 5,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(12),
        QueueLimit = 0,
        AutoReplenishment = false, // Manual for testing
    });

    // Act: exhaust all 5 tokens
    for (int i = 0; i < 5; i++)
    {
        var lease = limiter.AttemptAcquire();
        Assert.True(lease.IsAcquired, $"Request {i+1} should succeed");
    }

    // Assert: 6th request rejected
    var blocked = limiter.AttemptAcquire();
    Assert.False(blocked.IsAcquired, "6th request should be blocked");
}

[Fact]
public async Task CircuitBreaker_TripsAtBudgetLimit()
{
    // Arrange: $0.01 budget (5 images at $0.002 each)
    var redis = GetTestRedis();
    var breaker = new ScreenshotCostCircuitBreaker(redis, config, logger);

    // Act: 5 images = $0.01 = 100% budget
    for (int i = 0; i < 5; i++)
    {
        var (allowed, _, _) = await breaker.TryAcquireAsync();
        Assert.True(allowed);
    }

    // Assert: 6th image blocked
    var (blocked, state, reason) = await breaker.TryAcquireAsync();
    Assert.False(blocked);
    Assert.Equal(CircuitState.Open, state);
    Assert.Contains("budget", reason, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task DuplicateDetection_RejectsSameImage()
{
    var service = new ImageFingerprintService(redis);
    var imageBytes = LoadTestImage("math-problem-1.jpg");

    var (isDup1, _) = await service.CheckDuplicateAsync("student-1", imageBytes, "upload-1");
    Assert.False(isDup1); // First upload: not duplicate

    var (isDup2, origId) = await service.CheckDuplicateAsync("student-1", imageBytes, "upload-2");
    Assert.True(isDup2); // Same image: duplicate
    Assert.Equal("upload-1", origId);
}
```

### 12.2 Load Test

```bash
# k6 load test: simulate 50 students uploading at max rate for 10 minutes
k6 run --vus 50 --duration 10m screenshot-upload-loadtest.js
# Expected: per-student limits kick in after 5 rapid uploads
# Expected: institute limit kicks in when aggregate exceeds 500/hour
# Expected: circuit breaker does NOT trip (50 students * 5/min * 10 min = 2,500 images = $5)
```

---

## References

### Cena Codebase

- `src/api/Cena.Student.Api.Host/Program.cs` -- existing rate limiting policies (7 policies)
- `src/llm-acl/Cena.LlmAcl/Tracking/GlobalRateLimiter.cs` -- in-memory sliding window for LLM tokens
- `src/shared/Cena.Infrastructure/Ai/AiTokenBudgetService.cs` -- Redis-backed daily token budget
- `src/actors/Cena.Actors/Ingest/GeminiOcrClient.cs` -- Gemini 2.5 Flash vision client ($0.002/image)
- `config/prom-rules/cena-security.yml` -- existing Prometheus alerting rules
- `tasks/security/done/SEC-007-ratelimit.md` -- original rate limiting task (Redis sorted set design)
- `tasks/review/done/REV-011-rate-limiting-validation.md` -- rate limiting + file upload hardening review

### External Sources

- [ASP.NET Core Rate Limiting Middleware -- Microsoft Learn](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Rate Limiting Algorithms: Token Bucket vs Sliding Window vs Fixed Window -- Arcjet](https://blog.arcjet.com/rate-limiting-algorithms-token-bucket-vs-sliding-window-vs-fixed-window/)
- [From Token Bucket to Sliding Window: Pick the Perfect Rate Limiting Algorithm -- API7.ai](https://api7.ai/blog/rate-limiting-guide-algorithms-best-practices)
- [Build 5 Rate Limiters with Redis: Algorithm Comparison Guide -- Redis](https://redis.io/tutorials/howtos/ratelimiting/)
- [10 Best Practices for API Rate Limiting in 2025 -- Zuplo](https://zuplo.com/learning-center/10-best-practices-for-api-rate-limiting-in-2025)
- [The Cost Circuit Breaker: How We Prevent Runaway Spending Across 9 AI Agents -- DEV Community](https://dev.to/sebastian_chedal/the-cost-circuit-breaker-how-we-prevent-runaway-spending-across-9-ai-agents-4i5k)
- [API Rate Limiting at Scale: Patterns, Failures, and Control Strategies -- Gravitee](https://www.gravitee.io/blog/rate-limiting-apis-scale-patterns-strategies)
- [Duplicate Image Detection with Perceptual Hashing in Python -- Ben Hoyt](https://benhoyt.com/writings/duplicate-image-detection/)
- [Perceptual Hashing -- Wikipedia](https://en.wikipedia.org/wiki/Perceptual_hashing)
- [Adaptive Rate Limiting Using Reinforcement Learning to Thwart API Abuse -- Analytics Insight](https://www.analyticsinsight.net/tech-news/adaptive-rate-limiting-using-reinforcement-learning-to-thwart-api-abuse)
- [mCaptcha: Replacing Captchas with Rate Limiters -- Communications of the ACM](https://cacm.acm.org/research/mcaptcha-replacing-captchas-with-rate-limiters-to-improve-security-and-accessibility/)
- [429 Too Many Requests -- MDN Web Docs](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Status/429)
- [Account Sharing: The Step-By-Step Prevention Guide -- Fingerprint](https://fingerprint.com/blog/increase-revenue-identifying-preventing-account-sharing/)
- [What is Credential Stuffing -- CrowdStrike](https://www.crowdstrike.com/en-us/cybersecurity-101/cyberattacks/credential-stuffing/)
- [Rate Limiting Best Practices -- Cloudflare WAF Docs](https://developers.cloudflare.com/waf/rate-limiting-rules/best-practices/)
- [Rate Limits -- OpenAI API](https://developers.openai.com/api/docs/guides/rate-limits)
