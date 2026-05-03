// =============================================================================
// Cena Platform — ApplicableDiscountEndpoint (per-user discount-codes feature)
//
// GET /api/me/applicable-discount — returns the single active discount for
// the caller's email, or 404 when none. Email comes from the auth claim
// (`email` standard claim) and is normalised server-side (lowercase + Gmail
// dot/+ strip) so an admin who issued to alice@gmail.com matches a parent
// who registered as Alice+study@gmail.com.
//
// Auth: same `RequireAuthorization` as the rest of /api/me/*. Anonymous
// callers receive 401 from the auth middleware before reaching the handler.
//
// Lives in its own file so MeEndpoints.cs stays under the 500-LOC ratchet
// (per ADR-0012 + FileSize500LocTest architecture gate).
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Cena.Infrastructure.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

/// <summary>
/// Wires the GET /api/me/applicable-discount endpoint. Pricing page calls
/// this on mount to render the personal-discount banner + adjusted prices.
/// </summary>
public static class ApplicableDiscountEndpoint
{
    public const string Route = "/api/me/applicable-discount";

    /// <summary>Register the endpoint on the host's route builder.</summary>
    public static IEndpointRouteBuilder MapApplicableDiscountEndpoint(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, HandleAsync)
            .WithName("GetApplicableDiscount")
            .WithTags("Me", "Subscriptions", "Discounts")
            .RequireAuthorization()
            .Produces<ApplicableDiscountDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext ctx,
        [FromServices] DiscountAssignmentService discountService,
        CancellationToken ct)
    {
        var email = ctx.User.FindFirst("email")?.Value
            ?? ctx.User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            // No email on the token — treat as "no applicable discount"
            // rather than 401 so the SPA renders the no-discount path
            // gracefully on legacy/anonymous tokens.
            return Results.NotFound();
        }
        var summary = await discountService.FindActiveForEmailAsync(email, ct);
        if (summary is null) return Results.NotFound();
        return Results.Ok(new ApplicableDiscountDto(
            AssignmentId: summary.AssignmentId,
            DiscountKind: summary.Kind.ToString(),
            DiscountValue: summary.Value,
            DurationMonths: summary.DurationMonths,
            Status: summary.Status.ToString()));
    }
}
