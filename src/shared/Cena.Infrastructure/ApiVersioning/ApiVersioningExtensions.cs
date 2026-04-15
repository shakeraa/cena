// =============================================================================
// Cena Platform — REST API Versioning Extensions (RDY-010)
// Shared API versioning configuration and deprecated-route redirect middleware.
// =============================================================================

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.ApiVersioning;

public static class ApiVersioningExtensions
{
    /// <summary>
    /// Adds Asp.Versioning.Http with Cena defaults: v1 is current,
    /// assume default when unspecified, and report API versions.
    /// </summary>
    public static IServiceCollection AddCenaApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        });

        return services;
    }

    /// <summary>
    /// Redirects unversioned <c>/api/...</c> requests to <c>/api/v1/...</c>
    /// and emits Sunset + Deprecation headers. Logs usage for analytics.
    /// </summary>
    public static IApplicationBuilder UseDeprecatedApiRedirect(this IApplicationBuilder app)
    {
        return app.UseMiddleware<DeprecatedApiRedirectMiddleware>();
    }
}

public sealed class DeprecatedApiRedirectMiddleware(RequestDelegate next, ILogger<DeprecatedApiRedirectMiddleware> logger)
{
    // RDY-010: 6-month sunset from 2026-04-15
    private const string SunsetDate = "Mon, 15 Oct 2026 00:00:00 GMT";
    private const string DeprecationDate = "Sun, 15 Oct 2026 00:00:00 GMT";

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;

        if (path is not null
            && path.StartsWith("/api/", StringComparison.Ordinal)
            && !path.StartsWith("/api/v1/", StringComparison.Ordinal)
            && !path.StartsWith("/api/v", StringComparison.Ordinal))
        {
            var newPath = "/api/v1" + path["/api".Length..];

            logger.LogInformation(
                "RDY-010: Deprecated API path accessed: {Method} {OldPath} -> {NewPath}",
                context.Request.Method,
                path,
                newPath);

            context.Response.Headers["Deprecation"] = DeprecationDate;
            context.Response.Headers["Sunset"] = SunsetDate;
            context.Response.Redirect(newPath, permanent: false);
            return;
        }

        await next(context);
    }
}
