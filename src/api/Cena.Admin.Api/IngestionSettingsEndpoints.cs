// =============================================================================
// Cena Platform -- Ingestion Settings Endpoints
// CRUD + test endpoints for ingestion configuration (cloud dirs, email,
// messaging channels, pipeline defaults).
// =============================================================================

using System.Security.Claims;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api;

public static class IngestionSettingsEndpoints
{
    public static IEndpointRouteBuilder MapIngestionSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/ingestion-settings")
            .WithTags("Ingestion Settings")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .RequireRateLimiting("api");

        // GET — retrieve current settings (or defaults)
        group.MapGet("/", async (IIngestionSettingsService service) =>
        {
            var settings = await service.GetSettingsAsync();
            return Results.Ok(settings);
        }).WithName("GetIngestionSettings");

        // PUT — save full settings document
        group.MapPut("/", async (
            IngestionSettingsDocument settings,
            HttpContext ctx,
            IIngestionSettingsService service) =>
        {
            var userId = ctx.User.FindFirstValue("user_id")
                ?? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? ctx.User.FindFirstValue("sub")
                ?? "unknown";

            try
            {
                var updated = await service.UpdateSettingsAsync(settings, userId);
                return Results.Ok(updated);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithName("UpdateIngestionSettings");

        // POST — test email connection
        group.MapPost("/test-email", async (
            TestEmailRequest request,
            IIngestionSettingsService service) =>
        {
            var result = await service.TestEmailConnectionAsync(request.Config, request.Password);
            return Results.Ok(new
            {
                connected = result.Success,
                error = result.Error,
                details = result.Details
            });
        }).WithName("TestEmailIngestionConnection");

        // POST — test cloud directory connection
        group.MapPost("/test-cloud-dir", async (
            TestCloudDirRequest request,
            IIngestionSettingsService service) =>
        {
            var result = await service.TestCloudDirAsync(request.Config, request.SecretKey);
            return Results.Ok(new
            {
                connected = result.Success,
                error = result.Error,
                details = result.Details
            });
        }).WithName("TestCloudDirConnection");

        return app;
    }
}

// Request DTOs for test endpoints
public sealed record TestEmailRequest(
    EmailIngestionConfig Config,
    string? Password);

public sealed record TestCloudDirRequest(
    CloudDirConfig Config,
    string? SecretKey);
