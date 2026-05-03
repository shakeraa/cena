// =============================================================================
// Cena Platform — Bank-transfer admin endpoints (EPIC-PRR-I PRR-304)
//
// Finance-admin-only REST surface under /api/admin/subscriptions/bank-transfer:
//
//   GET  /pending                 — list Pending reservations for reconciliation
//   POST /{referenceCode}/confirm — mark payment received → Activate subscription
//
// Auth: AdminOnly. Pending listing is read-only; confirm is the finance
// action that flips a parent from "waiting for us to post their transfer"
// to "Active subscription". The service layer guards against:
//   - unknown reference codes
//   - already-confirmed (idempotent rejection with clear reason)
//   - already-expired
//   - parent activated via a different route in the meantime
//
// PRR-304 DoD items covered:
//   - Admin mark-received → Active: POST /confirm
//   - Listing for reconciliation UI: GET /pending
// =============================================================================

using System.Security.Claims;
using System.Text.Json.Serialization;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Subscriptions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using ErrorCategory = Cena.Infrastructure.Errors.ErrorCategory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>Response body for POST /api/admin/subscriptions/bank-transfer/{ref}/confirm.</summary>
public sealed record BankTransferConfirmResponseDto(
    [property: JsonPropertyName("referenceCode")] string ReferenceCode,
    [property: JsonPropertyName("activatedAt")] DateTimeOffset ActivatedAt,
    [property: JsonPropertyName("renewsAt")] DateTimeOffset RenewsAt,
    [property: JsonPropertyName("tier")] string Tier,
    [property: JsonPropertyName("grossAmountAgorot")] long GrossAmountAgorot);

/// <summary>
/// Wires the admin bank-transfer reconciliation endpoints. Both routes are
/// <see cref="CenaAuthPolicies.AdminOnly"/>.
/// </summary>
public static class BankTransferAdminEndpoints
{
    public const string PendingRoute = "/api/admin/subscriptions/bank-transfer/pending";
    public const string ConfirmRoute = "/api/admin/subscriptions/bank-transfer/{referenceCode}/confirm";

    /// <summary>Register the admin bank-transfer endpoints on the host's route builder.</summary>
    public static IEndpointRouteBuilder MapBankTransferAdminEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(PendingRoute, HandleListPendingAsync)
            .WithName("GetBankTransferPending")
            .WithTags("Admin", "Subscriptions", "BankTransfer")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<IReadOnlyList<BankTransferPendingItemDto>>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        app.MapPost(ConfirmRoute, HandleConfirmAsync)
            .WithName("PostBankTransferConfirm")
            .WithTags("Admin", "Subscriptions", "BankTransfer")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<BankTransferConfirmResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> HandleListPendingAsync(
        [FromServices] BankTransferReservationService service,
        CancellationToken ct)
    {
        var pending = await service.ListPendingAsync(ct);
        var items = pending
            .Select(d => new BankTransferPendingItemDto(
                ReferenceCode: d.ReferenceCode,
                AmountAgorot: d.AmountAgorot,
                Tier: d.Tier.ToString(),
                CreatedAt: d.CreatedAt,
                ExpiresAt: d.ExpiresAt))
            .ToList();
        return Results.Ok(items);
    }

    private static async Task<IResult> HandleConfirmAsync(
        HttpContext http,
        string referenceCode,
        [FromServices] BankTransferReservationService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referenceCode))
        {
            return Results.BadRequest(new CenaError(
                Code: "invalid_reference",
                Message: "ReferenceCode is required.",
                Category: ErrorCategory.Validation,
                Details: null,
                CorrelationId: null));
        }

        // Capture the admin's encrypted subject id for audit. Pulled from the
        // same claim every other admin-write endpoint uses.
        var adminId = http.User.FindFirstValue("sub")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "";
        if (string.IsNullOrWhiteSpace(adminId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var activation = await service.ConfirmAsync(referenceCode, adminId, ct);
            var doc = await service.GetAsync(referenceCode, ct);
            return Results.Ok(new BankTransferConfirmResponseDto(
                ReferenceCode: doc?.ReferenceCode ?? referenceCode,
                ActivatedAt: activation.ActivatedAt,
                RenewsAt: activation.RenewsAt,
                Tier: activation.Tier.ToString(),
                GrossAmountAgorot: activation.GrossAmountAgorot));
        }
        catch (BankTransferReservationException ex)
        {
            var status = ex.ReasonCode switch
            {
                "not_found" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest,
            };
            var category = ex.ReasonCode == "not_found"
                ? ErrorCategory.NotFound
                : ErrorCategory.Validation;
            return Results.Json(
                new CenaError(
                    Code: ex.ReasonCode,
                    Message: ex.Message,
                    Category: category,
                    Details: null,
                    CorrelationId: null),
                statusCode: status);
        }
    }
}
