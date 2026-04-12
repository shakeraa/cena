// =============================================================================
// Cena Platform -- RequiresConsent Attribute (SEC-012)
// Endpoint filter attribute for ASP.NET Core Minimal APIs that enforces
// GDPR consent validation before allowing access to processing-sensitive endpoints.
// =============================================================================

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Attribute that enforces GDPR consent validation for Minimal API endpoints.
/// Implements <see cref="IEndpointFilter"/> for ASP.NET Core 7+ Minimal APIs.
/// </summary>
/// <remarks>
/// This filter extracts the student ID from route parameters, query string, or request body,
/// then validates that the student has granted consent for the specified processing purpose.
/// If consent is missing, returns 403 Forbidden with a structured error response.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RequiresConsentAttribute : Attribute, IEndpointFilter
{
    private readonly ProcessingPurpose _purpose;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiresConsentAttribute"/> class.
    /// </summary>
    /// <param name="purpose">The processing purpose that requires consent.</param>
    public RequiresConsentAttribute(ProcessingPurpose purpose)
    {
        _purpose = purpose;
    }

    /// <summary>
    /// Invokes the consent validation filter.
    /// </summary>
    /// <param name="context">The endpoint filter invocation context.</param>
    /// <param name="next">The next filter in the pipeline.</param>
    /// <returns>The result of the endpoint invocation or a 403 response if consent is missing.</returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<RequiresConsentAttribute>>();
        var consentManager = httpContext.RequestServices.GetRequiredService<IGdprConsentManager>();

        // Extract studentId from route/query/body
        var studentId = ExtractStudentId(context, httpContext);

        if (string.IsNullOrEmpty(studentId))
        {
            logger.LogWarning("[SIEM] StudentIdNotFound: Cannot validate consent - student ID not found in request");
            return Results.Unauthorized();
        }

        // Check consent - use high-privacy default (isMinor=true) when age is unknown
        var hasConsent = await consentManager.HasConsentAsync(studentId, _purpose, isMinor: true, httpContext.RequestAborted);

        if (!hasConsent)
        {
            logger.LogWarning(
                "[SIEM] ConsentRequiredButMissing: Student {StudentId} lacks consent for {Purpose}",
                studentId,
                _purpose);

            var errorResponse = new
            {
                error = "consent_required",
                purpose = _purpose.ToString().ToLowerInvariant()
            };

            httpContext.Response.Headers.Append("X-Consent-Required", "true");
            return Results.Json(errorResponse, statusCode: StatusCodes.Status403Forbidden);
        }

        // Consent present - continue to handler
        return await next(context);
    }

    /// <summary>
    /// Extracts the student ID from route parameters, query string, or request body.
    /// </summary>
    /// <param name="context">The endpoint filter invocation context.</param>
    /// <param name="httpContext">The HTTP context.</param>
    /// <returns>The student ID if found; otherwise, null.</returns>
    private static string? ExtractStudentId(EndpointFilterInvocationContext context, HttpContext httpContext)
    {
        // First, try to get from claims (most common for authenticated endpoints)
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var claimValue = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;

            if (!string.IsNullOrEmpty(claimValue))
            {
                return claimValue;
            }
        }

        // Try route parameters
        if (httpContext.Request.RouteValues.TryGetValue("studentId", out var routeValue) &&
            routeValue is string routeStudentId &&
            !string.IsNullOrEmpty(routeStudentId))
        {
            return routeStudentId;
        }

        // Try query string
        var queryStudentId = httpContext.Request.Query["studentId"].FirstOrDefault();
        if (!string.IsNullOrEmpty(queryStudentId))
        {
            return queryStudentId;
        }

        // Try to extract from common DTO patterns in arguments
        for (var i = 0; i < context.Arguments.Count; i++)
        {
            var arg = context.Arguments[i];
            if (arg is null)
            {
                continue;
            }

            // Check for common property patterns via reflection
            var argType = arg.GetType();
            var studentIdProp = argType.GetProperty("StudentId")
                ?? argType.GetProperty("studentId")
                ?? argType.GetProperty("StudentID");

            if (studentIdProp?.PropertyType == typeof(string))
            {
                var value = studentIdProp.GetValue(arg) as string;
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            // Also check for Id property if the type name suggests it's student-related
            if (argType.Name.Contains("Student", StringComparison.OrdinalIgnoreCase))
            {
                var idProp = argType.GetProperty("Id")
                    ?? argType.GetProperty("id");

                if (idProp?.PropertyType == typeof(string))
                {
                    var value = idProp.GetValue(arg) as string;
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }
        }

        return null;
    }


}

/// <summary>
/// Extension methods for registering consent requirements on endpoints.
/// </summary>
public static class ConsentEndpointExtensions
{
    /// <summary>
    /// Adds a <see cref="RequiresConsentAttribute"/> filter to the endpoint.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the endpoint convention builder.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="purpose">The processing purpose that requires consent.</param>
    /// <returns>The endpoint convention builder for chaining.</returns>
    /// <example>
    /// <code>
    /// app.MapGet("/api/social/leaderboard", GetLeaderboard)
    ///     .RequireConsent(ProcessingPurpose.PeerComparison);
    /// </code>
    /// </example>
    public static TBuilder RequireConsent<TBuilder>(this TBuilder builder, ProcessingPurpose purpose)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(new RequiresConsentAttribute(purpose));
        return builder;
    }
}
