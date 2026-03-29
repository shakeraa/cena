# REV-011: API Rate Limiting, Input Validation & File Upload Hardening

**Priority:** P1 -- HIGH (no HTTP-level rate limiting; AI generation endpoint enables cost amplification attacks)
**Blocked by:** None
**Blocks:** None
**Estimated effort:** 2 days
**Source:** System Review 2026-03-28 -- Cyber Officer 1 (Findings 4, 9, 10), Backend Senior (I5)
**Supersedes:** SEC-007 (rate limiting) -- SEC-007 references SignalR/WebSocket that don't exist; this task reflects actual architecture

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

Three related API boundary gaps exist:

1. **No rate limiting**: The actor system has internal limits (200 activations/sec), but HTTP endpoints are unprotected. `/api/admin/ai/generate` can rack up Anthropic API costs. `/api/admin/users` enables user enumeration. `/api/admin/system/clean-reseed` is a destructive operation.
2. **No input validation**: All endpoint handlers pass request DTOs directly to services. No FluentValidation, no DataAnnotations, no parameter bounds. `period`, `limit`, `pageSize` accept arbitrary values.
3. **No file upload limits**: `/api/admin/ingestion/upload` and `/api/admin/users/bulk-invite` accept files without size limits, type validation, or filename sanitization.

## Architect's Decision

- Use **ASP.NET Core's built-in `AddRateLimiter()`** (not Redis-backed for now -- the internal `FixedWindowRateLimiter` is sufficient for single-instance development). Production can upgrade to Redis-backed via SEC-007/INF-004.
- Use **MinimalAPI parameter validation** with manual checks in endpoint handlers (not FluentValidation -- too heavy for the current minimal API pattern). Add a shared `ValidationHelper` for common patterns.
- Use **Kestrel's `MaxRequestBodySize`** for upload limits, plus explicit content-type allowlists.

## Subtasks

### REV-011.1: Add Rate Limiting Middleware

**Files to modify:**
- `src/api/Cena.Api.Host/Program.cs`
- `src/actors/Cena.Actors.Host/Program.cs`

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // General API: 100 req/min per user
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // AI generation: 10 req/min per user (cost protection)
    options.AddFixedWindowLimiter("ai", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    // Destructive operations: 2 req/min per user
    options.AddFixedWindowLimiter("destructive", opt =>
    {
        opt.PermitLimit = 2;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers["Retry-After"] = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded. Please try again later.",
            retryAfterSeconds = 60
        });
    };
});

// In pipeline
app.UseRateLimiter();
```

**Apply per-group:**
```csharp
// AI generation group
aiGroup.RequireRateLimiting("ai");

// System destructive operations
systemGroup.MapPost("/clean-reseed", ...).RequireRateLimiting("destructive");
systemGroup.MapPost("/reseed", ...).RequireRateLimiting("destructive");

// Default for all admin endpoints
adminGroup.RequireRateLimiting("api");
```

**Acceptance:**
- [ ] 101st request within 1 minute returns HTTP 429 with `Retry-After` header
- [ ] AI generation endpoint limited to 10 req/min
- [ ] Reseed/clean-reseed limited to 2 req/min
- [ ] Rate limit is per-authenticated-user (keyed by Firebase UID from claims)
- [ ] Unauthenticated requests (health checks) are not rate-limited

### REV-011.2: Add Parameter Validation to Admin Endpoints

**File to create:** `src/api/Cena.Admin.Api/Validation/ParameterValidator.cs`

```csharp
namespace Cena.Admin.Api.Validation;

public static class ParameterValidator
{
    private static readonly HashSet<string> ValidPeriods = new() { "7d", "30d", "90d", "365d" };

    public static string ValidatePeriod(string? period)
        => ValidPeriods.Contains(period ?? "30d") ? (period ?? "30d")
           : throw new BadHttpRequestException($"Invalid period '{period}'. Valid: {string.Join(", ", ValidPeriods)}");

    public static int ValidateLimit(int? limit, int max = 100)
        => limit switch
        {
            null => 20,
            < 1 => throw new BadHttpRequestException("Limit must be at least 1"),
            _ when limit > max => throw new BadHttpRequestException($"Limit cannot exceed {max}"),
            _ => limit.Value
        };

    public static int ValidatePage(int? page)
        => page switch
        {
            null or < 1 => 1,
            _ => page.Value
        };

    public static int ValidatePageSize(int? pageSize, int max = 100)
        => pageSize switch
        {
            null => 20,
            < 1 => throw new BadHttpRequestException("PageSize must be at least 1"),
            _ when pageSize > max => throw new BadHttpRequestException($"PageSize cannot exceed {max}"),
            _ => pageSize.Value
        };
}
```

**Files to modify:** Apply validation in `AdminDashboardEndpoints.cs`, `AdminApiEndpoints.cs`, and all endpoint files that accept `period`, `limit`, `page`, `pageSize` parameters.

**Acceptance:**
- [ ] `?period=999d` returns 400 Bad Request with valid options listed
- [ ] `?limit=99999` returns 400 with max allowed
- [ ] `?pageSize=-1` returns 400
- [ ] Default values applied when parameters omitted

### REV-011.3: Add File Upload Limits & Type Validation

**Files to modify:**
- `src/api/Cena.Admin.Api/IngestionPipelineService.cs` (upload endpoint)
- `src/api/Cena.Admin.Api/AdminUserEndpoints.cs` (bulk-invite endpoint)
- `src/api/Cena.Api.Host/Program.cs` (Kestrel limits)

**Kestrel global limit:**
```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50MB global max
});
```

**Per-endpoint validation in upload handler:**
```csharp
private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
{
    "application/pdf", "image/png", "image/jpeg", "image/webp",
    "text/csv", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
};

private const long MaxFileSize = 20 * 1024 * 1024; // 20MB per file

// In handler:
if (file.Length > MaxFileSize)
    return Results.BadRequest($"File exceeds maximum size of {MaxFileSize / (1024 * 1024)}MB");

if (!AllowedContentTypes.Contains(file.ContentType))
    return Results.BadRequest($"File type '{file.ContentType}' not allowed");

// Sanitize filename
var safeName = Path.GetFileName(file.FileName)
    .Replace("..", "")
    .Replace("/", "")
    .Replace("\\", "");
```

**Acceptance:**
- [ ] Files > 20MB are rejected with 400
- [ ] Only PDF, PNG, JPEG, WebP, CSV, XLSX are accepted
- [ ] Filename is sanitized (no path traversal)
- [ ] Bulk-invite only accepts CSV files
- [ ] Kestrel rejects request bodies > 50MB at the transport level

### REV-011.4: Guard Seeding Endpoints Behind IsDevelopment

**File to modify:** `src/api/Cena.Admin.Api/AdminApiEndpoints.cs`

```csharp
// Only register destructive seeding endpoints in development
if (env.IsDevelopment())
{
    systemGroup.MapPost("/reseed", ...);
    systemGroup.MapPost("/clean-reseed", ...);
}
```

**Acceptance:**
- [ ] `/api/admin/system/reseed` returns 404 in non-Development environments
- [ ] `/api/admin/system/clean-reseed` returns 404 in non-Development environments
- [ ] Both endpoints still work in Development with SuperAdminOnly auth
