// =============================================================================
// Cena Platform -- FERPA Compliance: Student Data Access Audit Middleware
// REV-013.1: Logs every access to student-identifiable data endpoints
//
// Pipeline position: after auth, before endpoint handlers.
// Non-blocking: logs asynchronously after the response completes.
// =============================================================================

using System.Security.Claims;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

public sealed class StudentDataAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<StudentDataAuditMiddleware> _logger;

    private static readonly HashSet<string> AuditedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/admin/mastery",
        "/api/admin/focus",
        "/api/admin/tutoring",
        "/api/admin/outreach",
        "/api/admin/cultural",
        "/api/v1/mastery"
    };

    public StudentDataAuditMiddleware(
        RequestDelegate next,
        ILogger<StudentDataAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IDocumentSession session)
    {
        var path = context.Request.Path.Value ?? "";

        if (!AuditedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Extract accessor identity from claims (set by Firebase auth + CenaClaimsTransformer)
        var uid = context.User.FindFirstValue("user_id")
               ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = context.User.FindFirstValue("cena_role")
                ?? context.User.FindFirstValue(ClaimTypes.Role);
        var school = context.User.FindFirstValue("school_id");

        // Extract student ID from route or query string if present
        var studentId = ExtractStudentId(context.Request);

        // Let the request proceed first -- we need the status code
        await _next(context);

        // Log after response to capture status code
        try
        {
            session.Store(new StudentRecordAccessLog
            {
                Id = Guid.NewGuid(),
                AccessedAt = DateTimeOffset.UtcNow,
                AccessedBy = uid ?? "anonymous",
                AccessorRole = role ?? "unknown",
                AccessorSchool = school,
                StudentId = studentId,
                Endpoint = path,
                HttpMethod = context.Request.Method,
                StatusCode = context.Response.StatusCode,
                IpAddress = context.Connection.RemoteIpAddress?.ToString()
            });

            await session.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit logging must never break the request pipeline
            _logger.LogError(ex, "Failed to write student data access audit log for {Path}", path);
        }
    }

    /// <summary>
    /// Extracts student ID from route values or query string parameters.
    /// Looks for common parameter names: studentId, student_id, id (on student-specific routes).
    /// </summary>
    private static string? ExtractStudentId(HttpRequest request)
    {
        // Check route values first
        if (request.RouteValues.TryGetValue("studentId", out var routeStudentId)
            && routeStudentId is string rsId && !string.IsNullOrEmpty(rsId))
        {
            return rsId;
        }

        if (request.RouteValues.TryGetValue("id", out var routeId)
            && routeId is string rId && !string.IsNullOrEmpty(rId))
        {
            return rId;
        }

        // Check query string
        if (request.Query.TryGetValue("studentId", out var qStudentId)
            && !string.IsNullOrEmpty(qStudentId))
        {
            return qStudentId;
        }

        if (request.Query.TryGetValue("student_id", out var qStudentIdUnderscore)
            && !string.IsNullOrEmpty(qStudentIdUnderscore))
        {
            return qStudentIdUnderscore;
        }

        return null;
    }
}
