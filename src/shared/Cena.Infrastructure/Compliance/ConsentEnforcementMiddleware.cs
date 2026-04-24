// =============================================================================
// Cena Platform -- GDPR Consent Enforcement Middleware (SEC-005)
// Enforces consent requirements for endpoints marked with [RequiresConsent].
// Blocks requests when consent is missing, with SIEM logging for security audit.
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// ASP.NET Core middleware that enforces GDPR consent requirements for endpoints
/// marked with <see cref="RequiresConsentAttribute"/>.
/// </summary>
/// <remarks>
/// Pipeline position: after authentication and authorization, before endpoint handlers.
/// Features:
/// - Detects [RequiresConsent] attribute on endpoints
/// - Extracts student ID from claims or route values
/// - Checks consent via IGdprConsentManager
/// - Caches consent checks per request to avoid repeated DB calls
/// - Supports skipConsentCheck query parameter for admin override
/// - SIEM-structured logging for security audit events
/// </remarks>
public sealed class ConsentEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ConsentEnforcementMiddleware> _logger;

    public ConsentEnforcementMiddleware(
        RequestDelegate next,
        ILogger<ConsentEnforcementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IGdprConsentManager consentManager)
    {
        // Get endpoint metadata to check for RequiresConsent attribute
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await _next(context);
            return;
        }

        var consentAttribute = endpoint.Metadata.GetMetadata<RequiresConsentAttribute>();
        if (consentAttribute is null)
        {
            await _next(context);
            return;
        }

        // Extract student ID from claims or route
        var studentId = ExtractStudentId(context);
        if (string.IsNullOrEmpty(studentId))
        {
            _logger.LogWarning(
                "[SIEM] ConsentEnforcementError: Cannot extract student ID for consent check on {Path}",
                context.Request.Path);
            
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "CENA_CONSENT_STUDENT_ID_MISSING",
                    message = "Student ID is required for consent verification."
                }
            });
            return;
        }

        // Check for admin skip override via query parameter
        if (context.Request.Query.TryGetValue("skipConsentCheck", out var skipValue) &&
            skipValue == "true")
        {
            // Verify the user has admin role before allowing skip
            var userRole = context.User.FindFirstValue(ClaimTypes.Role)
                        ?? context.User.FindFirstValue("role");
            
            if (IsAdminRole(userRole))
            {
                _logger.LogInformation(
                    "[SIEM] ConsentCheckSkipped: Admin {UserId} bypassed consent check on {Path}",
                    GetUserId(context),
                    context.Request.Path);
                
                await _next(context);
                return;
            }
        }

        // Get or create request-scoped consent cache
        var consentCache = GetConsentCache(context);
        
        // Get the purpose from the attribute via reflection (it's a private field in the existing attribute)
        var purpose = GetPurposeFromAttribute(consentAttribute);
        
        // Check cache first to avoid repeated DB calls
        // Default to high-privacy (isMinor=true) when age is unknown
        var cacheKey = $"{studentId}:{purpose}";
        if (!consentCache.TryGetValue(cacheKey, out var hasConsent))
        {
            // Query consent manager - default to high-privacy (isMinor=true) when age unknown
            hasConsent = await consentManager.HasConsentAsync(studentId, purpose, isMinor: true, context.RequestAborted);
            consentCache[cacheKey] = hasConsent;
        }

        if (!hasConsent)
        {
            // Consent missing - block the request
            _logger.LogWarning(
                "[SIEM] ConsentEnforcementBlocked: Student {StudentId} lacks consent for {Purpose} on {Path}",
                studentId,
                purpose,
                context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.Append("X-Consent-Required", "true");
            
            var errorResponse = new
            {
                error = "consent_required",
                purpose = purpose.ToString().ToLowerInvariant()
            };
            
            await context.Response.WriteAsJsonAsync(errorResponse);
            return;
        }

        // Consent verified - allow request to proceed
        await _next(context);
    }

    /// <summary>
    /// Extracts student ID from HTTP context claims or route values.
    /// </summary>
    private static string? ExtractStudentId(HttpContext context)
    {
        // First try to get from user claims (preferred for authenticated requests)
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirstValue("user_id")
                      ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            if (!string.IsNullOrEmpty(userId))
            {
                return userId;
            }
        }

        // Try route values
        if (context.Request.RouteValues.TryGetValue("studentId", out var routeStudentId)
            && routeStudentId is string rsId && !string.IsNullOrEmpty(rsId))
        {
            return rsId;
        }

        if (context.Request.RouteValues.TryGetValue("id", out var routeId)
            && routeId is string rId && !string.IsNullOrEmpty(rId))
        {
            return rId;
        }

        // Try query string
        if (context.Request.Query.TryGetValue("studentId", out var qStudentId)
            && !string.IsNullOrEmpty(qStudentId))
        {
            return qStudentId.ToString();
        }

        if (context.Request.Query.TryGetValue("student_id", out var qStudentIdUnderscore)
            && !string.IsNullOrEmpty(qStudentIdUnderscore))
        {
            return qStudentIdUnderscore.ToString();
        }

        return null;
    }

    /// <summary>
    /// Gets the current user's ID from claims for logging purposes.
    /// </summary>
    private static string GetUserId(HttpContext context)
    {
        return context.User.FindFirstValue("user_id")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "anonymous";
    }

    /// <summary>
    /// Determines if the user role qualifies as an administrator.
    /// </summary>
    private static bool IsAdminRole(string? role)
    {
        if (string.IsNullOrEmpty(role))
            return false;

        return role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
            || role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)
            || role.EndsWith("Admin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets or creates the request-scoped consent cache dictionary.
    /// </summary>
    private static Dictionary<string, bool> GetConsentCache(HttpContext context)
    {
        const string cacheKey = "Cena_ConsentCache";
        
        if (context.Items.TryGetValue(cacheKey, out var existing) && existing is Dictionary<string, bool> dict)
        {
            return dict;
        }

        var newCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        context.Items[cacheKey] = newCache;
        return newCache;
    }

    /// <summary>
    /// Extracts the ProcessingPurpose from the RequiresConsentAttribute via reflection.
    /// </summary>
    private static ProcessingPurpose GetPurposeFromAttribute(RequiresConsentAttribute attribute)
    {
        // Try to get the purpose field via reflection
        var fieldInfo = typeof(RequiresConsentAttribute).GetField("_purpose", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (fieldInfo?.GetValue(attribute) is ProcessingPurpose purpose)
        {
            return purpose;
        }

        // Fallback: try property
        var propInfo = typeof(RequiresConsentAttribute).GetProperty("Purpose",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        
        if (propInfo?.GetValue(attribute) is ProcessingPurpose propPurpose)
        {
            return propPurpose;
        }

        // Default fallback
        return ProcessingPurpose.BehavioralAnalytics;
    }


}

/// <summary>
/// Extension methods for registering <see cref="ConsentEnforcementMiddleware"/> in the ASP.NET Core pipeline.
/// </summary>
public static class ConsentEnforcementMiddlewareExtensions
{
    /// <summary>
    /// Adds the <see cref="ConsentEnforcementMiddleware"/> to the request pipeline.
    /// This middleware enforces GDPR consent requirements for endpoints marked with [RequiresConsent].
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> instance.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for method chaining.</returns>
    /// <remarks>
    /// Recommended pipeline position: after authentication and authorization, before endpoint handlers.
    /// Example:
    /// <code>
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseConsentEnforcement();
    /// app.MapControllers();
    /// </code>
    /// </remarks>
    public static IApplicationBuilder UseConsentEnforcement(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ConsentEnforcementMiddleware>();
    }
}
