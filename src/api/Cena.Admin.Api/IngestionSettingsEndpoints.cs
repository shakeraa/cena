// =============================================================================
// Cena Platform -- Ingestion Settings Endpoints
// CRUD + test endpoints for ingestion configuration (cloud dirs, email,
// messaging channels, pipeline defaults).
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Cena.Infrastructure.Errors;

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
        }).WithName("GetIngestionSettings")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

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
        }).WithName("UpdateIngestionSettings")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST — test email connection.
        // Accepts either {config, password} (TestEmailRequest) or a bare EmailIngestionConfig
        // — the SPA currently posts the bare shape; tolerate both so existing callers don't break.
        group.MapPost("/test-email", async (HttpContext ctx, IIngestionSettingsService service) =>
        {
            var (config, password) = await ReadEmailBodyAsync(ctx);
            if (config is null)
                return Results.BadRequest(new { error = "Invalid request body: missing email config." });
            var result = await service.TestEmailConnectionAsync(config, password);
            return Results.Ok(new
            {
                connected = result.Success,
                error = result.Error,
                details = result.Details
            });
        }).WithName("TestEmailIngestionConnection")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        // POST — test cloud directory connection.
        // Accepts either {config, secretKey} (TestCloudDirRequest) or a bare CloudDirConfig
        // — the SPA currently posts the bare shape; tolerate both so existing callers don't break.
        group.MapPost("/test-cloud-dir", async (HttpContext ctx, IIngestionSettingsService service) =>
        {
            var (config, secretKey) = await ReadCloudDirBodyAsync(ctx);
            if (config is null)
                return Results.BadRequest(new { error = "Invalid request body: missing cloud directory config." });
            var result = await service.TestCloudDirAsync(config, secretKey);
            return Results.Ok(new
            {
                connected = result.Success,
                error = result.Error,
                details = result.Details
            });
        }).WithName("TestCloudDirConnection")
    .Produces<object>(StatusCodes.Status200OK)
    .Produces<CenaError>(StatusCodes.Status400BadRequest)
    .Produces<CenaError>(StatusCodes.Status401Unauthorized)
    .Produces<CenaError>(StatusCodes.Status429TooManyRequests)
    .Produces<CenaError>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static readonly JsonSerializerOptions BodyJsonOptions = new(JsonSerializerDefaults.Web);

    // Reads either {config: {...}, secretKey?: "..."} or a bare CloudDirConfig from the request body.
    private static async Task<(CloudDirConfig? config, string? secretKey)> ReadCloudDirBodyAsync(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            if (root.TryGetProperty("config", out var cfgEl) && cfgEl.ValueKind == JsonValueKind.Object)
            {
                var cfg = cfgEl.Deserialize<CloudDirConfig>(BodyJsonOptions);
                string? secret = null;
                if (root.TryGetProperty("secretKey", out var skEl) && skEl.ValueKind == JsonValueKind.String)
                    secret = skEl.GetString();
                return (cfg, secret);
            }

            // Bare CloudDirConfig
            return (root.Deserialize<CloudDirConfig>(BodyJsonOptions), null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    // Reads either {config: {...}, password?: "..."} or a bare EmailIngestionConfig from the request body.
    private static async Task<(EmailIngestionConfig? config, string? password)> ReadEmailBodyAsync(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);

            if (root.TryGetProperty("config", out var cfgEl) && cfgEl.ValueKind == JsonValueKind.Object)
            {
                var cfg = cfgEl.Deserialize<EmailIngestionConfig>(BodyJsonOptions);
                string? pwd = null;
                if (root.TryGetProperty("password", out var pwEl) && pwEl.ValueKind == JsonValueKind.String)
                    pwd = pwEl.GetString();
                return (cfg, pwd);
            }

            return (root.Deserialize<EmailIngestionConfig>(BodyJsonOptions), null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
