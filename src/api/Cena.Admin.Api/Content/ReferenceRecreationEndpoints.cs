// =============================================================================
// Cena Platform — Reference Recreation Endpoint (RDY-019b, Phase 3.2)
//
// POST /api/admin/content/recreate-from-reference   (SuperAdminOnly + ai RL)
//
// Thin HTTP surface on top of ReferenceCalibratedGenerationService.RecreateAsync.
// Defaults DryRun=true — operators must explicitly set it false to spend.
// All validation + orchestration lives in the service.
// =============================================================================

using System.Security.Claims;
using Cena.Admin.Api.Content;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Content;

public static class ReferenceRecreationEndpoints
{
    public static IEndpointRouteBuilder MapReferenceRecreationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/content")
            .WithTags("Reference Recreation")
            .RequireAuthorization(CenaAuthPolicies.SuperAdminOnly)
            .RequireRateLimiting("ai");

        group.MapPost("/recreate-from-reference", async (
            ReferenceRecreationRequest request,
            ClaimsPrincipal user,
            IReferenceCalibratedGenerationService service,
            CancellationToken ct) =>
        {
            if (request is null)
                return Results.Json(new CenaError(
                    "invalid_body",
                    "ReferenceRecreationRequest body required.",
                    ErrorCategory.Validation, null, null),
                    statusCode: StatusCodes.Status400BadRequest);

            var startedBy = user.FindFirst("user_id")?.Value
                            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? user.FindFirst("sub")?.Value
                            ?? "unknown-super-admin";

            try
            {
                var response = await service.RecreateAsync(request, startedBy, ct);
                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(new CenaError(
                    "invalid_request", ex.Message,
                    ErrorCategory.Validation, null, null),
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (FileNotFoundException ex)
            {
                return Results.Json(new CenaError(
                    "missing_analysis", ex.Message,
                    ErrorCategory.Validation, null, null),
                    statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("RecreateFromReference")
        .Produces<ReferenceRecreationResponse>(StatusCodes.Status200OK)
        .Produces<CenaError>(StatusCodes.Status400BadRequest)
        .Produces<CenaError>(StatusCodes.Status401Unauthorized)
        .Produces<CenaError>(StatusCodes.Status403Forbidden)
        .Produces<CenaError>(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
