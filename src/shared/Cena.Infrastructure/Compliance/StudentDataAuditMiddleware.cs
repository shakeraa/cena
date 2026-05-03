// =============================================================================
// Cena Platform -- FERPA Compliance: Student Data Access Audit Middleware
// REV-013.1: Logs every access to student-identifiable data endpoints
//
// Pipeline position: after auth, before endpoint handlers.
// Non-blocking: logs asynchronously after the response completes.
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Network;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

public sealed class StudentDataAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<StudentDataAuditMiddleware> _logger;

    // FIND-privacy-012: Expanded FERPA audit coverage for all student data endpoints
    private static readonly HashSet<string> AuditedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        // Admin API - Student data access
        "/api/admin/mastery",
        "/api/admin/focus",
        "/api/admin/tutoring",
        "/api/admin/outreach",
        "/api/admin/cultural",
        "/api/admin/students",           // FIND-privacy-012: Student list/detail
        "/api/admin/analytics",          // FIND-privacy-012: Analytics endpoints
        "/api/admin/classroom",          // FIND-privacy-012: Classroom/student associations
        "/api/admin/insights",           // FIND-privacy-012: Student insights
        "/api/admin/experiments",        // FIND-privacy-012: Experiment assignments
        "/api/admin/content",            // FIND-privacy-012: Content with student progress
        "/api/admin/social",             // FIND-privacy-012: Social/feed data
        "/api/admin/compliance",         // FIND-privacy-012: Compliance exports
        
        // Student API - Self-access (also audited for completeness)
        "/api/v1/mastery",
        "/api/me/gdpr/export",           // FIND-privacy-012: Data exports
        "/api/me/profile",
        "/api/sessions",                 // FIND-privacy-012: Session history
        "/api/analytics",                // FIND-privacy-012: Student analytics
        
        // Actor API - Direct student data access
        "/api/actors/student",           // FIND-privacy-012: Actor student queries
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
        var role = context.User.FindFirstValue(ClaimTypes.Role)
                ?? context.User.FindFirstValue("role");
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
                IpAddress = IpAddressNormalizer.Normalize(context.Connection.RemoteIpAddress)
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
    /// Extracts student ID from route values, query string, or request body.
    /// FIND-privacy-012: Expanded to cover more parameter patterns.
    /// </summary>
    private static string? ExtractStudentId(HttpRequest request)
    {
        // Check route values first (common patterns)
        var routeKeys = new[] { "studentId", "student_id", "studentId", "id", "userId", "user_id" };
        foreach (var key in routeKeys)
        {
            if (request.RouteValues.TryGetValue(key, out var routeValue)
                && routeValue is string rsId && !string.IsNullOrEmpty(rsId))
            {
                return rsId;
            }
        }

        // Check query string (common patterns)
        var queryKeys = new[] { "studentId", "student_id", "studentId", "userId", "user_id", "uid" };
        foreach (var key in queryKeys)
        {
            if (request.Query.TryGetValue(key, out var qValue) && !string.IsNullOrEmpty(qValue))
            {
                return qValue;
            }
        }

        // Check if path contains student ID pattern (e.g., /api/admin/students/{id}/...)
        var path = request.Path.Value ?? "";
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("students", StringComparison.OrdinalIgnoreCase) &&
                segments[i + 1].Length > 10) // Likely a student ID
            {
                return segments[i + 1];
            }
        }

        return null;
    }
}
