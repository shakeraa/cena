// =============================================================================
// Cena Platform — POST /api/admin/ai/test-connection
//
// Probes the configured Anthropic API key + model with a real 1-token
// messages.create call. Returns { connected, error?, details? } where
// `error` is the human-readable message and `details` is the stable
// category code (AUTH_FAILED / MODEL_NOT_FOUND / RATE_LIMITED /
// UPSTREAM_ERROR / NETWORK_UNREACHABLE / TIMEOUT / CONFIG_MISSING_KEY /
// UNSUPPORTED_PROVIDER / UNEXPECTED_ERROR) — the SPA uses this category
// to render an actionable hint instead of a bare "Failed" badge.
//
// Extracted from AdminApiEndpoints.cs into a standalone endpoint so the
// route-smoke test can mount just this one route in isolation, per the
// route-smoke memory rule. The previous in-group registration produced a
// "Failure to infer one or more parameters" error in the test-app
// endpoint enumeration because the group's other endpoints depended on
// services that are out of scope for this fix.
//
// AuthZ: ModeratorOrAbove. RateLimiter: "ai" (shared with the rest of
// the AI generation surface).
// =============================================================================

using Cena.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.AiSettings;

public static class AiTestConnectionEndpoint
{
    public const string Route = "/api/admin/ai/test-connection";

    public static IEndpointRouteBuilder MapAiTestConnectionEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, HandleAsync)
            .WithName("TestAiConnection")
            .WithTags("AI Generation")
            .RequireAuthorization(CenaAuthPolicies.ModeratorOrAbove)
            .RequireRateLimiting("ai");
        return app;
    }

    internal static async Task<IResult> HandleAsync(
        [FromBody] TestConnectionRequest request,
        IAiGenerationService service,
        CancellationToken ct)
    {
        var result = await service.TestConnectionAsync(request.Provider, ct);
        return Results.Ok(new
        {
            connected = result.Success,
            error = result.Error,
            details = result.Details,
        });
    }
}
