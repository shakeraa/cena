// =============================================================================
// Cena Platform — Discount admin endpoints (per-user discount-codes feature)
//
// Admin-only REST surface under /api/admin/discounts:
//
//   POST   /api/admin/discounts                  — issue a personal discount
//   GET    /api/admin/discounts                  — list recent (or by ?email=...)
//   DELETE /api/admin/discounts/{assignmentId}   — revoke
//
// Auth: AdminOnly (matches BankTransferAdminEndpoints, AlphaMigrationEndpoints,
// UnitEconomicsAdminEndpoints). Every write is audit-logged via the
// AdminActionAuditMiddleware (all /api/admin/** writes), so the admin
// endpoint doesn't need to roll its own audit log; the structured fields
// inside the event payload (issuer, reason, revoker, reason) carry the
// domain-level audit trail.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Api.Contracts.Subscriptions;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Errors;
using ErrorCategory = Cena.Infrastructure.Errors.ErrorCategory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>
/// Wires the admin discount endpoints. All routes are
/// <see cref="CenaAuthPolicies.AdminOnly"/>.
/// </summary>
public static class DiscountAdminEndpoints
{
    public const string IssueRoute = "/api/admin/discounts";
    public const string ListRoute = "/api/admin/discounts";
    public const string RevokeRoute = "/api/admin/discounts/{assignmentId}";

    /// <summary>Default pagination size for the list endpoint.</summary>
    public const int DefaultListLimit = 100;

    /// <summary>Register the admin discount endpoints on the host's route builder.</summary>
    public static IEndpointRouteBuilder MapDiscountAdminEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapPost(IssueRoute, HandleIssueAsync)
            .WithName("PostAdminDiscountIssue")
            .WithTags("Admin", "Subscriptions", "Discounts")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<DiscountIssueResponseDto>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status409Conflict);

        app.MapGet(ListRoute, HandleListAsync)
            .WithName("GetAdminDiscountList")
            .WithTags("Admin", "Subscriptions", "Discounts")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces<IReadOnlyList<DiscountAssignmentDto>>(StatusCodes.Status200OK)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden);

        app.MapDelete(RevokeRoute, HandleRevokeAsync)
            .WithName("DeleteAdminDiscountRevoke")
            .WithTags("Admin", "Subscriptions", "Discounts")
            .RequireAuthorization(CenaAuthPolicies.AdminOnly)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<CenaError>(StatusCodes.Status400BadRequest)
            .Produces<CenaError>(StatusCodes.Status401Unauthorized)
            .Produces<CenaError>(StatusCodes.Status403Forbidden)
            .Produces<CenaError>(StatusCodes.Status404NotFound)
            .Produces<CenaError>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> HandleIssueAsync(
        HttpContext http,
        [FromBody] DiscountIssueRequestDto body,
        [FromServices] DiscountAssignmentService service,
        CancellationToken ct)
    {
        if (body is null)
        {
            return BadRequest("invalid_body", "Request body is required.");
        }
        if (!Enum.TryParse<DiscountKind>(body.DiscountKind, ignoreCase: true, out var kind)
            || (kind != DiscountKind.PercentOff && kind != DiscountKind.AmountOff))
        {
            return BadRequest("invalid_discount_kind",
                "discountKind must be 'PercentOff' or 'AmountOff'.", "discountKind");
        }

        var adminId = AdminSubjectId(http);
        if (string.IsNullOrWhiteSpace(adminId)) return Results.Unauthorized();

        try
        {
            var result = await service.IssueAsync(
                rawTargetEmail: body.TargetEmail,
                kind: kind,
                value: body.DiscountValue,
                durationMonths: body.DurationMonths,
                issuedByAdminSubjectIdEncrypted: adminId,
                reason: body.Reason ?? string.Empty,
                ct: ct);
            return Results.Ok(new DiscountIssueResponseDto(
                AssignmentId: result.AssignmentId,
                TargetEmailNormalized: EmailNormalizer.Normalize(body.TargetEmail),
                PromotionCodeString: result.PromotionCodeString,
                Status: DiscountStatus.Issued.ToString()));
        }
        catch (DiscountAssignmentException ex)
        {
            var status = ex.ReasonCode switch
            {
                "discount_already_active" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            var category = ex.ReasonCode == "discount_already_active"
                ? ErrorCategory.Conflict
                : ErrorCategory.Validation;
            var details = ex.Field is null
                ? null
                : new Dictionary<string, object> { ["field"] = ex.Field };
            return Results.Json(
                new CenaError(
                    Code: ex.ReasonCode,
                    Message: ex.Message,
                    Category: category,
                    Details: details,
                    CorrelationId: null),
                statusCode: status);
        }
    }

    private static async Task<IResult> HandleListAsync(
        [FromQuery] string? email,
        [FromQuery] int? limit,
        [FromServices] DiscountAssignmentService service,
        CancellationToken ct)
    {
        IReadOnlyList<DiscountAssignmentSummary> rows;
        if (!string.IsNullOrWhiteSpace(email))
        {
            rows = await service.ListByEmailAsync(email, ct);
        }
        else
        {
            var cap = (limit is > 0 and <= 1000) ? limit.Value : DefaultListLimit;
            rows = await service.ListRecentAsync(cap, ct);
        }
        var dtos = rows.Select(r => new DiscountAssignmentDto(
            AssignmentId: r.AssignmentId,
            TargetEmailNormalized: r.TargetEmailNormalized,
            Status: r.Status.ToString(),
            DiscountKind: r.Kind.ToString(),
            DiscountValue: r.Value,
            DurationMonths: r.DurationMonths,
            Reason: r.Reason,
            IssuedAt: r.IssuedAt,
            RedeemedAt: r.RedeemedAt,
            RevokedAt: r.RevokedAt)).ToList();
        return Results.Ok(dtos);
    }

    private static async Task<IResult> HandleRevokeAsync(
        HttpContext http,
        string assignmentId,
        [FromQuery] string? reason,
        [FromServices] DiscountAssignmentService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            return BadRequest("invalid_assignment_id", "Assignment id is required.");
        }
        var adminId = AdminSubjectId(http);
        if (string.IsNullOrWhiteSpace(adminId)) return Results.Unauthorized();

        try
        {
            await service.RevokeAsync(
                assignmentId: assignmentId,
                revokedByAdminSubjectIdEncrypted: adminId,
                reason: string.IsNullOrWhiteSpace(reason) ? "admin_revoked" : reason,
                ct: ct);
            return Results.NoContent();
        }
        catch (DiscountAssignmentException ex)
        {
            var status = ex.ReasonCode switch
            {
                "not_found" => StatusCodes.Status404NotFound,
                "already_redeemed" => StatusCodes.Status409Conflict,
                "already_revoked" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            };
            var category = ex.ReasonCode == "not_found"
                ? ErrorCategory.NotFound
                : ex.ReasonCode is "already_redeemed" or "already_revoked"
                    ? ErrorCategory.Conflict
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

    private static IResult BadRequest(string code, string message, string? field = null)
    {
        var details = field is null
            ? null
            : new Dictionary<string, object> { ["field"] = field };
        return Results.BadRequest(new CenaError(
            Code: code,
            Message: message,
            Category: ErrorCategory.Validation,
            Details: details,
            CorrelationId: null));
    }

    private static string AdminSubjectId(HttpContext http) =>
        http.User.FindFirstValue("sub")
        ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "";
}
