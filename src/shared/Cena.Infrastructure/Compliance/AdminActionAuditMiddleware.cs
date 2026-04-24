// =============================================================================
// Cena Platform — Admin Action Audit Middleware (RDY-029 sub-task 5)
//
// Captures every authenticated write operation (POST/PUT/PATCH/DELETE) on
// /api/admin/** into an AuditEventDocument + a [AUDIT] structured log so
// ops can trace any admin CRUD via Marten queries AND via the Serilog
// log stream (which ships to an external aggregator when configured).
//
// Sibling to StudentDataAuditMiddleware (FERPA read-access audit). This
// one covers the write side:
//   • who performed the action (user_id + display name + role)
//   • what action (HTTP method + path + extracted target type/id)
//   • when (server UTC)
//   • from where (client IP, normalized per GDPR IP truncation rule)
//   • result (HTTP status + success flag + error message if any)
//
// Reads are NOT captured here — they'd flood the audit table with session
// polls and GET noise without forensic value. Read access to student data
// is captured by StudentDataAuditMiddleware because FERPA requires it.
//
// Pipeline position: after auth, after StudentDataAuditMiddleware. Writes
// are non-blocking: always awaits the handler first, then logs in a
// try/catch so audit-write failures never break the request.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Network;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

public sealed class AdminActionAuditMiddleware
{
    private static readonly string[] AuditedMethods =
        { HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete };

    private readonly RequestDelegate _next;
    private readonly ILogger<AdminActionAuditMiddleware> _logger;

    public AdminActionAuditMiddleware(
        RequestDelegate next,
        ILogger<AdminActionAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IDocumentSession session)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        // Narrow to admin write ops. GET reads go through StudentDataAuditMiddleware
        // when they touch student data; other reads don't need an audit trail.
        if (!ShouldAudit(method, path))
        {
            await _next(context);
            return;
        }

        // Capture the request identity before invoking next — the handler
        // may clear claims (rare) and we want the "who" regardless.
        var (userId, userName, role, tenantId) = ExtractIdentity(context.User);

        var ipAddress = IpAddressNormalizer.Normalize(context.Connection.RemoteIpAddress);
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var (targetType, targetId) = ExtractTarget(path, context.Request);

        Exception? thrown = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            thrown = ex;
            throw;
        }
        finally
        {
            // Fire after the handler completes so we capture the real
            // status code. Never surfaces audit-write errors to callers.
            var status = context.Response.StatusCode;
            var success = thrown is null && status >= 200 && status < 400;
            var action = BuildAction(method, path);
            var description = BuildDescription(method, path, status);

            // Structured log first — cheapest, always runs even if Marten
            // is down. Serilog ships this to whatever external aggregator
            // the host has configured (prod: Loki / ELK / Grafana Cloud).
            _logger.LogInformation(
                "[AUDIT] user={UserId} role={Role} tenant={TenantId} action={Action} target={TargetType}:{TargetId} status={Status} success={Success} ip={Ip}",
                userId, role, tenantId, action, targetType ?? "-", targetId ?? "-", status, success, ipAddress);

            // Marten persistence — best-effort, never block the request.
            try
            {
                session.Store(new AuditEventDocument
                {
                    Id = $"audit:admin-action:{Guid.NewGuid():N}",
                    Timestamp = DateTimeOffset.UtcNow,
                    EventType = "admin_action",
                    UserId = userId,
                    UserName = userName,
                    UserRole = role,
                    TenantId = tenantId,
                    Action = action,
                    TargetType = targetType ?? string.Empty,
                    TargetId = targetId ?? string.Empty,
                    Description = description,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Success = success,
                    ErrorMessage = thrown?.Message,
                });

                await session.SaveChangesAsync(context.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[AUDIT] Failed to persist admin-action audit row for {Method} {Path}",
                    method, path);
            }
        }
    }

    /// <summary>
    /// Admin write ops only. Split out so the matcher is unit-testable.
    /// </summary>
    internal static bool ShouldAudit(string method, string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!Array.Exists(AuditedMethods, m =>
            string.Equals(m, method, StringComparison.OrdinalIgnoreCase)))
            return false;

        return path.StartsWith("/api/admin/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds the canonical action string, e.g.
    ///   POST /api/admin/questions            → "questions.create"
    ///   PATCH /api/admin/users/u-123         → "users.update"
    ///   DELETE /api/admin/questions/q-5      → "questions.delete"
    /// Path patterns unknown to the mapper are surfaced verbatim
    /// (method + path-segment) so we never drop audit context.
    /// </summary>
    internal static string BuildAction(string method, string path)
    {
        var verb = method.ToUpperInvariant() switch
        {
            "POST"   => "create",
            "PUT"    => "update",
            "PATCH"  => "update",
            "DELETE" => "delete",
            _         => method.ToLowerInvariant(),
        };

        // /api/admin/{resource}/[id]/[subaction]
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // parts = ["api", "admin", resource, ...]
        if (parts.Length < 3)
            return $"admin.{verb}";

        var resource = parts[2].ToLowerInvariant();
        return $"{resource}.{verb}";
    }

    /// <summary>
    /// Best-effort target extraction: the segment immediately after the
    /// resource is typically its id (e.g. /api/admin/users/u-123 → users:u-123).
    /// Returns null for collection-level operations.
    /// </summary>
    internal static (string? TargetType, string? TargetId) ExtractTarget(string path, HttpRequest request)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return (null, null);

        var targetType = parts[2].ToLowerInvariant();

        // /api/admin/{resource}/{id}/[subaction]
        if (parts.Length >= 4)
        {
            var candidate = parts[3];
            // Skip action-y keywords often used at resource level.
            if (!IsActionKeyword(candidate))
                return (targetType, candidate);
        }

        // Query-param fallback (admin bulk endpoints sometimes carry
        // the target as ?id=…).
        if (request.Query.TryGetValue("id", out var qid) && !string.IsNullOrEmpty(qid))
            return (targetType, qid!);

        return (targetType, null);
    }

    private static bool IsActionKeyword(string segment) =>
        segment.Equals("bulk-retry", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("expand-corpus", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("recreate-from-reference", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("flow-state", StringComparison.OrdinalIgnoreCase);

    private static string BuildDescription(string method, string path, int status)
    {
        var result = status switch
        {
            >= 200 and < 300 => "succeeded",
            >= 300 and < 400 => "redirected",
            >= 400 and < 500 => "rejected",
            >= 500           => "errored",
            _                => "unknown",
        };
        return $"Admin {method} {path} {result} (status={status})";
    }

    private static (string UserId, string UserName, string Role, string TenantId) ExtractIdentity(ClaimsPrincipal user)
    {
        var userId   = user.FindFirstValue("user_id")
                    ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? user.FindFirstValue("sub")
                    ?? "anonymous";
        var userName = user.FindFirstValue(ClaimTypes.Name)
                    ?? user.FindFirstValue("name")
                    ?? user.FindFirstValue("email")
                    ?? string.Empty;
        var role     = user.FindFirstValue(ClaimTypes.Role)
                    ?? user.FindFirstValue("role")
                    ?? "unknown";
        var tenantId = user.FindFirstValue("school_id")
                    ?? user.FindFirstValue("tenant_id")
                    ?? string.Empty;

        return (userId, userName, role, tenantId);
    }
}
